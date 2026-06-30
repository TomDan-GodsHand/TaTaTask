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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Username).HasMaxLength(50).IsRequired();
            entity.Property(u => u.PasswordHash).IsRequired();
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
        });

        modelBuilder.Entity<TodoStep>(entity =>
        {
            entity.Property(s => s.Title).HasMaxLength(200).IsRequired();
            entity.HasIndex(s => s.TodoItemId);
        });
    }
}
