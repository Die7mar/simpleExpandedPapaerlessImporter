namespace SimpleExpandedPaperlessImporter.Models;

public enum ImportState
{
    Pending,
    Importing,
    Done,
    Error
}

public class ImportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? CorrespondentName { get; set; }
    public ImportState State { get; set; } = ImportState.Pending;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PaperlessDocumentId { get; set; }
}
