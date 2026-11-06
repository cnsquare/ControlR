using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlR.Web.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScriptRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScriptRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Content = table.Column<string>(type: "character varying(262144)", maxLength: 262144, nullable: false),
                    InitiatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Shell = table.Column<int>(type: "integer", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScriptRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScriptRuns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScriptResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OutputTruncated = table.Column<bool>(type: "boolean", nullable: false),
                    ScriptRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    StandardError = table.Column<string>(type: "text", nullable: false),
                    StandardOutput = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScriptResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScriptResults_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScriptResults_ScriptRuns_ScriptRunId",
                        column: x => x.ScriptRunId,
                        principalTable: "ScriptRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScriptResults_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScriptResults_DeviceId",
                table: "ScriptResults",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ScriptResults_ScriptRunId",
                table: "ScriptResults",
                column: "ScriptRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ScriptResults_TenantId",
                table: "ScriptResults",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScriptRuns_TenantId",
                table: "ScriptRuns",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScriptResults");

            migrationBuilder.DropTable(
                name: "ScriptRuns");
        }
    }
}
