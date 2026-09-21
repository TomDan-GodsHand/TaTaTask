using System.Net.Http.Json;
using TaTaTask.Models.Dtos;

namespace TaTaTask.Client.Services;

public class ClientScheduleService : IScheduleService
{
    private readonly HttpClient _http;

    public ClientScheduleService(HttpClient http) => _http = http;

    // ---------- 周规则 ----------

    public async Task<List<ScheduleRuleDto>> GetRulesAsync()
        => await _http.GetFromJsonAsync<List<ScheduleRuleDto>>("api/schedule/rules") ?? new();

    public async Task<ScheduleRuleDto?> CreateRuleAsync(SaveScheduleRuleRequest request)
    {
        var resp = await _http.PostAsJsonAsync("api/schedule/rules", request);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(resp, "保存失败"));
        return await resp.Content.ReadFromJsonAsync<ScheduleRuleDto>();
    }

    public async Task<ScheduleRuleDto?> UpdateRuleAsync(int id, SaveScheduleRuleRequest request)
    {
        var resp = await _http.PutAsJsonAsync($"api/schedule/rules/{id}", request);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(resp, "保存失败"));
        return await resp.Content.ReadFromJsonAsync<ScheduleRuleDto>();
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage resp, string fallback)
    {
        var body = (await resp.Content.ReadAsStringAsync()).Trim().Trim('"');
        return string.IsNullOrWhiteSpace(body) ? fallback : body;
    }

    public async Task<bool> DeleteRuleAsync(int id)
    {
        var resp = await _http.DeleteAsync($"api/schedule/rules/{id}");
        return resp.IsSuccessStatusCode;
    }

    // ---------- 今日 ----------

    public async Task<TodayScheduleDto> GetTodayAsync()
        => await _http.GetFromJsonAsync<TodayScheduleDto>("api/schedule/today") ?? new();

    public async Task<List<ScheduleCandidateDto>> GetCandidatesAsync(int entryId)
        => await _http.GetFromJsonAsync<List<ScheduleCandidateDto>>($"api/schedule/entries/{entryId}/candidates") ?? new();

    public async Task<TodayScheduleDto?> PickAsync(int entryId, PickScheduleTaskRequest request)
    {
        var resp = await _http.PostAsJsonAsync($"api/schedule/entries/{entryId}/pick", request);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(resp, "排入失败"));
        return await resp.Content.ReadFromJsonAsync<TodayScheduleDto>();
    }

    public async Task<TodayScheduleDto?> SetStepsAsync(int entryId, SetEntryStepsRequest request)
    {
        var resp = await _http.PutAsJsonAsync($"api/schedule/entries/{entryId}/steps", request);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TodayScheduleDto>() : null;
    }

    public async Task<TodayScheduleDto?> ToggleStepAsync(int entryId, int todoStepId)
    {
        var resp = await _http.PostAsync($"api/schedule/entries/{entryId}/steps/{todoStepId}/toggle", null);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TodayScheduleDto>() : null;
    }

    public async Task<TodayScheduleDto?> CompleteEntryAsync(int entryId)
    {
        var resp = await _http.PostAsync($"api/schedule/entries/{entryId}/complete", null);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TodayScheduleDto>() : null;
    }

    public async Task<TodayScheduleDto?> CreateManualEntryAsync(CreateManualEntryRequest request)
    {
        var resp = await _http.PostAsJsonAsync("api/schedule/entries", request);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(resp, "加临时安排失败"));
        return await resp.Content.ReadFromJsonAsync<TodayScheduleDto>();
    }

    public async Task<TodayScheduleDto?> SkipTodayAsync(int ruleId)
    {
        var resp = await _http.PostAsync($"api/schedule/rules/{ruleId}/skip-today", null);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TodayScheduleDto>() : null;
    }

    // ---------- 昨日未完成 ----------

    public async Task<TodayScheduleDto?> CarryOverAsync(int entryId)
    {
        var resp = await _http.PostAsync($"api/schedule/entries/{entryId}/carry-over", null);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TodayScheduleDto>() : null;
    }

    public async Task<TodayScheduleDto?> FreezeEntryTaskAsync(int entryId)
    {
        var resp = await _http.PostAsync($"api/schedule/entries/{entryId}/freeze", null);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TodayScheduleDto>() : null;
    }

    public async Task<TodayScheduleDto?> DeleteEntryTaskAsync(int entryId)
    {
        var resp = await _http.DeleteAsync($"api/schedule/entries/{entryId}/task");
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TodayScheduleDto>() : null;
    }

    // ---------- 历史 ----------

    public async Task<List<ScheduleHistoryDto>> GetHistoryAsync(DateOnly from, DateOnly to, int? ruleId = null)
    {
        var url = $"api/schedule/history?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (ruleId is { } rid) url += $"&ruleId={rid}";
        return await _http.GetFromJsonAsync<List<ScheduleHistoryDto>>(url) ?? new();
    }

    // ---------- 统计 ----------

    public async Task<ScheduleStatsDto> GetStatsAsync()
        => await _http.GetFromJsonAsync<ScheduleStatsDto>("api/schedule/stats") ?? new();
}
