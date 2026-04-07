using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimpleExpandedPaperlessImporter.Configuration;
using SimpleExpandedPaperlessImporter.Data;
using SimpleExpandedPaperlessImporter.Models;

namespace SimpleExpandedPaperlessImporter.Services;

/// <summary>
/// Core import logic: resolves correspondent, converts emails, uploads to Paperless,
/// then zips the file into the done or error folder.
/// </summary>
public class DocumentImportService(
    PaperlessApiService paperlessApi,
    EmailConverterService emailConverter,
    ImportStatusService statusService,
    IOptionsMonitor<PaperlessSettings> optionsMonitor,
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentImportService> logger)
{
    private PaperlessSettings Settings => optionsMonitor.CurrentValue;

    // Cache correspondents and tags so we don't hit the API on every file
    private List<PaperlessCorrespondent>? _correspondents;
    private List<PaperlessTag>? _tags;

    public async Task ImportFileAsync(string filePath, string? correspondentFolderName, CancellationToken ct = default)
    {
        var job = statusService.AddJob(filePath, correspondentFolderName);
        statusService.MarkImporting(job);

        string? workingPath = filePath;
        try
        {
            // Convert .eml to PDF first
            if (Path.GetExtension(filePath).Equals(".eml", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(filePath).Equals(".msg", StringComparison.OrdinalIgnoreCase))
            {
                workingPath = await emailConverter.ConvertEmailToPdfAsync(filePath, ct);
                logger.LogInformation("Email converted: {Path}", workingPath);
            }

            // Resolve correspondent
            _correspondents ??= await paperlessApi.GetAllCorrespondentsAsync(ct);
            var correspondent = ResolveCorrespondent(correspondentFolderName);

            // Merge global default tags + per-correspondent tags
            _tags ??= await paperlessApi.GetAllTagsAsync(ct);
            var tagIds = await MergeTagIdsAsync(Settings.DefaultTags, correspondent?.Id, ct);

            var tagNames = tagIds
                .Select(id => _tags.FirstOrDefault(t => t.Id == id)?.Name ?? id.ToString())
                .ToList();
            logger.LogInformation(
                "Folder import '{File}' → correspondent='{Correspondent}', tags=[{Tags}]",
                Path.GetFileName(filePath),
                correspondent?.Name ?? "none",
                string.Join(", ", tagNames));

            // Upload
            var taskId = await paperlessApi.UploadDocumentAsync(
                workingPath,
                Path.GetFileName(workingPath),
                correspondent?.Id,
                tagIds,
                ct);

            statusService.MarkDone(job, taskId);

            // Zip original + converted file into done folder
            var filesToZip = new List<string> { filePath };
            if (workingPath != filePath && File.Exists(workingPath))
                filesToZip.Add(workingPath);
            ZipToFolder(filesToZip, Settings.DoneFolder, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import failed for '{FilePath}'", filePath);
            statusService.MarkError(job, ex.Message);

            // Zip original + error log into error folder
            var errorLogPath = WriteErrorLog(filePath, ex);
            var filesToZip = new List<string> { filePath };
            if (workingPath != filePath && workingPath is not null && File.Exists(workingPath))
                filesToZip.Add(workingPath);
            if (errorLogPath is not null)
                filesToZip.Add(errorLogPath);
            SafeZip(filesToZip, Settings.ErrorFolder);
        }
    }

    /// <summary>
    /// Handles a direct web upload: logs via ImportStatusService, uploads to Paperless.
    /// Default tags (from settings) and correspondent-specific tags are merged server-side
    /// on top of the user-selected tagIds, ensuring they are always applied unless
    /// the user explicitly unchecked them in the UI (in which case they won't be in tagIds).
    /// </summary>
    public async Task ImportFromWebAsync(
        string tempFilePath,
        string originalFileName,
        int? correspondentId,
        IEnumerable<int> uiTagIds,
        CancellationToken ct = default)
    {
        // Load correspondents + tags for name resolution and logging
        _correspondents ??= await paperlessApi.GetAllCorrespondentsAsync(ct);
        _tags           ??= await paperlessApi.GetAllTagsAsync(ct);

        var correspondentName = correspondentId.HasValue
            ? _correspondents.FirstOrDefault(c => c.Id == correspondentId.Value)?.Name
            : null;

        // Merge: user-selected tags + default tags + correspondent tags (server-side safety net)
        var finalTagIds = new HashSet<int>(uiTagIds);
        var serverTagIds = await MergeTagIdsAsync(Settings.DefaultTags, correspondentId, ct);
        // Only add server-side default/correspondent tags that are already selected in UI.
        // This preserves the user's ability to uncheck them (they won't be in uiTagIds if unchecked).
        // Exception: correspondent-specific tags from DB are always applied (consistent with folder import).
        if (correspondentId.HasValue)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cSettings = await db.CorrespondentSettings
                .FirstOrDefaultAsync(cs => cs.CorrespondentId == correspondentId.Value, ct);
            if (cSettings is not null)
                foreach (var id in cSettings.GetTagIds())
                    finalTagIds.Add(id); // always apply correspondent tags
        }

        var tagNames = finalTagIds
            .Select(id => _tags.FirstOrDefault(t => t.Id == id)?.Name ?? id.ToString())
            .ToList();
        logger.LogInformation(
            "Web upload '{File}' → correspondent='{Correspondent}', tags=[{Tags}]",
            originalFileName,
            correspondentName ?? "none",
            string.Join(", ", tagNames));

        var job = statusService.AddJob(originalFileName, correspondentName);
        statusService.MarkImporting(job);

        try
        {
            var taskId = await paperlessApi.UploadDocumentAsync(
                tempFilePath, originalFileName, correspondentId, [.. finalTagIds], ct);

            statusService.MarkDone(job, taskId);
            logger.LogInformation("Web upload '{File}' accepted by Paperless, task: {Task}", originalFileName, taskId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Web upload failed for '{File}'", originalFileName);
            statusService.MarkError(job, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Merges global default tags (by name) with per-correspondent tags (by ID from DB).
    /// </summary>
    private async Task<List<int>> MergeTagIdsAsync(List<string> defaultTagNames, int? correspondentId, CancellationToken ct)
    {
        var ids = new HashSet<int>(ResolveTagIds(defaultTagNames));

        if (correspondentId.HasValue)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.CorrespondentSettings
                .FirstOrDefaultAsync(cs => cs.CorrespondentId == correspondentId.Value, ct);
            if (settings is not null)
                foreach (var id in settings.GetTagIds())
                    ids.Add(id);
        }

        return [.. ids];
    }

    /// <summary>
    /// Finds a Paperless correspondent whose sanitized name matches the folder name.
    /// </summary>
    private PaperlessCorrespondent? ResolveCorrespondent(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName) || _correspondents is null)
            return null;

        var match = _correspondents.FirstOrDefault(c =>
            string.Equals(c.Name, folderName, StringComparison.OrdinalIgnoreCase));

        if (match is not null) return match;

        match = _correspondents.FirstOrDefault(c =>
            string.Equals(SanitizeFolderName(c.Name), folderName, StringComparison.OrdinalIgnoreCase));

        if (match is not null) return match;

        logger.LogWarning("No Paperless correspondent found for folder '{FolderName}' – document imported without correspondent", folderName);
        return null;
    }

    private List<int> ResolveTagIds(List<string> tagNames)
    {
        if (_tags is null || tagNames.Count == 0)
            return [];

        var result = new List<int>();
        foreach (var name in tagNames)
        {
            var tag = _tags.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Slug, name, StringComparison.OrdinalIgnoreCase));

            if (tag is not null)
                result.Add(tag.Id);
            else
                logger.LogWarning(
                    "Default tag '{TagName}' not found in Paperless – it will not be applied. " +
                    "Check Settings → DefaultTags and verify the tag exists in Paperless.",
                    name);
        }
        return result;
    }

    /// <summary>Zips the given files into a timestamped .zip in the target folder, then deletes the originals.</summary>
    private void ZipToFolder(List<string> filePaths, string targetBaseFolder, string? extraContent)
    {
        Directory.CreateDirectory(targetBaseFolder);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var baseName  = Path.GetFileNameWithoutExtension(filePaths.First());
        var zipPath   = Path.Combine(targetBaseFolder, $"{timestamp}_{baseName}.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var f in filePaths.Where(File.Exists))
            {
                zip.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.SmallestSize);
                File.Delete(f);
                logger.LogInformation("Zipped '{File}' → '{Zip}'", f, zipPath);
            }
        }
    }

    private void SafeZip(List<string> filePaths, string targetBaseFolder)
    {
        try { ZipToFolder(filePaths, targetBaseFolder, null); }
        catch (Exception ex) { logger.LogError(ex, "Could not zip files to error folder"); }
    }

    /// <summary>Writes the error log to a temp file and returns its path.</summary>
    private string? WriteErrorLog(string originalFilePath, Exception ex)
    {
        try
        {
            var logPath = Path.Combine(
                Path.GetDirectoryName(originalFilePath) ?? Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(originalFilePath) + "_error.log");
            File.WriteAllText(logPath, $"[{DateTime.Now:O}] Import error for: {originalFilePath}\n\n{ex}");
            return logPath;
        }
        catch (Exception logEx)
        {
            logger.LogError(logEx, "Failed to write error log");
            return null;
        }
    }

    /// <summary>Replaces characters that are invalid in directory names.</summary>
    public static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars()
            .Concat(Path.GetInvalidPathChars())
            .Distinct()
            .ToArray();
        return string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c)).Trim();
    }
}
