using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaTaTask.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Users",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "Asia/Shanghai");

            // 一次性数据解释：历史 DueDate 是「用户本地墙钟时间当 UTC 存」的（客户端 InputDate 直接提交本地时间），
            // 这里按默认时区 Asia/Shanghai（UTC+8）重新解释为真 UTC。
            // 只动用户手工录入的字段：TodoItems.DueDate、TodoSteps.DueDate。
            // CreatedAt / UpdatedAt / DoneAt / ArchivedAt / FrozeAt 都由服务端 DateTime.UtcNow 生成，本来就是真 UTC，不动。
            migrationBuilder.Sql(
                "UPDATE TodoItems SET DueDate = strftime('%Y-%m-%d %H:%M:%f', julianday(DueDate) - 8.0 / 24) WHERE DueDate IS NOT NULL;");
            migrationBuilder.Sql(
                "UPDATE TodoSteps SET DueDate = strftime('%Y-%m-%d %H:%M:%f', julianday(DueDate) - 8.0 / 24) WHERE DueDate IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE TodoItems SET DueDate = strftime('%Y-%m-%d %H:%M:%f', julianday(DueDate) + 8.0 / 24) WHERE DueDate IS NOT NULL;");
            migrationBuilder.Sql(
                "UPDATE TodoSteps SET DueDate = strftime('%Y-%m-%d %H:%M:%f', julianday(DueDate) + 8.0 / 24) WHERE DueDate IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Users");
        }
    }
}
