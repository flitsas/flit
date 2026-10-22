using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Entities.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Flit.Infrastructure.Persistence;

/// <summary>
/// Las tablas de identidad que usan los repositorios compartidos (HU #13231). La implementan los dos contextos:
/// <c>FlitDbContext</c> en core-api (un solo contexto, como siempre) e <see cref="IdentityDbContext"/> en core-identity.
/// Así un repositorio de identidad no sabe en qué servicio corre ni arrastra las tablas de negocio.
/// </summary>
public interface IIdentityDb
{
    DbSet<Tenant> Tenants { get; }

    DbSet<User> Users { get; }

    DbSet<UserCredential> UserCredentials { get; }

    DbSet<PasswordResetToken> PasswordResetTokens { get; }

    DbSet<UserTempSuspension> UserTempSuspensions { get; }

    DbSet<SecurityModule> SecurityModules { get; }

    DbSet<RbacAction> RbacActions { get; }

    DbSet<Role> Roles { get; }

    DbSet<RoleGrant> RoleGrants { get; }

    DbSet<UserRoleAssignment> UserRoleAssignments { get; }

    DbSet<UserInvitation> UserInvitations { get; }

    DbSet<InvitationRole> InvitationRoles { get; }

    DbSet<TenantOperationalPolicy> TenantOperationalPolicies { get; }

    DbSet<TenantConfigAuditLog> TenantConfigAuditLogs { get; }

    DbSet<TenantBrandingEntity> TenantBrandings { get; }

    DbSet<TenantBrandLogoEntity> TenantBrandLogos { get; }

    DbSet<TenantDomainEntity> TenantDomains { get; }

    DbSet<ActiveNetworkDomainView> ActiveNetworkDomains { get; }

    DbSet<TransitOfficeProfile> TransitOfficeProfiles { get; }

    /// <summary>Registro de entregas de correo; la entidad es interna a propósito (solo la escriben los adaptadores).</summary>
    internal DbSet<NotificationDeliveryLogEntity> NotificationDeliveryLogs { get; }

    DatabaseFacade Database { get; }

    ChangeTracker ChangeTracker { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716", Justification = "Mismo nombre que DbContext.Set<T>(): así lo implementan los dos contextos sin código extra.")]
    DbSet<TEntity> Set<TEntity>()
        where TEntity : class;

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    int SaveChanges();

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
