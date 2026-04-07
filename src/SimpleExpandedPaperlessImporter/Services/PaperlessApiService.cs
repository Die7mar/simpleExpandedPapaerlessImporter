using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SimpleExpandedPaperlessImporter.Configuration;
using SimpleExpandedPaperlessImporter.Models;

namespace SimpleExpandedPaperlessImporter.Services;

public class PaperlessApiService(HttpClient httpClient, IOptionsMonitor<PaperlessSettings> optionsMonitor, ILogger<PaperlessApiService> logger)
{
    private PaperlessSettings Settings => optionsMonitor.CurrentValue;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private HttpRequestMessage AuthRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Token", Settings.ApiToken);
        return req;
    }

    public async Task<List<PaperlessCorrespondent>> GetAllCorrespondentsAsync(CancellationToken ct = default)
    {
        var result = new List<PaperlessCorrespondent>();
        var url = $"{Settings.BaseUrl.TrimEnd('/')}/api/correspondents/?page_size=500";

        while (url is not null)
        {
            using var req = AuthRequest(HttpMethod.Get, url);
            var response = await httpClient.SendAsync(req, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"Paperless GET correspondents failed ({response.StatusCode}): {body}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var page = JsonSerializer.Deserialize<PaperlessListResponse<PaperlessCorrespondent>>(json, JsonOpts);
            if (page is null) break;
            result.AddRange(page.Results);
            url = page.Next;
        }

        logger.LogInformation("Loaded {Count} correspondents from Paperless", result.Count);
        return result;
    }

    public async Task<List<PaperlessTag>> GetAllTagsAsync(CancellationToken ct = default)
    {
        var result = new List<PaperlessTag>();
        var url = $"{Settings.BaseUrl.TrimEnd('/')}/api/tags/?page_size=500";

        while (url is not null)
        {
            using var req = AuthRequest(HttpMethod.Get, url);
            var response = await httpClient.SendAsync(req, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"Paperless GET tags failed ({response.StatusCode}): {body}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var page = JsonSerializer.Deserialize<PaperlessListResponse<PaperlessTag>>(json, JsonOpts);
            if (page is null) break;
            result.AddRange(page.Results);
            url = page.Next;
        }

        logger.LogInformation("Loaded {Count} tags from Paperless", result.Count);
        return result;
    }

    /// <summary>
    /// Uploads a document to Paperless. Returns the task ID assigned by Paperless.
    /// </summary>
    public async Task<string?> UploadDocumentAsync(
        string filePath,
        string fileName,
        int? correspondentId,
        IEnumerable<int> tagIds,
        CancellationToken ct = default)
    {
        var url = $"{Settings.BaseUrl.TrimEnd('/')}/api/documents/post_document/";

        logger.LogInformation("Uploading '{FileName}' to Paperless (correspondent={Cid})", fileName, correspondentId?.ToString() ?? "none");

        // Retry up to 3 times for transient network errors
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var req = AuthRequest(HttpMethod.Post, url);
                using var content = new MultipartFormDataContent();

                // Re-open the file for each attempt
                await using var fileStream = File.OpenRead(filePath);
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(fileName));
                content.Add(fileContent, "document", fileName);

                if (correspondentId.HasValue)
                    content.Add(new StringContent(correspondentId.Value.ToString()), "correspondent");

                foreach (var tagId in tagIds)
                    content.Add(new StringContent(tagId.ToString()), "tags");

                req.Content = content;
                var response = await httpClient.SendAsync(req, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"Paperless upload failed ({(int)response.StatusCode} {response.StatusCode}): {body}");

                var taskId = body.Trim('"');
                logger.LogInformation("Document '{FileName}' accepted by Paperless, task ID: {TaskId}", fileName, taskId);
                return taskId;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt < 3)
            {
                logger.LogWarning(ex, "Upload attempt {Attempt}/3 failed for '{File}', retrying…", attempt, fileName);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
        }

        throw new InvalidOperationException($"Upload failed after 3 attempts for '{fileName}'");
    }

    private static string GetMimeType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".tif" or ".tiff" => "image/tiff",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
}

