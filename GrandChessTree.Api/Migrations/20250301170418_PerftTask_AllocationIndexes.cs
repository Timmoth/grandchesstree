using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrandChessTree.Api.Migrations
{
    /// <inheritdoc />
    public partial class PerftTask_AllocationIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_fast_task_started_at_fast_task_finished_at_d~",
                table: "perft_tasks_v3",
                columns: new[] { "fast_task_started_at", "fast_task_finished_at", "depth", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_full_task_started_at_full_task_finished_at_d~",
                table: "perft_tasks_v3",
                columns: new[] { "full_task_started_at", "full_task_finished_at", "depth", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_perft_tasks_v3_fast_task_started_at_fast_task_finished_at_d~",
                table: "perft_tasks_v3");

            migrationBuilder.DropIndex(
                name: "IX_perft_tasks_v3_full_task_started_at_full_task_finished_at_d~",
                table: "perft_tasks_v3");
        }
    }
}
