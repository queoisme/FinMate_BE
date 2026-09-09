using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence;

public class FinMateDbContext : DbContext
{
    public FinMateDbContext(DbContextOptions<FinMateDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinMateDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
