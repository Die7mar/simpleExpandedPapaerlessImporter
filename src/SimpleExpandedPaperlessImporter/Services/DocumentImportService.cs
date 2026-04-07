using System.IO.Compression;
using Microsoft.Extensions.Options;
using SimpleExpandedPaperlessImporter.Configuration;
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

            // Resolve default tags
            _tags ??= await paperlessApi.GetAllTagsAsync(ct);
            var tagIds = ResolveTagIds(Settings.DefaultTags);

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
    /// Finds a Paperless correspondent whose sanitized name matches the folder name.
    /// </summary>
    private PaperlessCorrespondent? ResolveCorrespondent(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName) || _correspondents is null)
            return null;

        // Exact match first
        var match = _correspondents.FirstOrDefault(c =>
            string.Equals(c.Name, folderName, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return match;

        // Match against sanitized names (invalid path chars replaced)
        match = _correspondents.FirstOrDefault(c =>
            string.Equals(SanitizeFolderName(c.Name), folderName, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return match;

        logger.LogWarning("No Paperless correspondent found for folder '{FolderName}' – document imported without correspondent", folderName);
        return null;
    }

    private List<int> ResolveTagIds(List<string> tagNames)
    {
        if (_tags is null || tagNames.Count == 0)
            return [];

        return tagNames
            .Select(name => _tags.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Slug, name, StringComparison.OrdinalIgnoreCase)))
            .Where(t => t is not null)
            .Select(t => t!.Id)
            .ToList();
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
            var content = $"[{DateTime.Now:O}] Import error for: {originalFilePath}\n\n{ex}";
            File.WriteAllText(logPath, content);
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
