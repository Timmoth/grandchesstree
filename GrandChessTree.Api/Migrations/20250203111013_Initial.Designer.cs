﻿// <auto-generated />
using GrandChessTree.Api.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GrandChessTree.Api.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20250203111013_Initial")]
    partial class Initial
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.1")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("GrandChessTree.Api.Accounts.AccountModel", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Relational:JsonPropertyName", "id");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("email")
                        .HasAnnotation("Relational:JsonPropertyName", "email");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("name")
                        .HasAnnotation("Relational:JsonPropertyName", "name");

                    b.Property<string>("Role")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("role")
                        .HasAnnotation("Relational:JsonPropertyName", "role");

                    b.Property<uint>("Version")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.HasKey("Id");

                    b.ToTable("accounts");
                });

            modelBuilder.Entity("GrandChessTree.Api.ApiKeys.ApiKeyModel", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id")
                        .HasAnnotation("Relational:JsonPropertyName", "id");

                    b.Property<long>("AccountId")
                        .HasColumnType("bigint")
                        .HasColumnName("account_id")
                        .HasAnnotation("Relational:JsonPropertyName", "account_id");

                    b.Property<string>("ApiKeyTail")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("apikey_tail")
                        .HasAnnotation("Relational:JsonPropertyName", "apikey_tail");

                    b.Property<long>("CreatedAt")
                        .HasColumnType("bigint")
                        .HasColumnName("created_at")
                        .HasAnnotation("Relational:JsonPropertyName", "created_at");

                    b.Property<string>("Role")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("role")
                        .HasAnnotation("Relational:JsonPropertyName", "role");

                    b.Property<uint>("Version")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.HasKey("Id");

                    b.HasIndex("AccountId");

                    b.ToTable("api_keys");
                });

            modelBuilder.Entity("GrandChessTree.Api.D10Search.PerftItem", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<long>("AvailableAt")
                        .HasColumnType("bigint")
                        .HasColumnName("available_at");

                    b.Property<bool>("Confirmed")
                        .HasColumnType("boolean")
                        .HasColumnName("confirmed");

                    b.Property<int>("Depth")
                        .HasColumnType("integer")
                        .HasColumnName("depth");

                    b.Property<decimal>("Hash")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("hash");

                    b.Property<int>("Occurrences")
                        .HasColumnType("integer")
                        .HasColumnName("occurrences");

                    b.Property<int>("PassCount")
                        .HasColumnType("integer")
                        .HasColumnName("pass_count");

                    b.HasKey("Id");

                    b.HasIndex("Depth");

                    b.HasIndex("Hash", "Depth")
                        .IsUnique();

                    b.ToTable("perft_items");
                });

            modelBuilder.Entity("GrandChessTree.Api.D10Search.PerftTask", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<long?>("AccountId")
                        .HasColumnType("bigint")
                        .HasColumnName("account_id")
                        .HasAnnotation("Relational:JsonPropertyName", "account_id");

                    b.Property<decimal>("Captures")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("captures");

                    b.Property<decimal>("Castles")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("castles");

                    b.Property<int>("Depth")
                        .HasColumnType("integer")
                        .HasColumnName("depth");

                    b.Property<decimal>("DirectCheck")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("direct_checks");

                    b.Property<decimal>("DirectCheckmate")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("direct_checkmate");

                    b.Property<decimal>("DirectDiscoverdCheckmate")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("direct_discoverd_checkmate");

                    b.Property<decimal>("DirectDiscoveredCheck")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("direct_discovered_check");

                    b.Property<decimal>("DoubleDiscoverdCheckmate")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("double_discoverd_checkmate");

                    b.Property<decimal>("DoubleDiscoveredCheck")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("double_discovered_check");

                    b.Property<decimal>("Enpassant")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("enpassants");

                    b.Property<long>("FinishedAt")
                        .HasColumnType("bigint")
                        .HasColumnName("finished_at");

                    b.Property<decimal>("Nodes")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("nodes");

                    b.Property<float>("Nps")
                        .HasColumnType("real")
                        .HasColumnName("nps");

                    b.Property<long>("PerftItemId")
                        .HasColumnType("bigint")
                        .HasColumnName("perft_item_id");

                    b.Property<decimal>("Promotions")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("promotions");

                    b.Property<decimal>("SingleDiscoveredCheck")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("single_discovered_check");

                    b.Property<decimal>("SingleDiscoveredCheckmate")
                        .HasColumnType("numeric(20,0)")
                        .HasColumnName("single_discovered_checkmate");

                    b.Property<long>("StartedAt")
                        .HasColumnType("bigint")
                        .HasColumnName("started_at");

                    b.HasKey("Id");

                    b.HasIndex("AccountId");

                    b.HasIndex("Depth");

                    b.HasIndex("PerftItemId");

                    b.ToTable("perft_tasks");
                });

            modelBuilder.Entity("GrandChessTree.Api.ApiKeys.ApiKeyModel", b =>
                {
                    b.HasOne("GrandChessTree.Api.Accounts.AccountModel", "Account")
                        .WithMany("ApiKeys")
                        .HasForeignKey("AccountId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Account");
                });

            modelBuilder.Entity("GrandChessTree.Api.D10Search.PerftTask", b =>
                {
                    b.HasOne("GrandChessTree.Api.Accounts.AccountModel", "Account")
                        .WithMany("SearchTasks")
                        .HasForeignKey("AccountId")
                        .OnDelete(DeleteBehavior.SetNull);

                    b.HasOne("GrandChessTree.Api.D10Search.PerftItem", "PerftItem")
                        .WithMany("SearchTasks")
                        .HasForeignKey("PerftItemId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Account");

                    b.Navigation("PerftItem");
                });

            modelBuilder.Entity("GrandChessTree.Api.Accounts.AccountModel", b =>
                {
                    b.Navigation("ApiKeys");

                    b.Navigation("SearchTasks");
                });

            modelBuilder.Entity("GrandChessTree.Api.D10Search.PerftItem", b =>
                {
                    b.Navigation("SearchTasks");
                });
#pragma warning restore 612, 618
        }
    }
}
