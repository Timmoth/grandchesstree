using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GrandChessTree.Api.Migrations
{
    /// <inheritdoc />
    public partial class PerftTaskV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "perft_tasks_v3",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    board = table.Column<string>(type: "text", nullable: false),
                    root_position_id = table.Column<int>(type: "integer", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    launch_depth = table.Column<int>(type: "integer", nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false),
                    full_task_worker_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    full_task_started_at = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    full_task_finished_at = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    full_task_nps = table.Column<float>(type: "real", nullable: false, defaultValue: 0f),
                    full_task_nodes = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    captures = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    enpassants = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    castles = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    promotions = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    direct_checks = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    single_discovered_checks = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    direct_discovered_checks = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    double_discovered_checks = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    direct_mates = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    single_discovered_mates = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    direct_discovered_mates = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    double_discovered_mates = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    full_task_account_id = table.Column<long>(type: "bigint", nullable: true),
                    fast_task_worker_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    fast_task_started_at = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    fast_task_finished_at = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    fast_task_nps = table.Column<float>(type: "real", nullable: false, defaultValue: 0f),
                    fast_task_nodes = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    fast_task_account_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perft_tasks_v3", x => x.id);
                    table.ForeignKey(
                        name: "FK_perft_tasks_v3_accounts_fast_task_account_id",
                        column: x => x.fast_task_account_id,
                        principalTable: "accounts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_perft_tasks_v3_accounts_full_task_account_id",
                        column: x => x.full_task_account_id,
                        principalTable: "accounts",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_email",
                table: "accounts",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_name",
                table: "accounts",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_depth",
                table: "perft_tasks_v3",
                column: "depth");

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_fast_task_account_id",
                table: "perft_tasks_v3",
                column: "fast_task_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_full_task_account_id",
                table: "perft_tasks_v3",
                column: "full_task_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_root_position_id",
                table: "perft_tasks_v3",
                column: "root_position_id");

            migrationBuilder.CreateIndex(
                name: "IX_perft_tasks_v3_root_position_id_depth_board",
                table: "perft_tasks_v3",
                columns: new[] { "root_position_id", "depth", "board" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "perft_tasks_v3");

            migrationBuilder.DropIndex(
                name: "IX_accounts_email",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "IX_accounts_name",
                table: "accounts");
        }
    }
}
