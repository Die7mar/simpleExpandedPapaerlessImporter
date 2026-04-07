using System.Text.Json;

namespace SimpleExpandedPaperlessImporter.Data;

/// <summary>
/// Stores per-correspondent tag assignments (tag IDs from Paperless).
/// CorrespondentId is the Paperless correspondent ID (primary key).
/// </summary>
public class CorrespondentSettings
{
    public int CorrespondentId { get; set; }

    /// <summary>JSON array of Paperless tag IDs, e.g. [1, 3, 7]</summary>
    public string TagIdsJson { get; set; } = "[]";

    /// <summary>Convenience accessor – deserializes TagIdsJson on every read.</summary>
    public List<int> GetTagIds() =>
        JsonSerializer.Deserialize<List<int>>(TagIdsJson) ?? [];

    public void SetTagIds(IEnumerable<int> ids) =>
        TagIdsJson = JsonSerializer.Serialize(ids.Distinct().OrderBy(x => x).ToList());
}
