using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrandChessTree.Api.Migrations
{
    /// <inheritdoc />
    public partial class perftjobsprops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "task_type",
                table: "perft_jobs",
                newName: "launch_depth");

            migrationBuilder.AddColumn<long>(
                name: "completed_fast_tasks",
                table: "perft_jobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "completed_full_tasks",
                table: "perft_jobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "computed_nodes",
                table: "perft_jobs",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "total_tasks",
                table: "perft_jobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "verified_tasks",
                table: "perft_jobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "completed_fast_tasks",
                table: "perft_jobs");

            migrationBuilder.DropColumn(
                name: "completed_full_tasks",
                table: "perft_jobs");

            migrationBuilder.DropColumn(
                name: "computed_nodes",
                table: "perft_jobs");

            migrationBuilder.DropColumn(
                name: "total_tasks",
                table: "perft_jobs");

            migrationBuilder.DropColumn(
                name: "verified_tasks",
                table: "perft_jobs");

            migrationBuilder.RenameColumn(
                name: "launch_depth",
                table: "perft_jobs",
                newName: "task_type");
        }
    }
}
