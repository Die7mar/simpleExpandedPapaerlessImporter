namespace SimpleExpandedPaperlessImporter.Models;

public class PaperlessCorrespondent
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
}

public class PaperlessTag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
}

public class PaperlessListResponse<T>
{
    public int Count { get; set; }
    public string? Next { get; set; }
    public List<T> Results { get; set; } = [];
}
