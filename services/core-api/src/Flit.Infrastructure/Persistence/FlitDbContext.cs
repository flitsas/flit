using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence;

public sealed class FlitDbContext(DbContextOptions<FlitDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<UserTempSuspension> UserTempSuspensions => Set<UserTempSuspension>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlitDbContext).Assembly);
    }
}
