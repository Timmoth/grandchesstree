using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrandChessTree.Api.Migrations
{
    /// <inheritdoc />
    public partial class RootPositionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_root_position_id",
                table: "perft_tasks",
                column: "root_position_id");

            migrationBuilder.CreateIndex(
                name: "IX_perft_items_root_position_id",
                table: "perft_items",
                column: "root_position_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_perft_tasks_root_position_id",
                table: "perft_tasks");

            migrationBuilder.DropIndex(
                name: "IX_perft_items_root_position_id",
                table: "perft_items");
        }
    }
}
