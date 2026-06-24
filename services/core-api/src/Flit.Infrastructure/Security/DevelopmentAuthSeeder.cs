using System.Data;
using System.Security.Cryptography;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Infrastructure.Security;

public static class DevelopmentAuthSeeder
{
    public const string DemoEmail = "demo@flit.local";
    public const string DemoPassword = "DemoPass1!";
    public const string OtAdminEmail = "otadmin@flit.local";
    public const string OtAdminPassword = "OtAdminPass1!";
    public const string DemoTenantCode = "DEMO";

    public static async Task SeedAsync(
        FlitDbContext db,
        IPasswordHasher passwordHasher,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return;

        // Seed DEV de Operación: tenant 11111111 + user 22222222 + publicación de
        // MATRICULA_NUEVA. Estos IDs son destino de las FK de procedure_instances
        // (tenant_id, created_by_user_id); sin ellos, crear un trámite falla. El SQL es
        // idempotente (ON CONFLICT / WHERE NOT EXISTS) y se ejecuta en CADA arranque en
        // Development → es self-healing: NO depende del gate de la migración HU10200_DevSeed,
        // que queda como no-op permanente si esa migración llegó a aplicarse fuera de Development.
        await ExecuteRawSqlScriptAsync(db, EmbeddedDdl.LoadUp("12-HU10200-dev-seed.sql"), cancellationToken);
        // Mirror de traspaso: publica TRASPASO_STANDARD (modalidad "traspaso"). Mismo patrón
        // idempotente y env-gated en su migración (TramitesTraspasoDevSeed) → también self-healing.
        await ExecuteRawSqlScriptAsync(db, EmbeddedDdl.LoadUp("15-tramites-traspaso-dev-seed.sql"), cancellationToken);

        if (await db.Users.AnyAsync(u => u.Email == DemoEmail, cancellationToken))
        {
            await EnsureOtAdminUserAsync(db, passwordHasher, cancellationToken: cancellationToken);
            return;
        }

        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var roleId = Guid.CreateVersion7();
        var moduleId = Guid.CreateVersion7();
        var permissionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Code = DemoTenantCode,
            LegalName = "Empresa Demo FLIT",
            TaxId = "9000000001",
            TenantType = "standard",
            IsActive = true,
            CreatedAt = now,
            RowVersion = 0,
        });

        // Persist tenant first: roles.role_permissions FK → identity.tenants.
        await db.SaveChangesAsync(cancellationToken);

        db.Users.Add(new User
        {
            Id = userId,
            Email = DemoEmail,
            DisplayName = "Usuario Demo",
            Status = "active",
            CreatedAt = now,
            RowVersion = 0,
        });

        db.UserCredentials.Add(new UserCredential
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            PasswordHash = passwordHasher.Hash(DemoPassword),
            MustChangePassword = false,
            FailedLoginAttempts = 0,
            CreatedAt = now,
            RowVersion = 0,
        });

        db.SecurityModules.Add(new SecurityModule
        {
            Id = moduleId,
            Code = "auth",
            Name = "Autenticación",
            SortOrder = 0,
            IsActive = true,
        });

        await db.SaveChangesAsync(cancellationToken);

        db.RbacActions.Add(new RbacAction
        {
            Id = permissionId,
            ModuleId = moduleId,
            Slug = "auth.me.read",
            Name = "Ver perfil autenticado",
            HttpMethod = "GET",
            RoutePattern = "/api/v1/auth/me",
            IsActive = true,
        });

        db.Roles.Add(new Role
        {
            Id = roleId,
            TenantId = tenantId,
            // El claim "role" del JWT se emite con este Code (RsaJwtTokenIssuer) y la policy
            // del módulo Admin exige RequireRole("SuperAdmin") (AdminAuthorization.SuperAdminRole).
            // Debe ser exactamente "SuperAdmin" para que el usuario demo acceda a la consola
            // de administración (compañías, OT, documental).
            Code = "SuperAdmin",
            Name = "Super Administrador",
            IsSystem = true,
            CreatedAt = now,
            RowVersion = 0,
        });

        db.RoleGrants.Add(new RoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            RoleId = roleId,
            PermissionId = permissionId,
            CreatedAt = now,
        });

        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            AssignedAt = now,
            CreatedAt = now,
            RowVersion = 0,
        });

        await db.SaveChangesAsync(cancellationToken);

        await EnsureOtAdminUserAsync(db, passwordHasher, tenantId, cancellationToken);
    }

    /// <summary>
    /// Usuario demo ot_admin para validar el módulo OT en Development (HU #10218).
    /// Comparte el tenant DEMO con el SuperAdmin de prueba.
    /// </summary>
    private static async Task EnsureOtAdminUserAsync(
        FlitDbContext db,
        IPasswordHasher passwordHasher,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(u => u.Email == OtAdminEmail, cancellationToken))
            return;

        var resolvedTenantId = tenantId
            ?? await db.Tenants
                .Where(t => t.Code == DemoTenantCode)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (resolvedTenantId == Guid.Empty)
            return;

        var userId = Guid.CreateVersion7();
        var roleId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        db.Users.Add(new User
        {
            Id = userId,
            Email = OtAdminEmail,
            DisplayName = "Administrador OT Demo",
            Status = "active",
            CreatedAt = now,
            RowVersion = 0,
        });

        db.UserCredentials.Add(new UserCredential
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            PasswordHash = passwordHasher.Hash(OtAdminPassword),
            MustChangePassword = false,
            FailedLoginAttempts = 0,
            CreatedAt = now,
            RowVersion = 0,
        });

        db.Roles.Add(new Role
        {
            Id = roleId,
            TenantId = resolvedTenantId,
            Code = "ot_admin",
            Name = "Administrador OT",
            IsSystem = true,
            CreatedAt = now,
            RowVersion = 0,
        });

        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = resolvedTenantId,
            UserId = userId,
            RoleId = roleId,
            AssignedAt = now,
            CreatedAt = now,
            RowVersion = 0,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    // Ejecuta un script SQL embebido crudo SIN pasar por el parser de format-string
    // de ExecuteSqlRaw, que interpreta '{' como placeholder posicional ({0}) y revienta
    // con FormatException ante literales jsonb como '{}'. Va por el DbConnection directo.
    // Los seeds DEV son idempotentes (ON CONFLICT / WHERE NOT EXISTS), así que ejecutarlos
    // fuera de la estrategia de reintentos es aceptable durante el arranque.
    private static async Task ExecuteRawSqlScriptAsync(FlitDbContext db, string sql, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }
    }
}

public sealed class JwtKeyMaterial
{
    public required RsaSecurityKey SigningKey { get; init; }

    public string Issuer { get; init; } = "https://api.flit.co";

    public string Audience { get; init; } = "flit-api";
}

public static class JwtKeyMaterialLoader
{
    public static JwtKeyMaterial Load(JwtSettings settings, IHostEnvironment environment)
    {
        var pem = settings.PrivateKeyPem;
        if (string.IsNullOrWhiteSpace(pem) && !string.IsNullOrWhiteSpace(settings.PrivateKeyPath)
            && File.Exists(settings.PrivateKeyPath))
            pem = File.ReadAllText(settings.PrivateKeyPath);

        RSA rsa;
        if (string.IsNullOrWhiteSpace(pem))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("JWT private key is required outside Development.");

            rsa = RSA.Create(2048);
        }
        else
        {
            rsa = RSA.Create();
            rsa.ImportFromPem(pem);
        }

        return new JwtKeyMaterial
        {
            SigningKey = new RsaSecurityKey(rsa),
            Issuer = settings.Issuer,
            Audience = settings.Audience,
        };
    }
}
