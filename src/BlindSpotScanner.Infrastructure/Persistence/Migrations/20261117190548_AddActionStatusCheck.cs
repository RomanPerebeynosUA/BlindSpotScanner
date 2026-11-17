using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlindSpotScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActionStatusCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_findings_scan_run_id",
                table: "findings");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "status_checked_at",
                table: "remediation_actions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "ix_findings_scan_run_id_report_finding_id",
                table: "findings",
                columns: new[] { "scan_run_id", "report_finding_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_findings_scan_run_id_report_finding_id",
                table: "findings");

            migrationBuilder.DropColumn(
                name: "status_checked_at",
                table: "remediation_actions");

            migrationBuilder.CreateIndex(
                name: "ix_findings_scan_run_id",
                table: "findings",
                column: "scan_run_id");
        }
    }
}
