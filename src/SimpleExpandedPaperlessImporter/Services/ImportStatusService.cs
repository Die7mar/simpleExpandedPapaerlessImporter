using Microsoft.EntityFrameworkCore;
using SimpleExpandedPaperlessImporter.Data;
using SimpleExpandedPaperlessImporter.Models;

namespace SimpleExpandedPaperlessImporter.Services;

/// <summary>
/// In-memory store for active and recent import jobs, shared with the Blazor UI.
/// Persists all jobs to SQLite via AppDbContext.
/// </summary>
public class ImportStatusService(IServiceScopeFactory scopeFactory, ILogger<ImportStatusService> logger)
{
    private readonly List<ImportJob> _jobs = [];
    private readonly Lock _lock = new();

    public event Action? OnChange;

    /// <summary>Load the most recent 500 jobs from SQLite on startup.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

            var entities = await db.ImportJobs
                .OrderByDescending(j => j.StartedAt)
                .Take(500)
                .ToListAsync();

            lock (_lock)
            {
                _jobs.Clear();
                _jobs.AddRange(entities.Select(e => e.ToJob()));
            }

            logger.LogInformation("Loaded {Count} import jobs from database", entities.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize import job history from database");
        }
    }

    public IReadOnlyList<ImportJob> Jobs
    {
        get { lock (_lock) return _jobs.ToList(); }
    }

    public IReadOnlyList<ImportJob> ActiveJobs =>
        Jobs.Where(j => j.State is ImportState.Pending or ImportState.Importing).ToList();

    public IReadOnlyList<ImportJob> ErrorJobs =>
        Jobs.Where(j => j.State == ImportState.Error).OrderByDescending(j => j.FinishedAt).ToList();

    public IReadOnlyList<ImportJob> DoneJobs =>
        Jobs.Where(j => j.State == ImportState.Done).OrderByDescending(j => j.FinishedAt).ToList();

    public ImportJob AddJob(string filePath, string? correspondentName)
    {
        var job = new ImportJob
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            CorrespondentName = correspondentName,
            State = ImportState.Pending,
            StartedAt = DateTime.UtcNow
        };
        lock (_lock)
            _jobs.Add(job);
        _ = PersistJobAsync(job);
        NotifyChange();
        return job;
    }

    public void MarkImporting(ImportJob job)
    {
        job.State = ImportState.Importing;
        job.StartedAt = DateTime.UtcNow;
        _ = PersistJobAsync(job);
        NotifyChange();
    }

    public void MarkDone(ImportJob job, string? paperlessTaskId)
    {
        job.State = ImportState.Done;
        job.FinishedAt = DateTime.UtcNow;
        job.PaperlessDocumentId = paperlessTaskId;
        _ = PersistJobAsync(job);
        NotifyChange();
    }

    public void MarkError(ImportJob job, string errorMessage)
    {
        job.State = ImportState.Error;
        job.FinishedAt = DateTime.UtcNow;
        job.ErrorMessage = errorMessage;
        _ = PersistJobAsync(job);
        NotifyChange();
    }

    /// <summary>Removes in-memory jobs older than the given age to keep the list manageable.</summary>
    public void PruneOldJobs(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        lock (_lock)
            _jobs.RemoveAll(j =>
                j.State is ImportState.Done or ImportState.Error &&
                j.FinishedAt.HasValue &&
                j.FinishedAt.Value < cutoff);
        NotifyChange();
    }

    private async Task PersistJobAsync(ImportJob job)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var entity = await db.ImportJobs.FindAsync(job.Id);
            if (entity is null)
            {
                db.ImportJobs.Add(ImportJobEntity.FromJob(job));
            }
            else
            {
                entity.State = job.State;
                entity.FinishedAt = job.FinishedAt;
                entity.ErrorMessage = job.ErrorMessage;
                entity.PaperlessTaskId = job.PaperlessDocumentId;
                entity.StartedAt = job.StartedAt;
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist import job {JobId} to database", job.Id);
        }
    }

    private void NotifyChange() => OnChange?.Invoke();
}

