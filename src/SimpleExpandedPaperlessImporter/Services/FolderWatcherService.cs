using Microsoft.Extensions.Options;
using SimpleExpandedPaperlessImporter.Configuration;

namespace SimpleExpandedPaperlessImporter.Services;

/// <summary>
/// Background service: watches the inbox folder (including all correspondent sub-folders)
/// for new files and hands them off to DocumentImportService.
/// </summary>
public class FolderWatcherService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PaperlessSettings> optionsMonitor,
    ILogger<FolderWatcherService> logger) : BackgroundService
{
    private PaperlessSettings Settings => optionsMonitor.CurrentValue;
    private readonly SemaphoreSlim _importLock = new(4, 4);

    // Tracks files currently being imported to prevent double-processing
    private readonly HashSet<string> _inProgress = [];
    private readonly Lock _inProgressLock = new();

    private FileSystemWatcher? _watcher;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureBaseFolders();
        SetupWatcher(stoppingToken);

        // On startup: process any files that arrived while the service was down
        await ScanAndImportExistingFilesAsync(stoppingToken);

        // Periodic scan as safety net (watcher can miss events under heavy load)
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Settings.PollingIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ScanAndImportExistingFilesAsync(stoppingToken);
    }

    private void EnsureBaseFolders()
    {
        Directory.CreateDirectory(Settings.InboxFolder);
        Directory.CreateDirectory(Settings.DoneFolder);
        Directory.CreateDirectory(Settings.ErrorFolder);
    }

    private void SetupWatcher(CancellationToken stoppingToken)
    {
        if (!Directory.Exists(Settings.InboxFolder)) return;

        _watcher = new FileSystemWatcher(Settings.InboxFolder)
        {
            // Watch ALL sub-directories (correspondent folders) with one watcher
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };

        _watcher.Created += (_, e) =>
        {
            // Ignore new sub-directory creation events (not a file)
            if (Directory.Exists(e.FullPath)) return;
            _ = HandleNewFileAsync(e.FullPath, stoppingToken);
        };

        stoppingToken.Register(() => _watcher?.Dispose());
        logger.LogInformation("Watching inbox folder: {Folder}", Settings.InboxFolder);
    }

    private async Task HandleNewFileAsync(string filePath, CancellationToken ct)
    {
        // Wait for the file to be fully written (retry up to 10 × 1s)
        if (!await WaitForFileReadyAsync(filePath, ct)) return;
        await EnqueueImportAsync(filePath, ct);
    }

    /// <summary>
    /// Waits until the file can be opened for reading (not locked by the writer).
    /// Returns false if the file disappears or is still locked after all retries.
    /// </summary>
    private async Task<bool> WaitForFileReadyAsync(string filePath, CancellationToken ct, int maxRetries = 10)
    {
        for (var i = 0; i < maxRetries; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            if (!File.Exists(filePath)) return false;

            try
            {
                await using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return true; // File is readable and not locked
            }
            catch (IOException)
            {
                logger.LogDebug("File still locked, retrying ({Attempt}/{Max}): {File}", i + 1, maxRetries, filePath);
            }
        }

        logger.LogWarning("File still locked after {Max} retries, skipping: {File}", maxRetries, filePath);
        return false;
    }

    private async Task EnqueueImportAsync(string filePath, CancellationToken ct)
    {
        if (!IsImportableFile(filePath)) return;

        // Prevent double-import
        lock (_inProgressLock)
        {
            if (_inProgress.Contains(filePath)) return;
            _inProgress.Add(filePath);
        }

        await _importLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(filePath))
            {
                logger.LogDebug("File already processed, skipping: {File}", filePath);
                return;
            }

            var correspondentFolder = GetCorrespondentFolderName(filePath);

            await using var scope = scopeFactory.CreateAsyncScope();
            var importService = scope.ServiceProvider.GetRequiredService<DocumentImportService>();
            await importService.ImportFileAsync(filePath, correspondentFolder, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error importing '{File}'", filePath);
        }
        finally
        {
            lock (_inProgressLock)
                _inProgress.Remove(filePath);

            _importLock.Release();
        }
    }

    private async Task ScanAndImportExistingFilesAsync(CancellationToken ct)
    {
        if (!Directory.Exists(Settings.InboxFolder)) return;

        var files = Directory.GetFiles(Settings.InboxFolder, "*.*", SearchOption.AllDirectories)
            .Where(IsImportableFile)
            .ToList();

        if (files.Count > 0)
            logger.LogInformation("Scan found {Count} file(s) to import", files.Count);

        foreach (var file in files)
            await EnqueueImportAsync(file, ct);
    }

    private string? GetCorrespondentFolderName(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir is null) return null;
        if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(Settings.InboxFolder),
                StringComparison.OrdinalIgnoreCase))
            return null; // root inbox → no correspondent
        return Path.GetFileName(dir);
    }

    private static bool IsImportableFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".pdf" or ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".txt" or ".eml" or ".msg";
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }
}

