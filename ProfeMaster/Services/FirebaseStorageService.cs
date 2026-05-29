using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ProfeMaster.Config;

namespace ProfeMaster.Services;

public sealed class FirebaseStorageService
{
    private readonly HttpClient _http;

    public FirebaseStorageService(HttpClient http)
    {
        _http = http;
    }

    public async Task<(bool ok, string downloadUrl, string storagePath, string error)> UploadAsync(
        string idToken,
        string storagePath,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken ct = default)
    {
        var bucket = FirebaseConfig.StorageBucket;
        if (string.IsNullOrWhiteSpace(bucket))
            return (false, "", storagePath, "Firebase Storage não configurado.");

        if (string.IsNullOrWhiteSpace(idToken))
            return (false, "", storagePath, "Sessão inválida para upload.");

        storagePath = NormalizeStoragePath(storagePath, fileName);
        contentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;

        var endpoint =
            $"https://firebasestorage.googleapis.com/v0/b/{Uri.EscapeDataString(bucket)}/o" +
            $"?uploadType=media&name={Uri.EscapeDataString(storagePath)}";

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        req.Content = new StreamContent(content);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await ReadErrorAsync(resp);
            return (false, "", storagePath, string.IsNullOrWhiteSpace(err) ? "Falha ao enviar arquivo." : err);
        }

        var body = await resp.Content.ReadFromJsonAsync<UploadResponse>(cancellationToken: ct);
        var path = string.IsNullOrWhiteSpace(body?.Name) ? storagePath : body.Name;
        var token = body?.DownloadTokens?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

        var downloadUrl =
            $"https://firebasestorage.googleapis.com/v0/b/{Uri.EscapeDataString(bucket)}/o/{Uri.EscapeDataString(path)}?alt=media";

        if (!string.IsNullOrWhiteSpace(token))
            downloadUrl += $"&token={Uri.EscapeDataString(token)}";

        return (true, downloadUrl, path, "");
    }

    private static string NormalizeStoragePath(string storagePath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            storagePath = fileName;

        storagePath = storagePath.Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(storagePath) ? Guid.NewGuid().ToString("N") : storagePath;
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage resp)
    {
        try
        {
            var text = await resp.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(text) ? $"{(int)resp.StatusCode}: {resp.ReasonPhrase}" : text;
        }
        catch
        {
            return $"{(int)resp.StatusCode}: {resp.ReasonPhrase}";
        }
    }

    private sealed class UploadResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("downloadTokens")]
        public string DownloadTokens { get; set; } = "";
    }
}
