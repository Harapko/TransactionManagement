using Microsoft.EntityFrameworkCore;
using TransactionManagement.Models;

namespace TransactionManagement.Database;

public class AppDbContext(
    DbContextOptions<AppDbContext> context,
    IConfiguration configuration
    ) : DbContext(context)
{
    public DbSet<Transaction> transactions { get; set; }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .UseSqlServer(configuration.GetConnectionString(nameof(AppDbContext)));
    }
}