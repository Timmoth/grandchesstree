using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrandChessTree.Api.Migrations
{
    /// <inheritdoc />
    public partial class removed_perft_task_indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropIndex(
                name: "IX_perft_tasks_v3_root_position_id_depth_board",
                table: "perft_tasks_v3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_root_position_id_depth_board",
                table: "perft_tasks_v3",
                columns: new[] { "root_position_id", "depth", "board" },
                unique: true);
        }
    }
}
