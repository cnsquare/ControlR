using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlR.Web.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScriptQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RunOnNextConnect",
                table: "ScriptSchedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "InitiatedByUserId",
                table: "ScriptRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScriptScheduleId",
                table: "ScriptRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchedAt",
                table: "ScriptResults",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScriptResults_Status",
                table: "ScriptResults",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScriptResults_Status",
                table: "ScriptResults");

            migrationBuilder.DropColumn(
                name: "RunOnNextConnect",
                table: "ScriptSchedules");

            migrationBuilder.DropColumn(
                name: "InitiatedByUserId",
                table: "ScriptRuns");

            migrationBuilder.DropColumn(
                name: "ScriptScheduleId",
                table: "ScriptRuns");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "ScriptResults");
        }
    }
}
