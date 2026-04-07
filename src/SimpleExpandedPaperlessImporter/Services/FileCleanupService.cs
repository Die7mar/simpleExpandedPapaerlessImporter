using Microsoft.Extensions.Options;
using SimpleExpandedPaperlessImporter.Configuration;

namespace SimpleExpandedPaperlessImporter.Services;

/// <summary>
/// Background service: deletes files in the done/error folders that are older than RetentionDays.
/// Runs once per hour.
/// </summary>
public class FileCleanupService(
    IOptionsMonitor<PaperlessSettings> optionsMonitor,
    ILogger<FileCleanupService> logger) : BackgroundService
{
    private PaperlessSettings Settings => optionsMonitor.CurrentValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        // Run immediately on startup, then hourly
        CleanFolder(Settings.DoneFolder);
        CleanFolder(Settings.ErrorFolder);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            CleanFolder(Settings.DoneFolder);
            CleanFolder(Settings.ErrorFolder);
        }
    }

    private void CleanFolder(string baseFolder)
    {
        if (!Directory.Exists(baseFolder)) return;

        var cutoff = DateTime.UtcNow.AddDays(-Settings.RetentionDays);

        // Delete old ZIP files (new format)
        foreach (var file in Directory.GetFiles(baseFolder, "*.zip"))
        {
            try
            {
                if (new FileInfo(file).CreationTimeUtc < cutoff)
                {
                    File.Delete(file);
                    logger.LogInformation("Deleted old zip: {File}", file);
                }
            }
            catch (Exception ex) { logger.LogError(ex, "Failed to delete old zip: {File}", file); }
        }

        // Also delete any legacy timestamped sub-folders
        foreach (var subDir in Directory.GetDirectories(baseFolder))
        {
            try
            {
                if (new DirectoryInfo(subDir).CreationTimeUtc < cutoff)
                {
                    Directory.Delete(subDir, recursive: true);
                    logger.LogInformation("Deleted old folder: {Folder}", subDir);
                }
            }
            catch (Exception ex) { logger.LogError(ex, "Failed to delete old folder: {Folder}", subDir); }
        }
    }
}
