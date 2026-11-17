using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlindSpotScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScanJobColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempts",
                table: "scan_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "cancel_requested",
                table: "scan_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string[]>(
                name: "failure_reasons",
                table: "scan_runs",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "leased_until",
                table: "scan_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_cost_usd",
                table: "scan_runs",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "queued_at",
                table: "scan_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requested_branch",
                table: "scan_runs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_scan_runs_status_queued_at",
                table: "scan_runs",
                columns: new[] { "status", "queued_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_scan_runs_status_queued_at",
                table: "scan_runs");

            migrationBuilder.DropColumn(
                name: "attempts",
                table: "scan_runs");

            migrationBuilder.DropColumn(
                name: "cancel_requested",
                table: "scan_runs");

            migrationBuilder.DropColumn(
                name: "failure_reasons",
                table: "scan_runs");

            migrationBuilder.DropColumn(
                name: "leased_until",
                table: "scan_runs");

            migrationBuilder.DropColumn(
                name: "max_cost_usd",
                table: "scan_runs");

            migrationBuilder.DropColumn(
                name: "queued_at",
                table: "scan_runs");

            migrationBuilder.DropColumn(
                name: "requested_branch",
                table: "scan_runs");
        }
    }
}
