using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlindSpotScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryUniqueName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_repositories_owner_name",
                table: "repositories",
                columns: new[] { "owner", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_repositories_owner_name",
                table: "repositories");
        }
    }
}
