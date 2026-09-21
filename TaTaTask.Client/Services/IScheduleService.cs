using TaTaTask.Models.Dtos;

namespace TaTaTask.Client.Services;

/// <summary>
/// 日程服务。与 <see cref="ITodoService"/> 一样，接口定义在客户端、两端各有实现：
/// 服务端 <c>ServerScheduleService</c>（EF Core），WASM <c>ClientScheduleService</c>（HTTP）。
/// </summary>
public interface IScheduleService
{
    // ---------- 周规则 ----------
    Task<List<ScheduleRuleDto>> GetRulesAsync();
    Task<ScheduleRuleDto?> CreateRuleAsync(SaveScheduleRuleRequest request);
    Task<ScheduleRuleDto?> UpdateRuleAsync(int id, SaveScheduleRuleRequest request);
    Task<bool> DeleteRuleAsync(int id);

    // ---------- 今日日程（读时惰性物化） ----------
    Task<TodayScheduleDto> GetTodayAsync();

    /// <summary>某条「待选」日程项的候选任务（标签精确匹配 + 未归档 + 未完成 + 未冻结）。</summary>
    Task<List<ScheduleCandidateDto>> GetCandidatesAsync(int entryId);

    Task<TodayScheduleDto?> PickAsync(int entryId, PickScheduleTaskRequest request);
    Task<TodayScheduleDto?> SetStepsAsync(int entryId, SetEntryStepsRequest request);

    /// <summary>勾/取消一个今日步骤，并推进「条目完成 → 任务完成」链条。</summary>
    Task<TodayScheduleDto?> ToggleStepAsync(int entryId, int todoStepId);

    /// <summary>无子步骤的任务：直接切换该日程项的完成状态。</summary>
    Task<TodayScheduleDto?> CompleteEntryAsync(int entryId);

    /// <summary>手动加一条临时日程项（无规则，先落成「待选」，再用候选池挑任务）。</summary>
    Task<TodayScheduleDto?> CreateManualEntryAsync(CreateManualEntryRequest request);

    /// <summary>编辑一条临时安排（时段/标签/事项标题）。</summary>
    Task<TodayScheduleDto?> UpdateManualEntryAsync(int entryId, CreateManualEntryRequest request);

    /// <summary>删除一条临时安排；自动建的任务一并删除，从看板挑来的只解除关联。</summary>
    Task<TodayScheduleDto?> DeleteManualEntryAsync(int entryId);

    /// <summary>跳过某条规则在今天（不产生任务）。</summary>
    Task<TodayScheduleDto?> SkipTodayAsync(int ruleId);

    // ---------- 昨日未完成的处置 ----------
    Task<TodayScheduleDto?> CarryOverAsync(int entryId);
    Task<TodayScheduleDto?> FreezeEntryTaskAsync(int entryId);
    Task<TodayScheduleDto?> DeleteEntryTaskAsync(int entryId);

    // ---------- 历史（归档页） ----------
    /// <summary>按日期区间取日程历史；<paramref name="ruleId"/> 非空时只返回该规则相关的条目（§6.6「查看全部」的落点）。</summary>
    Task<List<ScheduleHistoryDto>> GetHistoryAsync(DateOnly from, DateOnly to, int? ruleId = null);

    // ---------- 统计（并入 /stats 页） ----------
    Task<ScheduleStatsDto> GetStatsAsync();
}
