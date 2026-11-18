using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlindSpotScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmSharingAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "llm_sharing_acknowledged_at",
                table: "repositories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "llm_sharing_acknowledged_by",
                table: "repositories",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "llm_sharing_acknowledged_at",
                table: "repositories");

            migrationBuilder.DropColumn(
                name: "llm_sharing_acknowledged_by",
                table: "repositories");
        }
    }
}
