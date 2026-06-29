using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homdutio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskTags_HouseholdTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "HouseholdTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskTags_HouseholdId_Value",
                table: "TaskTags",
                columns: new[] { "HouseholdId", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskTags_TaskId",
                table: "TaskTags",
                column: "TaskId");

            // Backfill: seed one tag per existing non-blank Category, preserving the prior single-category
            // classification (the trimmed value, copying the task's HouseholdId for the suggestion query).
            // Category is NOT dropped this deploy (additive-only against the no-rollback B1 — see MIGRATIONS.md).
            // LEFT(...,50) guards the 50-char Value column should any legacy Category (max 100) exceed it; the
            // NOT EXISTS makes the backfill idempotent so a re-run can never duplicate a seeded tag.
            migrationBuilder.Sql(@"
                INSERT INTO TaskTags (Id, TaskId, HouseholdId, Value)
                SELECT NEWID(), t.Id, t.HouseholdId, LEFT(LTRIM(RTRIM(t.Category)), 50)
                FROM HouseholdTasks t
                WHERE t.Category IS NOT NULL
                  AND LEN(LTRIM(RTRIM(t.Category))) > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM TaskTags tt
                      WHERE tt.TaskId = t.Id
                        AND tt.Value = LEFT(LTRIM(RTRIM(t.Category)), 50)
                  );");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskTags");
        }
    }
}
