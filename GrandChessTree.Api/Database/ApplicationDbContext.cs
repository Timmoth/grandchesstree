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

        }

    }
}
