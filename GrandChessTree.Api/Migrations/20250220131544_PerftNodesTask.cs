using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GrandChessTree.Api.Migrations
{
    /// <inheritdoc />
    public partial class PerftNodesTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "perft_nodes_tasks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    hash = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    fen = table.Column<string>(type: "text", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    launch_depth = table.Column<int>(type: "integer", nullable: false),
                    available_at = table.Column<long>(type: "bigint", nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false),
                    root_position_id = table.Column<int>(type: "integer", nullable: false),
                    worker_id = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: false),
                    finished_at = table.Column<long>(type: "bigint", nullable: false),
                    nps = table.Column<float>(type: "real", nullable: false),
                    nodes = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perft_nodes_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_perft_nodes_tasks_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_perft_nodes_tasks_account_id",
                table: "perft_nodes_tasks",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_perft_nodes_tasks_depth",
                table: "perft_nodes_tasks",
                column: "depth");

            migrationBuilder.CreateIndex(
                name: "IX_perft_nodes_tasks_finished_at",
                table: "perft_nodes_tasks",
                column: "finished_at");

            migrationBuilder.CreateIndex(
                name: "IX_perft_nodes_tasks_hash_depth",
                table: "perft_nodes_tasks",
                columns: new[] { "hash", "depth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_perft_nodes_tasks_root_position_id",
                table: "perft_nodes_tasks",
                column: "root_position_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "perft_nodes_tasks");
        }
    }
}
