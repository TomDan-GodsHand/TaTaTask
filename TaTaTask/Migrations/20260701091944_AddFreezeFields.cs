using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaTaTask.Migrations
{
    /// <inheritdoc />
    public partial class AddFreezeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FrozeAt",
                table: "TodoItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FrozenReason",
                table: "TodoItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousStatus",
                table: "TodoItems",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FrozeAt",
                table: "TodoItems");

            migrationBuilder.DropColumn(
                name: "FrozenReason",
                table: "TodoItems");

            migrationBuilder.DropColumn(
                name: "PreviousStatus",
                table: "TodoItems");
        }
    }
}
