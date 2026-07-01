using System.Net.Http.Json;
using TaTaTask.Models.Dtos;
using TaTaTask.Models.Enums;

namespace TaTaTask.Client.Services;

public class ClientTodoService : ITodoService
{
    private readonly HttpClient _http;

    public ClientTodoService(HttpClient http) => _http = http;

    public async Task<List<TodoItemDto>> GetBoardAsync(string? search = null)
    {
        var url = string.IsNullOrWhiteSpace(search)
            ? "api/todos"
            : $"api/todos?q={Uri.EscapeDataString(search)}";
        return await _http.GetFromJsonAsync<List<TodoItemDto>>(url) ?? new();
    }

    public async Task<TodoItemDto> CreateAsync(CreateTodoRequest request)
    {
        var resp = await _http.PostAsJsonAsync("api/todos", request);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TodoItemDto>())!;
    }

    public async Task<TodoItemDto?> UpdateAsync(int id, UpdateTodoRequest request)
    {
        var resp = await _http.PutAsJsonAsync($"api/todos/{id}", request);
        return resp.IsSuccessStatusCode
            ? await resp.Content.ReadFromJsonAsync<TodoItemDto>()
            : null;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var resp = await _http.DeleteAsync($"api/todos/{id}");
        return resp.IsSuccessStatusCode;
    }

    public async Task<TodoItemDto> ChangeStatusAsync(int id, TodoStatus status, string? frozenReason = null, bool resetSteps = false)
    {
        var resp = await _http.PostAsJsonAsync($"api/todos/{id}/status", new ChangeStatusRequest { Status = status, FrozenReason = frozenReason, ResetSteps = resetSteps });
        if (!resp.IsSuccessStatusCode)
        {
            var msg = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? "操作失败" : msg);
        }
        return (await resp.Content.ReadFromJsonAsync<TodoItemDto>())!;
    }

    public async Task<TodoItemDto?> AddStepAsync(int todoId, string title)
    {
        var resp = await _http.PostAsJsonAsync($"api/todos/{todoId}/steps", new AddStepRequest { Title = title });
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TodoItemDto>() : null;
    }

    public async Task<TodoItemDto?> ToggleStepAsync(int todoId, int stepId)
    {
        var resp = await _http.PostAsync($"api/todos/{todoId}/steps/{stepId}/toggle", null);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TodoItemDto>() : null;
    }

    public async Task<TodoItemDto?> DeleteStepAsync(int todoId, int stepId)
    {
        var resp = await _http.DeleteAsync($"api/todos/{todoId}/steps/{stepId}");
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TodoItemDto>() : null;
    }

    public async Task<TodoItemDto> ArchiveAsync(int id)
    {
        var resp = await _http.PostAsync($"api/todos/{id}/archive", null);
        if (!resp.IsSuccessStatusCode)
        {
            var msg = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? "归档失败" : msg);
        }
        return (await resp.Content.ReadFromJsonAsync<TodoItemDto>())!;
    }

    public async Task<List<TodoItemDto>> GetArchivedAsync(string? tag, DateTime? from, DateTime? to, string? q)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(tag)) parts.Add($"tag={Uri.EscapeDataString(tag)}");
        if (!string.IsNullOrWhiteSpace(q)) parts.Add($"q={Uri.EscapeDataString(q)}");
        if (from.HasValue) parts.Add($"from={from.Value:yyyy-MM-dd}");
        if (to.HasValue) parts.Add($"to={to.Value:yyyy-MM-dd}");
        var url = "api/archive" + (parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty);
        return await _http.GetFromJsonAsync<List<TodoItemDto>>(url) ?? new();
    }

    public async Task<DashboardStatsDto> GetStatsAsync()
    {
        return await _http.GetFromJsonAsync<DashboardStatsDto>("api/stats") ?? new();
    }

    public async Task<UserSettingsDto> GetUserSettingsAsync()
    {
        return await _http.GetFromJsonAsync<UserSettingsDto>("api/user/settings") ?? new();
    }
}
