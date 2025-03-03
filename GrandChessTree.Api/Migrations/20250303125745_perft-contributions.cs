using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GrandChessTree.Api.Migrations
{
    /// <inheritdoc />
    public partial class perftcontributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "computed_nodes",
                table: "perft_jobs",
                newName: "full_task_nodes");

            migrationBuilder.AddColumn<decimal>(
                name: "fast_task_nodes",
                table: "perft_jobs",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "perft_contributions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    root_position_id = table.Column<int>(type: "integer", nullable: false),
                    completed_full_tasks = table.Column<long>(type: "bigint", nullable: false),
                    completed_fast_tasks = table.Column<long>(type: "bigint", nullable: false),
                    fast_task_nodes = table.Column<decimal>(type: "numeric", nullable: false),
                    full_task_nodes = table.Column<decimal>(type: "numeric", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perft_contributions", x => x.id);
                    table.ForeignKey(
                        name: "FK_perft_contributions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_perft_contributions_account_id",
                table: "perft_contributions",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "perft_contributions");

            migrationBuilder.DropColumn(
                name: "fast_task_nodes",
                table: "perft_jobs");

            migrationBuilder.RenameColumn(
                name: "full_task_nodes",
                table: "perft_jobs",
                newName: "computed_nodes");
        }
    }
}
