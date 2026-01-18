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

    private static string AuthParam(string? idToken) =>
        string.IsNullOrWhiteSpace(idToken) ? "" : $"?auth={Uri.EscapeDataString(idToken)}";

    public async Task<List<Institution>> GetInstitutionsAsync(string uid, string? idToken)
    {
        var url = $"{BaseUrl}/users/{uid}/institutions.json{AuthParam(idToken)}";
        var dict = await _http.GetFromJsonAsync<Dictionary<string, Institution>>(url);
        if (dict == null) return new List<Institution>();

        foreach (var kv in dict)
            kv.Value.Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? kv.Key : kv.Value.Id;

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

        return dict.Values.OrderBy(x => x.Start).ToList();
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

        return dict.Values.OrderBy(x => x.Start).ToList();
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

    // ===== PLANOS (GERAL) =====
    public async Task<List<LessonPlan>> GetPlansAllAsync(string uid, string? idToken)
    {
        var url = $"{BaseUrl}/users/{uid}/plans/all.json{AuthParam(idToken)}";
        var dict = await _http.GetFromJsonAsync<Dictionary<string, LessonPlan>>(url);
        if (dict == null) return new();

        foreach (var kv in dict)
            kv.Value.Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? kv.Key : kv.Value.Id;

        return dict.Values.OrderByDescending(x => x.Date).ToList();
    }

    public async Task<bool> UpsertPlanAllAsync(string uid, string? idToken, LessonPlan plan)
    {
        plan.Id = string.IsNullOrWhiteSpace(plan.Id) ? Guid.NewGuid().ToString("N") : plan.Id;

        var url = $"{BaseUrl}/users/{uid}/plans/all/{plan.Id}.json{AuthParam(idToken)}";
        var resp = await _http.PutAsJsonAsync(url, plan);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeletePlanAllAsync(string uid, string? idToken, string planId)
    {
        var url = $"{BaseUrl}/users/{uid}/plans/all/{planId}.json{AuthParam(idToken)}";
        var resp = await _http.DeleteAsync(url);
        return resp.IsSuccessStatusCode;
    }

    // ===== PLANOS (POR TURMA) =====
    public async Task<List<LessonPlan>> GetPlansByClassAsync(string uid, string institutionId, string classId, string? idToken)
    {
        var url = $"{BaseUrl}/users/{uid}/plans/byClass/{institutionId}/{classId}.json{AuthParam(idToken)}";
        var dict = await _http.GetFromJsonAsync<Dictionary<string, LessonPlan>>(url);
        if (dict == null) return new();

        foreach (var kv in dict)
            kv.Value.Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? kv.Key : kv.Value.Id;

        return dict.Values.OrderByDescending(x => x.Date).ToList();
    }

    public async Task<bool> UpsertPlanByClassAsync(string uid, string institutionId, string classId, string? idToken, LessonPlan plan)
    {
        plan.Id = string.IsNullOrWhiteSpace(plan.Id) ? Guid.NewGuid().ToString("N") : plan.Id;

        var url = $"{BaseUrl}/users/{uid}/plans/byClass/{institutionId}/{classId}/{plan.Id}.json{AuthParam(idToken)}";
        var resp = await _http.PutAsJsonAsync(url, plan);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeletePlanByClassAsync(string uid, string institutionId, string classId, string? idToken, string planId)
    {
        var url = $"{BaseUrl}/users/{uid}/plans/byClass/{institutionId}/{classId}/{planId}.json{AuthParam(idToken)}";
        var resp = await _http.DeleteAsync(url);
        return resp.IsSuccessStatusCode;
    }

    public async Task<List<ScheduleEvent>> GetAgendaAllRecentAsync(string uid, string? idToken, int daysBack = 90, int daysForward = 365)
    {
        var all = await GetAgendaAllAsync(uid, idToken);
        var min = DateTime.Today.AddDays(-daysBack);
        var max = DateTime.Today.AddDays(daysForward);
        return all.Where(x => x.Start >= min && x.Start <= max)
                  .OrderBy(x => x.Start)
                  .ToList();
    }

    public async Task<List<LessonPlan>> GetPlansLinkedToEventAsync(string uid, string eventId, string? idToken)
    {
        var all = await GetPlansAllAsync(uid, idToken);
        return all.Where(p => p.LinkedEventId == eventId)
                  .OrderByDescending(p => p.Date)
                  .ToList();
    }

    // ===== PROVAS =====
    public async Task<List<ExamItem>> GetExamsAllAsync(string uid, string? idToken)
    {
        var url = $"{BaseUrl}/users/{uid}/exams/all.json{AuthParam(idToken)}";
        var dict = await _http.GetFromJsonAsync<Dictionary<string, ExamItem>>(url);
        if (dict == null) return new();

        foreach (var kv in dict)
            kv.Value.Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? kv.Key : kv.Value.Id;

        return dict.Values.ToList();
    }

    public async Task<bool> UpsertExamAsync(string uid, string? idToken, ExamItem item)
    {
        item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;

        var url = $"{BaseUrl}/users/{uid}/exams/all/{item.Id}.json{AuthParam(idToken)}";
        var resp = await _http.PutAsJsonAsync(url, item);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteExamAsync(string uid, string? idToken, string examId)
    {
        var url = $"{BaseUrl}/users/{uid}/exams/all/{examId}.json{AuthParam(idToken)}";
        var resp = await _http.DeleteAsync(url);
        return resp.IsSuccessStatusCode;
    }

    // ===== EVENTOS =====
    public async Task<List<EventItem>> GetEventsAllAsync(string uid, string? idToken)
    {
        var url = $"{BaseUrl}/users/{uid}/events/all.json{AuthParam(idToken)}";
        var dict = await _http.GetFromJsonAsync<Dictionary<string, EventItem>>(url);
        if (dict == null) return new();

        foreach (var kv in dict)
            kv.Value.Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? kv.Key : kv.Value.Id;

        return dict.Values.ToList();
    }

    public async Task<bool> UpsertEventAsync(string uid, string? idToken, EventItem item)
    {
        item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;

        var url = $"{BaseUrl}/users/{uid}/events/all/{item.Id}.json{AuthParam(idToken)}";
        var resp = await _http.PutAsJsonAsync(url, item);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteEventAsync(string uid, string? idToken, string eventId)
    {
        var url = $"{BaseUrl}/users/{uid}/events/all/{eventId}.json{AuthParam(idToken)}";
        var resp = await _http.DeleteAsync(url);
        return resp.IsSuccessStatusCode;
    }

    // ==========================================================
    // Helpers genéricos (corrige GetListAsync/PutAsync/DeleteAsync)
    // ==========================================================
    private async Task<List<T>> GetListAsync<T>(string relativePath, string? idToken)
    {
        // relativePath SEM ".json"
        var url = $"{BaseUrl}/{relativePath}.json{AuthParam(idToken)}";

        // Firebase RTDB retorna objeto {id: value}
        var dict = await _http.GetFromJsonAsync<Dictionary<string, T>>(url);
        if (dict == null) return new List<T>();

        // Se o model tiver "Id" (string), tentamos preencher automaticamente
        foreach (var kv in dict)
        {
            var obj = kv.Value;
            if (obj == null) continue;

            var prop = obj.GetType().GetProperty("Id");
            if (prop != null && prop.PropertyType == typeof(string))
            {
                var cur = prop.GetValue(obj) as string;
                if (string.IsNullOrWhiteSpace(cur))
                    prop.SetValue(obj, kv.Key);
            }
        }

        return dict.Values.Where(v => v != null).ToList()!;
    }

    private async Task<bool> PutAsync<T>(string relativePath, string? idToken, T payload)
    {
        var url = $"{BaseUrl}/{relativePath}.json{AuthParam(idToken)}";
        var resp = await _http.PutAsJsonAsync(url, payload);
        return resp.IsSuccessStatusCode;
    }

    private async Task<bool> DeleteAsync(string relativePath, string? idToken)
    {
        var url = $"{BaseUrl}/{relativePath}.json{AuthParam(idToken)}";
        var resp = await _http.DeleteAsync(url);
        return resp.IsSuccessStatusCode;
    }

    // ===== AULAS (AVULSAS) =====
    // Obs: aqui o token está como string (não-null). Se preferir, altere para string?
    public Task<List<Lesson>> GetLessonsAllAsync(string uid, string token)
        => GetListAsync<Lesson>($"users/{uid}/lessons/all", token);

    public Task<List<Lesson>> GetLessonsByClassAsync(string uid, string institutionId, string classId, string token)
        => GetListAsync<Lesson>($"users/{uid}/lessons/byClass/{institutionId}/{classId}", token);

    public Task<bool> UpsertLessonAllAsync(string uid, string token, Lesson lesson)
        => PutAsync($"users/{uid}/lessons/all/{lesson.Id}", token, lesson);

    public Task<bool> UpsertLessonByClassAsync(string uid, string institutionId, string classId, string token, Lesson lesson)
        => PutAsync($"users/{uid}/lessons/byClass/{institutionId}/{classId}/{lesson.Id}", token, lesson);

    public Task<bool> DeleteLessonAllAsync(string uid, string token, string lessonId)
        => DeleteAsync($"users/{uid}/lessons/all/{lessonId}", token);

    public Task<bool> DeleteLessonByClassAsync(string uid, string institutionId, string classId, string token, string lessonId)
        => DeleteAsync($"users/{uid}/lessons/byClass/{institutionId}/{classId}/{lessonId}", token);
}
