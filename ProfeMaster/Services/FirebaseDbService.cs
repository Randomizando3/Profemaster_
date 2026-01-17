using System.Net.Http.Json;
using ProfeMaster.Config;
using ProfeMaster.Models;


namespace ProfeMaster.Services;

public sealed class FirebaseDbService
{
    private readonly HttpClient _http;

    public FirebaseDbService(HttpClient http)
    {
        _http = http;
    }

    private static string BaseUrl => FirebaseConfig.RealtimeDbUrl;

    // No MVP com rules abertas, não é obrigatório auth param.
    // Quando você fechar as rules, usaremos ?auth={idToken}
    private static string AuthParam(string? idToken) =>
        string.IsNullOrWhiteSpace(idToken) ? "" : $"?auth={Uri.EscapeDataString(idToken)}";

    public async Task<List<Institution>> GetInstitutionsAsync(string uid, string? idToken)
    {
        var url = $"{BaseUrl}/users/{uid}/institutions.json{AuthParam(idToken)}";
        var dict = await _http.GetFromJsonAsync<Dictionary<string, Institution>>(url);
        if (dict == null) return new List<Institution>();

        // o "Id" pode vir vazio, então garantimos
        foreach (var kv in dict)
        {
            kv.Value.Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? kv.Key : kv.Value.Id;
        }
        return dict.Values.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<bool> UpsertInstitutionAsync(string uid, string? idToken, Institution inst)
    {
        inst.Id = string.IsNullOrWhiteSpace(inst.Id) ? Guid.NewGuid().ToString("N") : inst.Id;
        var url = $"{BaseUrl}/users/{uid}/institutions/{inst.Id}.json{AuthParam(idToken)}";
        var resp = await _http.PutAsJsonAsync(url, inst);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteInstitutionAsync(string uid, string? idToken, string instId)
    {
        var url = $"{BaseUrl}/users/{uid}/institutions/{instId}.json{AuthParam(idToken)}";
        var resp = await _http.DeleteAsync(url);
        return resp.IsSuccessStatusCode;
    }


    public async Task<List<Classroom>> GetClassesAsync(string uid, string institutionId, string? idToken)
    {
        var url = $"{BaseUrl}/users/{uid}/classes/{institutionId}.json{AuthParam(idToken)}";
        var dict = await _http.GetFromJsonAsync<Dictionary<string, Classroom>>(url);
        if (dict == null) return new();

        foreach (var kv in dict)
        {
            kv.Value.Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? kv.Key : kv.Value.Id;
            kv.Value.InstitutionId = institutionId;
        }

        return dict.Values.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<bool> UpsertClassAsync(string uid, string institutionId, string? idToken, Classroom cls)
    {
        cls.Id = string.IsNullOrWhiteSpace(cls.Id) ? Guid.NewGuid().ToString("N") : cls.Id;
        cls.InstitutionId = institutionId;

        var url = $"{BaseUrl}/users/{uid}/classes/{institutionId}/{cls.Id}.json{AuthParam(idToken)}";
        var resp = await _http.PutAsJsonAsync(url, cls);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteClassAsync(string uid, string institutionId, string? idToken, string classId)
    {
        var url = $"{BaseUrl}/users/{uid}/classes/{institutionId}/{classId}.json{AuthParam(idToken)}";
        var resp = await _http.DeleteAsync(url);
        return resp.IsSuccessStatusCode;
    }

    // ===== ALUNOS/CONTATOS =====
    public async Task<List<StudentContact>> GetStudentsAsync(string uid, string institutionId, string classId, string? idToken)
    {
        var url = $"{BaseUrl}/users/{uid}/students/{institutionId}/{classId}.json{AuthParam(idToken)}";
        var dict = await _http.GetFromJsonAsync<Dictionary<string, StudentContact>>(url);
        if (dict == null) return new();

        foreach (var kv in dict)
        {
            kv.Value.Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? kv.Key : kv.Value.Id;
            kv.Value.InstitutionId = institutionId;
            kv.Value.ClassroomId = classId;
        }

        return dict.Values.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<bool> UpsertStudentAsync(string uid, string institutionId, string classId, string? idToken, StudentContact st)
    {
        st.Id = string.IsNullOrWhiteSpace(st.Id) ? Guid.NewGuid().ToString("N") : st.Id;
        st.InstitutionId = institutionId;
        st.ClassroomId = classId;

        var url = $"{BaseUrl}/users/{uid}/students/{institutionId}/{classId}/{st.Id}.json{AuthParam(idToken)}";
        var resp = await _http.PutAsJsonAsync(url, st);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteStudentAsync(string uid, string institutionId, string classId, string? idToken, string studentId)
    {
        var url = $"{BaseUrl}/users/{uid}/students/{institutionId}/{classId}/{studentId}.json{AuthParam(idToken)}";
        var resp = await _http.DeleteAsync(url);
        return resp.IsSuccessStatusCode;
    }

    // ===== AGENDA (GERAL) =====
    public async Task<List<ScheduleEvent>> GetAgendaAllAsync(string uid, string? idToken)
    {
        var url = $"{BaseUrl}/users/{uid}/schedule/all.json{AuthParam(idToken)}";
        var dict = await _http.GetFromJsonAsync<Dictionary<string, ScheduleEvent>>(url);
        if (dict == null) return new();

        foreach (var kv in dict)
            kv.Value.Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? kv.Key : kv.Value.Id;

        return dict.Values
            .OrderBy(x => x.Start)
            .ToList();
    }

    public async Task<bool> UpsertAgendaAllAsync(string uid, string? idToken, ScheduleEvent ev)
    {
        ev.Id = string.IsNullOrWhiteSpace(ev.Id) ? Guid.NewGuid().ToString("N") : ev.Id;

        var url = $"{BaseUrl}/users/{uid}/schedule/all/{ev.Id}.json{AuthParam(idToken)}";
        var resp = await _http.PutAsJsonAsync(url, ev);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAgendaAllAsync(string uid, string? idToken, string eventId)
    {
        var url = $"{BaseUrl}/users/{uid}/schedule/all/{eventId}.json{AuthParam(idToken)}";
        var resp = await _http.DeleteAsync(url);
        return resp.IsSuccessStatusCode;
    }

    // ===== AGENDA (POR TURMA) =====
    public async Task<List<ScheduleEvent>> GetAgendaByClassAsync(string uid, string institutionId, string classId, string? idToken)
    {
        var url = $"{BaseUrl}/users/{uid}/schedule/byClass/{institutionId}/{classId}.json{AuthParam(idToken)}";
        var dict = await _http.GetFromJsonAsync<Dictionary<string, ScheduleEvent>>(url);
        if (dict == null) return new();

        foreach (var kv in dict)
            kv.Value.Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? kv.Key : kv.Value.Id;

        return dict.Values
            .OrderBy(x => x.Start)
            .ToList();
    }

    public async Task<bool> UpsertAgendaByClassAsync(string uid, string institutionId, string classId, string? idToken, ScheduleEvent ev)
    {
        ev.Id = string.IsNullOrWhiteSpace(ev.Id) ? Guid.NewGuid().ToString("N") : ev.Id;

        var url = $"{BaseUrl}/users/{uid}/schedule/byClass/{institutionId}/{classId}/{ev.Id}.json{AuthParam(idToken)}";
        var resp = await _http.PutAsJsonAsync(url, ev);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAgendaByClassAsync(string uid, string institutionId, string classId, string? idToken, string eventId)
    {
        var url = $"{BaseUrl}/users/{uid}/schedule/byClass/{institutionId}/{classId}/{eventId}.json{AuthParam(idToken)}";
        var resp = await _http.DeleteAsync(url);
        return resp.IsSuccessStatusCode;
    }

}
