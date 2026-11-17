using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlindSpotScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaseOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "lease_owner",
                table: "scan_runs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lease_owner",
                table: "scan_runs");
        }
    }
}
