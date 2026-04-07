namespace SimpleExpandedPaperlessImporter.Configuration;

public class PaperlessSettings
{
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public string ApiToken { get; set; } = "";
    public List<string> DefaultTags { get; set; } = [];
    public string InboxFolder { get; set; } = "/importer/inbox";
    public string DoneFolder { get; set; } = "/importer/done";
    public string ErrorFolder { get; set; } = "/importer/error";
    public int RetentionDays { get; set; } = 7;
    /// <summary>Polling-Intervall in Sekunden für den Folder-Scan (zusätzlich zu FileSystemWatcher)</summary>
    public int PollingIntervalSeconds { get; set; } = 30;
}
