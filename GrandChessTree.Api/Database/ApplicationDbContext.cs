using GrandChessTree.Api.Accounts;
using GrandChessTree.Api.ApiKeys;
using GrandChessTree.Api.D10Search;
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
                .Property(e => e.Role)
                .HasConversion<string>();     
            
            modelBuilder.Entity<ApiKeyModel>()
                .Property(e => e.Role)
                .HasConversion<string>();

            modelBuilder.Entity<PerftItem>()
                .HasIndex(p => new { p.Hash, p.Depth })
                .IsUnique();


            modelBuilder.Entity<PerftItem>()
                .HasIndex(p => p.RootPositionId);

            modelBuilder.Entity<PerftItem>()
                .HasIndex(p => p.Depth);

            modelBuilder.Entity<PerftTask>()
                 .HasIndex(p => p.RootPositionId);

            modelBuilder.Entity<PerftTask>()
                .HasIndex(p => p.Depth);
        }

    }
}
