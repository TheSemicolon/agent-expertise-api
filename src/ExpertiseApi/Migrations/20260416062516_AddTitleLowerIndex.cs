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
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY "IX_ExpertiseEntries_TitleLower"
                    ON "ExpertiseEntries" (LOWER("Title"))
                    WHERE "DeprecatedAt" IS NULL;
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS "IX_ExpertiseEntries_TitleLower";
                """,
                suppressTransaction: true);
        }
    }
}
