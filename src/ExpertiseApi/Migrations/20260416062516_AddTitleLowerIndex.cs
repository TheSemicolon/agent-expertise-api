using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertiseApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTitleLowerIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX "IX_ExpertiseEntries_TitleLower"
                    ON "ExpertiseEntries" (LOWER("Title"))
                    WHERE "DeprecatedAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_ExpertiseEntries_TitleLower";
                """);
        }
    }
}
