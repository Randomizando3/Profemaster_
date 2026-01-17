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
    private static string ClassesPath(string institutionId) =>
    Path.Combine(BaseDir, $"classes_{institutionId}.json");

    private static string StudentsPath(string institutionId, string classroomId) =>
        Path.Combine(BaseDir, $"students_{institutionId}_{classroomId}.json");

    public Task SaveClassesCacheAsync(string institutionId, List<Models.Classroom> list) =>
        SaveAsync(ClassesPath(institutionId), list);

    public Task<List<Models.Classroom>?> LoadClassesCacheAsync(string institutionId) =>
        LoadAsync<List<Models.Classroom>>(ClassesPath(institutionId));

    public Task SaveStudentsCacheAsync(string institutionId, string classroomId, List<Models.StudentContact> list) =>
        SaveAsync(StudentsPath(institutionId, classroomId), list);

    public Task<List<Models.StudentContact>?> LoadStudentsCacheAsync(string institutionId, string classroomId) =>
        LoadAsync<List<Models.StudentContact>>(StudentsPath(institutionId, classroomId));

    private static string AgendaAllPath => Path.Combine(BaseDir, "agenda_all.json");
    private static string AgendaClassPath(string institutionId, string classId) =>
        Path.Combine(BaseDir, $"agenda_{institutionId}_{classId}.json");

    public Task SaveAgendaAllCacheAsync(List<Models.ScheduleEvent> list) =>
        SaveAsync(AgendaAllPath, list);

    public Task<List<Models.ScheduleEvent>?> LoadAgendaAllCacheAsync() =>
        LoadAsync<List<Models.ScheduleEvent>>(AgendaAllPath);

    public Task SaveAgendaClassCacheAsync(string institutionId, string classId, List<Models.ScheduleEvent> list) =>
        SaveAsync(AgendaClassPath(institutionId, classId), list);

    public Task<List<Models.ScheduleEvent>?> LoadAgendaClassCacheAsync(string institutionId, string classId) =>
        LoadAsync<List<Models.ScheduleEvent>>(AgendaClassPath(institutionId, classId));


}
