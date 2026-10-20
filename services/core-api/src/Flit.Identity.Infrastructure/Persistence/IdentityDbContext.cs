using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Entities.Security;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence;

/// <summary>
/// Contexto de core-identity (HU #13231): solo las tablas de identidad, más OpenIddict y el anillo de Data Protection.
/// <b>No tiene migraciones</b>: el esquema lo migra core-api con <c>FlitDbContext</c>, que aplica estas mismas
/// configuraciones (una prueba de paridad compara los dos modelos columna por columna).
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options), IIdentityDb, IDataProtectionKeyContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<UserTempSuspension> UserTempSuspensions => Set<UserTempSuspension>();

    public DbSet<SecurityModule> SecurityModules => Set<SecurityModule>();

    public DbSet<RbacAction> RbacActions => Set<RbacAction>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();

    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();

    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();

    public DbSet<InvitationRole> InvitationRoles => Set<InvitationRole>();

    public DbSet<TenantOperationalPolicy> TenantOperationalPolicies => Set<TenantOperationalPolicy>();

    public DbSet<TenantConfigAuditLog> TenantConfigAuditLogs => Set<TenantConfigAuditLog>();

    public DbSet<TenantBrandingEntity> TenantBrandings => Set<TenantBrandingEntity>();

    public DbSet<TenantBrandLogoEntity> TenantBrandLogos => Set<TenantBrandLogoEntity>();

    public DbSet<TenantDomainEntity> TenantDomains => Set<TenantDomainEntity>();

    public DbSet<ActiveNetworkDomainView> ActiveNetworkDomains => Set<ActiveNetworkDomainView>();

    public DbSet<TransitOfficeProfile> TransitOfficeProfiles => Set<TransitOfficeProfile>();

    DbSet<NotificationDeliveryLogEntity> IIdentityDb.NotificationDeliveryLogs => Set<NotificationDeliveryLogEntity>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(IdentityPersistence.Assembly);
        Configurations.Identity.OidcModel.Map(modelBuilder);

        // HU #13355 (Epic #13316): outbox propia de core-identity (los correos que deja a Notificaciones). La tabla la crea
        // core-api con la migración HU13355_IdentityOutbox, como el resto del esquema; no está en el modelo de core-api
        // porque allí OutboxMessage ya es tramites.outbox (IdentityModelParityTests la excluye por eso).
        modelBuilder.AddFlitOutbox("identity");
    }
}
