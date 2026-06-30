using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaTaTask.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultDueDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultDueDays",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultDueDays",
                table: "Users");
        }
    }
}
