using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homdutio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "HouseholdTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill: seed each pre-S-04 task's position within its (household, status) column from its
            // creation order, so existing boards keep a sensible manual order on first deploy (FR-021). The
            // 0-based contiguous index matches the reindex the reorder endpoint maintains thereafter.
            migrationBuilder.Sql(
                "WITH [Ordered] AS (" +
                "    SELECT [Id], ROW_NUMBER() OVER (" +
                "        PARTITION BY [HouseholdId], [Status] ORDER BY [CreatedAtUtc]) - 1 AS [RowIndex] " +
                "    FROM [HouseholdTasks]) " +
                "UPDATE [t] SET [t].[SortOrder] = [o].[RowIndex] " +
                "FROM [HouseholdTasks] AS [t] " +
                "INNER JOIN [Ordered] AS [o] ON [t].[Id] = [o].[Id];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "HouseholdTasks");
        }
    }
}
