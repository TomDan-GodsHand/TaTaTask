using Microsoft.AspNetCore.SignalR;
using TaTaTask.Data;
using TaTaTask.Hubs;

namespace TaTaTask.Api;

/// <summary>一个外部写命令使用一个事务；提交完成后才通知同账号的网页。</summary>
public sealed class ApiCommandExecutor(
    AppDbContext db, ApiContext context, IHubContext<TodoHub> hub,
    ILogger<ApiCommandExecutor> logger)
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        context.RequireWrite();
        await using var transaction = await db.Database.BeginTransactionAsync();
        T result;
        try
        {
            result = await action();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw;
        }

        // 命令已经成功，通知失败不能使调用方误以为写入失败并重复提交。
        try
        {
            await hub.Clients.Group($"user_{context.UserId}").SendAsync("Refresh");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "外部接口写入成功，但 SignalR 刷新通知失败");
        }
        return result;
    }

    public Task ExecuteAsync(Func<Task> action) => ExecuteAsync(async () =>
    {
        await action();
        return true;
    });
}
