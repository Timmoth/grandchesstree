using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrandChessTree.Api.Migrations
{
    /// <inheritdoc />
    public partial class composite_perft_index : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_perft_tasks_v3_depth",
                table: "perft_tasks_v3");

            migrationBuilder.DropIndex(
                name: "IX_perft_tasks_v3_root_position_id",
                table: "perft_tasks_v3");

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_fast_task_finished_at",
                table: "perft_tasks_v3",
                column: "fast_task_finished_at");

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_fast_task_started_at",
                table: "perft_tasks_v3",
                column: "fast_task_started_at");

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_full_task_finished_at",
                table: "perft_tasks_v3",
                column: "full_task_finished_at");

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_full_task_started_at",
                table: "perft_tasks_v3",
                column: "full_task_started_at");

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_root_position_id_depth",
                table: "perft_tasks_v3",
                columns: new[] { "root_position_id", "depth" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_perft_tasks_v3_fast_task_finished_at",
                table: "perft_tasks_v3");

            migrationBuilder.DropIndex(
                name: "IX_perft_tasks_v3_fast_task_started_at",
                table: "perft_tasks_v3");

            migrationBuilder.DropIndex(
                name: "IX_perft_tasks_v3_full_task_finished_at",
                table: "perft_tasks_v3");

            migrationBuilder.DropIndex(
                name: "IX_perft_tasks_v3_full_task_started_at",
                table: "perft_tasks_v3");

            migrationBuilder.DropIndex(
                name: "IX_perft_tasks_v3_root_position_id_depth",
                table: "perft_tasks_v3");

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_depth",
                table: "perft_tasks_v3",
                column: "depth");

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_root_position_id",
                table: "perft_tasks_v3",
                column: "root_position_id");
        }
    }
}
