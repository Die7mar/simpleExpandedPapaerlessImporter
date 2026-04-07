using Microsoft.Extensions.Options;
using SimpleExpandedPaperlessImporter.Configuration;

namespace SimpleExpandedPaperlessImporter.Services;

/// <summary>
/// Dedicated background service that creates a sub-folder in the inbox for every
/// correspondent found in Paperless.
///
/// Startup behaviour: retries every 10 s until the first successful sync (Paperless
/// might still be starting up when this service runs).
/// Steady-state: re-syncs once per hour so new correspondents get their folder
/// automatically.
/// </summary>
public class CorrespondentSyncService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PaperlessSettings> optionsMonitor,
    ILogger<CorrespondentSyncService> logger) : BackgroundService
{
    private PaperlessSettings Settings => optionsMonitor.CurrentValue;

    // Exposed so the Blazor UI can trigger a manual sync
    public DateTime? LastSyncUtc { get; private set; }
    public int LastSyncCount { get; private set; }
    public string? LastError { get; private set; }

    private readonly SemaphoreSlim _syncLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ── Startup: keep retrying every 10 s until first success ────────────
        while (!stoppingToken.IsCancellationRequested)
        {
            var success = await TrySyncAsync(stoppingToken);
            if (success) break;
            logger.LogWarning("Correspondent folder sync failed – retrying in 10 s …");
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }

        // ── Steady-state: re-sync once per hour ──────────────────────────────
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await TrySyncAsync(stoppingToken);
    }

    /// <summary>
    /// Creates a folder for every Paperless correspondent that does not yet have one.
    /// Returns true on success, false on any error.
    /// </summary>
    public async Task<bool> TrySyncAsync(CancellationToken ct = default)
    {
        await _syncLock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Settings.InboxFolder);

            await using var scope = scopeFactory.CreateAsyncScope();
            var api = scope.ServiceProvider.GetRequiredService<PaperlessApiService>();

            var correspondents = await api.GetAllCorrespondentsAsync(ct);

            int created = 0;
            foreach (var c in correspondents)
            {
                var folderName = DocumentImportService.SanitizeFolderName(c.Name);
                if (string.IsNullOrWhiteSpace(folderName)) continue;

                var folderPath = Path.Combine(Settings.InboxFolder, folderName);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                    logger.LogInformation("Created correspondent folder: {Path}", folderPath);
                    created++;
                }
            }

            LastSyncUtc = DateTime.UtcNow;
            LastSyncCount = correspondents.Count;
            LastError = null;

            logger.LogInformation(
                "Correspondent sync complete – {Total} correspondents, {Created} new folders",
                correspondents.Count, created);

            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            logger.LogError(ex, "Correspondent folder sync failed");
            return false;
        }
        finally
        {
            _syncLock.Release();
        }
    }
}
