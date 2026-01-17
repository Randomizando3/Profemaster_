using System.Text.Json;

namespace ProfeMaster.Services;

public sealed class LocalStore
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static string BaseDir => FileSystem.AppDataDirectory;
    private static string SessionPath => Path.Combine(BaseDir, "session.json");
    private static string InstitutionsPath => Path.Combine(BaseDir, "institutions_cache.json");

    public async Task SaveAsync<T>(string path, T data)
    {
        Directory.CreateDirectory(BaseDir);
        var json = JsonSerializer.Serialize(data, _json);
        await File.WriteAllTextAsync(path, json);
    }

    public async Task<T?> LoadAsync<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<T>(json, _json);
        }
        catch
        {
            return default;
        }
    }

    public Task SaveSessionAsync(Models.UserSession session) => SaveAsync(SessionPath, session);
    public Task<Models.UserSession?> LoadSessionAsync() => LoadAsync<Models.UserSession>(SessionPath);
    public Task ClearSessionAsync()
    {
        try { if (File.Exists(SessionPath)) File.Delete(SessionPath); } catch { }
        return Task.CompletedTask;
    }

    public Task SaveInstitutionsCacheAsync(List<Models.Institution> list) => SaveAsync(InstitutionsPath, list);
    public Task<List<Models.Institution>?> LoadInstitutionsCacheAsync() => LoadAsync<List<Models.Institution>>(InstitutionsPath);
}
