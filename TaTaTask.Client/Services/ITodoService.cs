using TaTaTask.Models.Dtos;
using TaTaTask.Models.Enums;

namespace TaTaTask.Client.Services;

public interface ITodoService
{
    Task<List<TodoItemDto>> GetBoardAsync(string? search = null);
    Task<TodoItemDto> CreateAsync(CreateTodoRequest request);
    Task<TodoItemDto?> UpdateAsync(int id, TodoItemDto dto);
    Task<bool> DeleteAsync(int id);
    Task<TodoItemDto> ChangeStatusAsync(int id, TodoStatus status);
    Task<TodoItemDto?> AddStepAsync(int todoId, string title);
    Task<TodoItemDto?> ToggleStepAsync(int todoId, int stepId);
    Task<TodoItemDto?> DeleteStepAsync(int todoId, int stepId);
    Task<TodoItemDto> ArchiveAsync(int id);
    Task<List<TodoItemDto>> GetArchivedAsync(string? tag, DateTime? from, DateTime? to, string? q);
    Task<UserSettingsDto> GetUserSettingsAsync();
}
