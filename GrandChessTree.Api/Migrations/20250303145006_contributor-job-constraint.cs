using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrandChessTree.Api.Migrations
{
    /// <inheritdoc />
    public partial class contributorjobconstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_perft_jobs_root_position_id_depth",
                table: "perft_jobs",
                columns: new[] { "root_position_id", "depth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_perft_contributions_root_position_id_depth_account_id",
                table: "perft_contributions",
                columns: new[] { "root_position_id", "depth", "account_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_perft_jobs_root_position_id_depth",
                table: "perft_jobs");

            migrationBuilder.DropIndex(
                name: "IX_perft_contributions_root_position_id_depth_account_id",
                table: "perft_contributions");
        }
    }
}
