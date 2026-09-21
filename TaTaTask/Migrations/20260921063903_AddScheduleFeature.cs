using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaTaTask.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceRuleId",
                table: "TodoItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScheduleRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    DaysOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleRules_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: true),
                    TodoItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    TitleSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsTaskDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    HandledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleEntries_ScheduleRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "ScheduleRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScheduleEntries_TodoItems_TodoItemId",
                        column: x => x.TodoItemId,
                        principalTable: "TodoItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScheduleEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleRuleSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScheduleRuleId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRuleSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleRuleSteps_ScheduleRules_ScheduleRuleId",
                        column: x => x.ScheduleRuleId,
                        principalTable: "ScheduleRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleEntrySteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScheduleEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    TodoStepId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleEntrySteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleEntrySteps_ScheduleEntries_ScheduleEntryId",
                        column: x => x.ScheduleEntryId,
                        principalTable: "ScheduleEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduleEntrySteps_TodoSteps_TodoStepId",
                        column: x => x.TodoStepId,
                        principalTable: "TodoSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_SourceRuleId",
                table: "TodoItems",
                column: "SourceRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_UserId_SourceRuleId",
                table: "TodoItems",
                columns: new[] { "UserId", "SourceRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEntries_RuleId",
                table: "ScheduleEntries",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEntries_TodoItemId",
                table: "ScheduleEntries",
                column: "TodoItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEntries_UserId_Date",
                table: "ScheduleEntries",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEntries_UserId_Date_RuleId",
                table: "ScheduleEntries",
                columns: new[] { "UserId", "Date", "RuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEntrySteps_ScheduleEntryId",
                table: "ScheduleEntrySteps",
                column: "ScheduleEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEntrySteps_ScheduleEntryId_TodoStepId",
                table: "ScheduleEntrySteps",
                columns: new[] { "ScheduleEntryId", "TodoStepId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEntrySteps_TodoStepId",
                table: "ScheduleEntrySteps",
                column: "TodoStepId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleRules_UserId_IsActive",
                table: "ScheduleRules",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleRuleSteps_ScheduleRuleId",
                table: "ScheduleRuleSteps",
                column: "ScheduleRuleId");

            migrationBuilder.AddForeignKey(
                name: "FK_TodoItems_ScheduleRules_SourceRuleId",
                table: "TodoItems",
                column: "SourceRuleId",
                principalTable: "ScheduleRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TodoItems_ScheduleRules_SourceRuleId",
                table: "TodoItems");

            migrationBuilder.DropTable(
                name: "ScheduleEntrySteps");

            migrationBuilder.DropTable(
                name: "ScheduleRuleSteps");

            migrationBuilder.DropTable(
                name: "ScheduleEntries");

            migrationBuilder.DropTable(
                name: "ScheduleRules");

            migrationBuilder.DropIndex(
                name: "IX_TodoItems_SourceRuleId",
                table: "TodoItems");

            migrationBuilder.DropIndex(
                name: "IX_TodoItems_UserId_SourceRuleId",
                table: "TodoItems");

            migrationBuilder.DropColumn(
                name: "SourceRuleId",
                table: "TodoItems");
        }
    }
}
