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
}
