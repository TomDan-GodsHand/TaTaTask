using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TaTaTask.Models.Dtos;
using TaTaTask.Models.Enums;

namespace TaTaTask.External;

[McpServerToolType]
public sealed class TaskTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<CallToolResult> Call<T>(Func<Task<T>> action)
    {
        try
        {
            object? result = await action();
            var data = JsonSerializer.SerializeToElement(result is bool success ? new { success } : result, JsonOptions);
            return new() { StructuredContent = data, Content = [new TextContentBlock { Text = data.GetRawText() }] };
        }
        catch (ExternalApiException ex)
        {
            var data = JsonSerializer.SerializeToElement(new { status = ex.Status, title = ex.Message }, JsonOptions);
            return new() { IsError = true, StructuredContent = data, Content = [new TextContentBlock { Text = data.GetRawText() }] };
        }
    }

    [McpServerTool(Name = "get_profile", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Get the token owner's username, timeZoneId and write permission. All task dates use this user's wall-clock time without Z or UTC offset.")]
    public static Task<CallToolResult> Profile(ExternalApiService service) => Call(service.MeAsync);

    [McpServerTool(Name = "list_tasks", Destructive = false, OpenWorld = false)]
    [Description("Search paginated tasks. status: 1 NotStarted, 2 InProgress, 3 Converging, 4 Done, 5 Frozen. priority: 0–4. tag is one exact tag. archived defaults to false; null includes both. sort: updated, created, priority, due. Listing lazily archives tasks completed over 7 days ago.")]
    public static Task<CallToolResult> List(ExternalApiService service, int page = 1, int pageSize = 20,
        string? q = null, string? tag = null, TodoStatus? status = null, int? priority = null,
        bool? archived = false, string sort = "updated", CancellationToken cancellationToken = default)
        => Call(() => service.ListAsync(new TaskQuery
        {
            Page = page, PageSize = pageSize, Q = q, Tag = tag, Status = status,
            Priority = priority, Archived = archived, Sort = sort,
        }, cancellationToken));

    [McpServerTool(Name = "get_task", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Get one task including its steps by ID. Returns 404 if missing or owned by another user.")]
    public static Task<CallToolResult> Get(ExternalApiService service, int id) => Call(() => service.GetAsync(id));

    [McpServerTool(Name = "create_task", Destructive = false, OpenWorld = false)]
    [Description("Create a task. request.title is required (1–200 characters), priority is 0–4, status defaults to 1. dueDate must be user-local ISO time without Z or offset, e.g. 2026-10-20T18:00:00. steps is an optional array of titles. Requires write permission; do not retry blindly after a transport failure.")]
    public static Task<CallToolResult> Create(ExternalApiService service, CreateTodoRequest request)
        => Call(() => service.CreateAsync(request));

    [McpServerTool(Name = "update_task", Destructive = true, OpenWorld = false)]
    [Description("Replace a task's editable fields. Read the task first and supply title, description, priority, tags, dueDate and dueWarningHours; omitted nullable fields are cleared. Optional stepsToAdd, stepIdsToDelete and stepsOrder change steps. Status uses change_task_status. Requires write permission.")]
    public static Task<CallToolResult> Update(ExternalApiService service, int id, UpdateTodoRequest request)
        => Call(() => service.UpdateAsync(id, request));

    [McpServerTool(Name = "delete_task", Destructive = true, OpenWorld = false)]
    [Description("Permanently delete a task and its steps by ID; retain linked schedule history. Requires write permission.")]
    public static Task<CallToolResult> Delete(ExternalApiService service, int id) => Call(() => service.DeleteAsync(id));

    [McpServerTool(Name = "change_task_status", Destructive = true, OpenWorld = false)]
    [Description("Set task status: 1 NotStarted, 2 InProgress, 3 Converging, 4 Done, 5 Frozen. All steps must be complete before Done. Pass frozenReason when freezing. resetSteps clears completion and cannot be used with Done. Use unfreeze_task to restore the pre-freeze status. Requires write permission.")]
    public static Task<CallToolResult> Status(ExternalApiService service, int id, TodoStatus status,
        string? frozenReason = null, bool resetSteps = false)
        => Call(() => service.ChangeStatusAsync(id, new ChangeStatusRequest { Status = status, FrozenReason = frozenReason, ResetSteps = resetSteps }));

    [McpServerTool(Name = "unfreeze_task", Destructive = false, OpenWorld = false)]
    [Description("Unfreeze a frozen task and restore its previous status. Requires write permission.")]
    public static Task<CallToolResult> Unfreeze(ExternalApiService service, int id) => Call(() => service.UnfreezeAsync(id));

    [McpServerTool(Name = "archive_task", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Archive a completed task. Other statuses cannot be archived. Requires write permission.")]
    public static Task<CallToolResult> Archive(ExternalApiService service, int id) => Call(() => service.ArchiveAsync(id));

    [McpServerTool(Name = "add_task_step", Destructive = false, OpenWorld = false)]
    [Description("Append a step with a 1–200 character title to a task. Requires write permission.")]
    public static Task<CallToolResult> AddStep(ExternalApiService service, int id, string title) => Call(() => service.AddStepAsync(id, title));

    [McpServerTool(Name = "update_task_step", Destructive = true, OpenWorld = false)]
    [Description("Rename one step in a task. Requires write permission.")]
    public static Task<CallToolResult> UpdateStep(ExternalApiService service, int id, int stepId, string title)
        => Call(() => service.UpdateStepAsync(id, stepId, title));

    [McpServerTool(Name = "delete_task_step", Destructive = true, OpenWorld = false)]
    [Description("Permanently delete one step from a task. Requires write permission.")]
    public static Task<CallToolResult> DeleteStep(ExternalApiService service, int id, int stepId)
        => Call(() => service.DeleteStepAsync(id, stepId));

    [McpServerTool(Name = "set_task_step_completion", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Set one step's completed flag explicitly (safe to repeat). Completing the first step starts a NotStarted task. This is a board step operation; it does not advance schedule entries. Requires write permission.")]
    public static Task<CallToolResult> SetStep(ExternalApiService service, int id, int stepId, bool completed)
        => Call(() => service.SetStepAsync(id, stepId, completed));

    [McpServerTool(Name = "get_today_schedule", Destructive = false, OpenWorld = false)]
    [Description("Get today's schedule in the user's time zone. Lazily materializes today's entries and generated tasks, as opening the schedule page does.")]
    public static Task<CallToolResult> Today(ExternalApiService service) => Call(service.TodayAsync);

    [McpServerTool(Name = "list_schedule_rules", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List weekly schedule rules, paginated (page >= 1, pageSize 1–100).")]
    public static Task<CallToolResult> Rules(ExternalApiService service, int page = 1, int pageSize = 20)
        => Call(() => service.RulesAsync(page, pageSize));

    [McpServerTool(Name = "get_schedule_rule", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Get one weekly schedule rule by ID.")]
    public static Task<CallToolResult> Rule(ExternalApiService service, int id) => Call(() => service.RuleAsync(id));

    [McpServerTool(Name = "create_schedule_rule", Destructive = false, OpenWorld = false)]
    [Description("Create a weekly rule. title required; mode: 1 Pick or 2 Generate; daysOfWeek: bitmask Monday=1 through Sunday=64 (1–127). startTime/endTime: local HH:mm:ss, end must be later. steps: generated task step titles. Requires write permission.")]
    public static Task<CallToolResult> CreateRule(ExternalApiService service, SaveScheduleRuleRequest request)
        => Call(() => service.CreateRuleAsync(request));

    [McpServerTool(Name = "update_schedule_rule", Destructive = true, OpenWorld = false)]
    [Description("Replace a weekly schedule rule. Read its current values first and supply all desired fields and steps. Requires write permission.")]
    public static Task<CallToolResult> UpdateRule(ExternalApiService service, int id, SaveScheduleRuleRequest request)
        => Call(() => service.UpdateRuleAsync(id, request));

    [McpServerTool(Name = "delete_schedule_rule", Destructive = true, OpenWorld = false)]
    [Description("Delete a weekly schedule rule, preserving schedule history. Requires write permission.")]
    public static Task<CallToolResult> DeleteRule(ExternalApiService service, int id) => Call(() => service.DeleteRuleAsync(id));
}
