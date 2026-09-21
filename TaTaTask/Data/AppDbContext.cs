using Microsoft.EntityFrameworkCore;
using TaTaTask.Models.Entities;

namespace TaTaTask.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<TodoItem> TodoItems => Set<TodoItem>();
    public DbSet<TodoStep> TodoSteps => Set<TodoStep>();
    public DbSet<FeedbackItem> FeedbackItems => Set<FeedbackItem>();
    public DbSet<FeedbackReply> FeedbackReplies => Set<FeedbackReply>();

    public DbSet<ScheduleRule> ScheduleRules => Set<ScheduleRule>();
    public DbSet<ScheduleRuleStep> ScheduleRuleSteps => Set<ScheduleRuleStep>();
    public DbSet<ScheduleEntry> ScheduleEntries => Set<ScheduleEntry>();
    public DbSet<ScheduleEntryStep> ScheduleEntrySteps => Set<ScheduleEntryStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Username).HasMaxLength(50).IsRequired();
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.TimeZoneId).HasMaxLength(64).IsRequired().HasDefaultValue("Asia/Shanghai");
        });

        modelBuilder.Entity<TodoItem>(entity =>
        {
            entity.Property(t => t.Title).HasMaxLength(200).IsRequired();
            entity.Property(t => t.Description).HasMaxLength(2000);
            entity.Property(t => t.Tags).HasMaxLength(500);
            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => new { t.UserId, t.Status });

            entity.HasOne(t => t.User)
                .WithMany(u => u.TodoItems)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(t => t.Steps)
                .WithOne(s => s.TodoItem)
                .HasForeignKey(s => s.TodoItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(t => new { t.UserId, t.SourceRuleId });

            entity.HasOne(t => t.SourceRule)
                .WithMany()
                .HasForeignKey(t => t.SourceRuleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TodoStep>(entity =>
        {
            entity.Property(s => s.Title).HasMaxLength(200).IsRequired();
            entity.HasIndex(s => s.TodoItemId);
        });

        modelBuilder.Entity<FeedbackItem>(entity =>
        {
            entity.Property(f => f.Title).HasMaxLength(200).IsRequired();
            entity.Property(f => f.Content).HasMaxLength(5000).IsRequired();
            entity.HasIndex(f => f.CreatedAt);

            entity.HasOne(f => f.User)
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(f => f.Replies)
                .WithOne(r => r.FeedbackItem)
                .HasForeignKey(r => r.FeedbackItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FeedbackReply>(entity =>
        {
            entity.Property(r => r.Content).HasMaxLength(5000).IsRequired();
            entity.HasIndex(r => r.FeedbackItemId);

            entity.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduleRule>(entity =>
        {
            entity.Property(r => r.Title).HasMaxLength(200).IsRequired();
            entity.Property(r => r.Tags).HasMaxLength(500);
            entity.HasIndex(r => new { r.UserId, r.IsActive });

            entity.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(r => r.Steps)
                .WithOne(s => s.ScheduleRule)
                .HasForeignKey(s => s.ScheduleRuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduleRuleStep>(entity =>
        {
            entity.Property(s => s.Title).HasMaxLength(200).IsRequired();
            entity.HasIndex(s => s.ScheduleRuleId);
        });

        modelBuilder.Entity<ScheduleEntry>(entity =>
        {
            entity.Property(e => e.TitleSnapshot).HasMaxLength(200);
            entity.HasIndex(e => new { e.UserId, e.Date });
            // 每日物化幂等：同一用户、同一天、同一条规则只生成一条
            entity.HasIndex(e => new { e.UserId, e.Date, e.RuleId }).IsUnique();

            entity.Property(e => e.Tags).HasMaxLength(500);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // 规则被删除时保留历史日程项
            entity.HasOne(e => e.Rule)
                .WithMany(r => r.Entries)
                .HasForeignKey(e => e.RuleId)
                .OnDelete(DeleteBehavior.SetNull);

            // 任务被硬删除时保留历史日程项（并置 IsTaskDeleted / TitleSnapshot，见服务层）
            entity.HasOne(e => e.TodoItem)
                .WithMany()
                .HasForeignKey(e => e.TodoItemId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Steps)
                .WithOne(s => s.ScheduleEntry)
                .HasForeignKey(s => s.ScheduleEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduleEntryStep>(entity =>
        {
            entity.HasIndex(s => s.ScheduleEntryId);
            entity.HasIndex(s => s.TodoStepId);
            // 同一条目里同一个子步骤只排一次
            entity.HasIndex(s => new { s.ScheduleEntryId, s.TodoStepId }).IsUnique();

            entity.HasOne(s => s.TodoStep)
                .WithMany()
                .HasForeignKey(s => s.TodoStepId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
