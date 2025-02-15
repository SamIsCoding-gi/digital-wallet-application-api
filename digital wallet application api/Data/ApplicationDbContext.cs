using Microsoft.EntityFrameworkCore;
using digital_wallet_application_api.Models.Entities;

namespace digital_wallet_application_api
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(e => e.Balance)
                      .HasColumnType("decimal(18, 2)");
            });
        }
    }
}
