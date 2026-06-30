using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaTaTask.Migrations
{
    /// <inheritdoc />
    public partial class AddDoneAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DoneAt",
                table: "TodoItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DoneAt",
                table: "TodoItems");
        }
    }
}
