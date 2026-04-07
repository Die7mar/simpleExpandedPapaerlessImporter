using SimpleExpandedPaperlessImporter.Models;

namespace SimpleExpandedPaperlessImporter.Data;

/// <summary>EF Core entity – maps directly to the ImportJobs SQLite table.</summary>
public class ImportJobEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? CorrespondentName { get; set; }
    public ImportState State { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PaperlessTaskId { get; set; }

    public static ImportJobEntity FromJob(ImportJob job) => new()
    {
        Id = job.Id,
        FileName = job.FileName,
        FilePath = job.FilePath,
        CorrespondentName = job.CorrespondentName,
        State = job.State,
        StartedAt = job.StartedAt,
        FinishedAt = job.FinishedAt,
        ErrorMessage = job.ErrorMessage,
        PaperlessTaskId = job.PaperlessDocumentId
    };

    public ImportJob ToJob() => new()
    {
        Id = Id,
        FileName = FileName,
        FilePath = FilePath,
        CorrespondentName = CorrespondentName,
        State = State,
        StartedAt = StartedAt,
        FinishedAt = FinishedAt,
        ErrorMessage = ErrorMessage,
        PaperlessDocumentId = PaperlessTaskId
    };
}
