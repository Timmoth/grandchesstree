using GrandChessTree.Api.Accounts;
using GrandChessTree.Api.ApiKeys;
using GrandChessTree.Api.D10Search;
using GrandChessTree.Api.Perft.PerftNodes;
using Microsoft.EntityFrameworkCore;

namespace GrandChessTree.Api.Database
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
           : base(options)
        {
        }

        public DbSet<PerftTaskV3> PerftTasksV3 { get; set; }
        public DbSet<PerftItem> PerftItems { get; set; }
        public DbSet<PerftTask> PerftTasks { get; set; }
        public DbSet<PerftNodesTask> PerftNodesTask { get; set; }
        public DbSet<ApiKeyModel> ApiKeys { get; set; }
        public DbSet<AccountModel> Accounts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<PerftTask>()
                .HasOne(t => t.PerftItem)
                .WithMany(i => i.SearchTasks)
                .HasForeignKey(t => t.PerftItemId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AccountModel>()
                .HasMany(t => t.ApiKeys)
                .WithOne(i => i.Account)
                .HasForeignKey(t => t.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AccountModel>()
                .HasMany(t => t.SearchTasks)
                .WithOne(i => i.Account)
                .HasForeignKey(t => t.AccountId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<AccountModel>()
                .HasMany(t => t.PerftNodesTasks)
                .WithOne(i => i.Account)
                .HasForeignKey(t => t.AccountId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<AccountModel>()
                .Property(e => e.Role)
                .HasConversion<string>();

            modelBuilder.Entity<AccountModel>()
                .HasIndex(e => e.Name)
                .IsUnique();

            modelBuilder.Entity<AccountModel>()
                .HasIndex(e => e.Email)
                .IsUnique();

            modelBuilder.Entity<ApiKeyModel>()
                .Property(e => e.Role)
                .HasConversion<string>();

            modelBuilder.Entity<PerftItem>()
                .HasIndex(p => new { p.Hash, p.Depth })
                .IsUnique();

            modelBuilder.Entity<PerftItem>()
                .HasIndex(p => p.RootPositionId);

            #region Perft Nodes Task
            modelBuilder.Entity<PerftNodesTask>()
                .HasIndex(p => new { p.Hash, p.Depth })
                .IsUnique();

            modelBuilder.Entity<PerftNodesTask>()
                 .Property(e => e.AvailableAt)
                 .HasDefaultValue(0);

            modelBuilder.Entity<PerftNodesTask>()
                 .Property(e => e.WorkerId)
                 .HasDefaultValue(0);

            modelBuilder.Entity<PerftNodesTask>()
                 .Property(e => e.StartedAt)
                 .HasDefaultValue(0);

            modelBuilder.Entity<PerftNodesTask>()
                 .Property(e => e.FinishedAt)
                 .HasDefaultValue(0);

            modelBuilder.Entity<PerftNodesTask>()
             .Property(e => e.Nps)
             .HasDefaultValue(0);

            modelBuilder.Entity<PerftNodesTask>()
             .Property(e => e.Nodes)
             .HasDefaultValue(0);
            #endregion

            modelBuilder.Entity<PerftItem>()
                .HasIndex(p => p.RootPositionId);

            modelBuilder.Entity<PerftItem>()
                .HasIndex(p => p.Depth);

            modelBuilder.Entity<PerftTask>()
                 .HasIndex(p => p.RootPositionId);

            modelBuilder.Entity<PerftTask>()
                .HasIndex(p => p.Depth);

            modelBuilder.Entity<PerftTask>()
                .HasIndex(p => p.FinishedAt);

            modelBuilder.Entity<PerftNodesTask>()
                .HasIndex(p => p.RootPositionId);

            modelBuilder.Entity<PerftNodesTask>()
                .HasIndex(p => p.Depth);

            modelBuilder.Entity<PerftNodesTask>()
                .HasIndex(p => p.FinishedAt);

            #region V3

            modelBuilder.Entity<PerftTaskV3>()
               .HasIndex(p => new { p.RootPositionId, p.Depth, p.Board })
               .IsUnique();

            modelBuilder.Entity<PerftTaskV3>()
                .HasIndex(p => new { p.RootPositionId, p.Depth });


            modelBuilder.Entity<PerftTaskV3>()
     .HasIndex(p => p.FullTaskStartedAt);

            modelBuilder.Entity<PerftTaskV3>()
.HasIndex(p => p.FullTaskFinishedAt);

            modelBuilder.Entity<PerftTaskV3>()
.HasIndex(p => p.FastTaskStartedAt);

            modelBuilder.Entity<PerftTaskV3>()
.HasIndex(p => p.FastTaskFinishedAt);

            modelBuilder.Entity<PerftTaskV3>()
                 .Property(e => e.FullTaskStartedAt)
                 .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
                 .Property(e => e.FullTaskFinishedAt)
                 .HasDefaultValue(0);


            modelBuilder.Entity<PerftTaskV3>()
                 .Property(e => e.FullTaskWorkerId)
                 .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.FullTaskStartedAt)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.FullTaskFinishedAt)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.FullTaskNps)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.FullTaskNodes)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.Captures)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.Enpassants)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.Castles)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.Promotions)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.DirectChecks)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.SingleDiscoveredChecks)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.DirectDiscoveredChecks)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.DoubleDiscoveredChecks)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.DirectMates)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.SingleDiscoveredMates)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.DirectDiscoveredMates)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.DoubleDiscoveredMates)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.FastTaskWorkerId)
     .HasDefaultValue(0);


            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.FastTaskStartedAt)
     .HasDefaultValue(0);


            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.FastTaskFinishedAt)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.FastTaskNps)
     .HasDefaultValue(0);

            modelBuilder.Entity<PerftTaskV3>()
     .Property(e => e.FastTaskNodes)
     .HasDefaultValue(0);

            #endregion

        }

    }
}
