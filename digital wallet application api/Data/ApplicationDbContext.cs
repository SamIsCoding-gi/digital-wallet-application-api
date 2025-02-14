using Microsoft.EntityFrameworkCore;

namespace digital_wallet_application_api;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{

    public DbSet<User> Users { get; set; }
}
