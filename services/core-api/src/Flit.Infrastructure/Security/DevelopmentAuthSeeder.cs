using System.Data;
using System.Security.Cryptography;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
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

    /// <summary>Admin OT Sabaneta (DEV) — tenant vinculado al catálogo RUNT 5631000.</summary>
    public const string OtSabanetaEmail = "otsabaneta@flit.local";
    public const string OtSabanetaPassword = "OtSabaneta1!";

    /// <summary>Admin OT Envigado (DEV) — tenant vinculado al catálogo RUNT 5266000.</summary>
    public const string OtEnvigadoEmail = "otenvigado@flit.local";
    public const string OtEnvigadoPassword = "OtEnvigado1!";

    /// <summary>Usuario fijo del tab Operación (HU #10200); el SQL seed crea la fila sin credencial.</summary>
    public const string DevOperacionEmail = "dev@flitsas.io";
    public const string DevOperacionPassword = "DevPass1!";
    public const string DemoTenantCode = "DEMO";

    public const string DemoAdminCompanyEmail = "admin@empresa.local";
    public const string DemoAdminCompanyPassword = "AdminPass1!";
    public const string DemoEmpresaTenantCode = "EMPRESA_DEMO";

    /// <summary>Radicador de pruebas en EMPRESA_DEMO (compañía sin configurar): rol propio con acceso
    /// al módulo Trámites (leer + crear). Sirve para probar el flujo de radicación / matrícula inicial.</summary>
    public const string DemoRadicadorEmail = "radicador@empresa.local";
    public const string DemoRadicadorPassword = "RadicadorPass1!";

    /// <summary>Tenant OT fijo para validación E2E del módulo /admin/transit-offices (HU #10133).</summary>
    public static readonly Guid OtDevTenantId =
        Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001");

    /// <summary>Tenant OT Sabaneta (DEV) — perfil → catálogo <c>5631000</c>.</summary>
    public static readonly Guid OtSabanetaTenantId =
        Guid.Parse("bbbbbbbb-0003-4000-8000-000000000001");

    /// <summary>Oficina Sabaneta en <c>catalogs.transit_offices</c> (seed RUNT HU #10659).</summary>
    public static readonly Guid OtSabanetaCatalogOfficeId =
        Guid.Parse("ba575641-ea48-5cd2-ac51-ebba02584ba5");

    /// <summary>Usuario ot_admin fijo — alineado con seed SQL y FK changed_by en trámites.</summary>
    public static readonly Guid OtAdminUserId =
        Guid.Parse("ec4dddb9-ade5-43e8-b33b-c6036eba49d0");

    public static readonly Guid OtSabanetaUserId =
        Guid.Parse("ec4dddb9-ade5-43e8-b33b-c6036eba49d1");

    public static readonly Guid OtSabanetaProfileId =
        Guid.Parse("b9ec839d-7b78-4165-8860-cf29b104c76d");

    /// <summary>Tenant OT Envigado (DEV) — perfil → catálogo <c>5266000</c>.</summary>
    public static readonly Guid OtEnvigadoTenantId =
        Guid.Parse("bbbbbbbb-0004-4000-8000-000000000001");

    /// <summary>Oficina Envigado en <c>catalogs.transit_offices</c> (seed RUNT HU #10659).</summary>
    public static readonly Guid OtEnvigadoCatalogOfficeId =
        Guid.Parse("69f48545-a7cf-5201-9198-6e3b3fab9a99");

    public static readonly Guid OtEnvigadoUserId =
        Guid.Parse("ec4dddb9-ade5-43e8-b33b-c6036eba49d2");

    public static readonly Guid OtEnvigadoProfileId =
        Guid.Parse("b9ec839d-7b78-4165-8860-cf29b104c76e");

    public static async Task SeedAsync(
        FlitDbContext db,
        IPasswordHasher passwordHasher,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return;

        await ExecuteRawSqlScriptAsync(db, EmbeddedDdl.LoadUp("12-HU10200-dev-seed.sql"), cancellationToken);
        await ExecuteRawSqlScriptAsync(db, EmbeddedDdl.LoadUp("15-tramites-traspaso-dev-seed.sql"), cancellationToken);
        await ExecuteRawSqlScriptAsync(db, EmbeddedDdl.LoadUp("16-HU10133-ot-admin-dev-seed.sql"), cancellationToken);
        await ExecuteRawSqlScriptAsync(db, EmbeddedDdl.LoadUp("27-HU10659-transit-offices-runt-catalog-seed.sql"), cancellationToken);

        await SeedSuperAdminAsync(db, passwordHasher, cancellationToken);
        await SeedAdminCompanyUserAsync(db, passwordHasher, cancellationToken);
        await EnsureDevOperacionCredentialsAsync(db, passwordHasher, cancellationToken);
        await SeedSabanetaOtAdminAsync(db, passwordHasher, cancellationToken);
        await SeedEnvigadoOtAdminAsync(db, passwordHasher, cancellationToken);
        await SeedBaseModulesAsync(db, cancellationToken);
        await SeedReportesPermissionsAsync(db, cancellationToken);
        await SeedDetailedReportPermissionsAsync(db, cancellationToken);
        await SeedLogQxPermissionsAsync(db, cancellationToken);
        await SeedIctLogsPermissionsAsync(db, cancellationToken);
        await SeedIctClientsPermissionsAsync(db, cancellationToken);
        await SeedResetPasswordPermissionsAsync(db, cancellationToken);
        await SeedRadicadorUserAsync(db, passwordHasher, cancellationToken);
    }

    /// <summary>
    /// Crea un usuario RADICADOR de pruebas (<see cref="DemoRadicadorEmail"/>) en el tenant
    /// EMPRESA_DEMO con un rol propio "Radicador" que solo tiene acceso al módulo Trámites
    /// (leer + crear) y al Dashboard. EMPRESA_DEMO no tiene configuración operativa, por lo que
    /// sirve para verificar que la matrícula inicial nace apagada (sin config → no permitida).
    /// Idempotente: reusa rol/usuario/credencial/asignación si ya existen.
    /// </summary>
    private static async Task SeedRadicadorUserAsync(
        FlitDbContext db,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var empresaTenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Code == DemoEmpresaTenantCode, cancellationToken);
        if (empresaTenant is null)
            return;

        // 1. Rol "Radicador" (idempotente). HU #10505 / ADR-0023: security.roles es un catálogo
        //    GLOBAL (sin tenant_id) — se busca/crea por Code + target_entity_type, no por tenant.
        var role = await db.Roles.FirstOrDefaultAsync(
            r => r.Code == "Radicador" && r.TargetEntityType == "COMPANY" && r.DeletedAt == null,
            cancellationToken);
        if (role is null)
        {
            role = new Role
            {
                Id = Guid.CreateVersion7(),
                Code = "Radicador",
                Name = "Radicador",
                TargetEntityType = "COMPANY",
                IsSystem = false,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                RowVersion = 0,
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync(cancellationToken);
        }

        // 2. Grants: acceso a Operación (trámites: leer + crear) y al dashboard. Solo se agregan
        //    los permisos que aún no tenga el rol (idempotente).
        var slugs = new[] { "dashboard.read", "tramites.read", "tramites.create" };
        var actions = await db.RbacActions
            .Where(a => slugs.Contains(a.Slug))
            .ToListAsync(cancellationToken);
        var existingGrants = await db.RoleGrants
            .Where(g => g.RoleId == role.Id)
            .Select(g => g.PermissionId)
            .ToListAsync(cancellationToken);
        var toGrant = actions.Where(a => !existingGrants.Contains(a.Id)).ToList();
        if (toGrant.Count > 0)
        {
            var grantedAt = DateTimeOffset.UtcNow;
            db.RoleGrants.AddRange(toGrant.Select(a => new RoleGrant
            {
                Id = Guid.CreateVersion7(),
                RoleId = role.Id,
                PermissionId = a.Id,
                CreatedAt = grantedAt,
            }));
            await db.SaveChangesAsync(cancellationToken);
        }

        // 3. Usuario + credencial (idempotente).
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == DemoRadicadorEmail, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                Id = Guid.CreateVersion7(),
                Email = DemoRadicadorEmail,
                DisplayName = "Radicador Empresa Demo",
                Status = "active",
                HomeTenantId = empresaTenant.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                RowVersion = 0,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
        }

        await EnsureUserCredentialsAsync(db, user.Id, DemoRadicadorPassword, passwordHasher, cancellationToken);

        // 4. Asignación de rol: rol único por usuario/tenant (uq_ura_active_user_tenant).
        await EnsureSingleRoleAssignmentAsync(db, user.Id, empresaTenant.Id, role.Id, cancellationToken);
    }

    /// <summary>
    /// Deja al usuario con EXACTAMENTE una asignación activa, la del rol indicado.
    ///
    /// <para>Reusa la fila que ya exista en vez de crear otra, y cierra cualquier asignación
    /// activa sobrante: la tabla guarda histórico en soft-delete, así que un usuario puede
    /// arrastrar varias filas —incluidas las que dejó el modelo aditivo de la HU #10506— y
    /// reabrir una a ciegas viola <c>uq_ura_active_user_tenant</c> y tumba el arranque.</para>
    /// </summary>
    private static async Task EnsureSingleRoleAssignmentAsync(
        FlitDbContext db, Guid userId, Guid tenantId, Guid roleId, CancellationToken cancellationToken)
    {
        var assignments = await db.UserRoleAssignments
            .Where(a => a.UserId == userId && a.TenantId == tenantId)
            .OrderByDescending(a => a.AssignedAt)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // Se prefiere una fila activa; si no hay, se reutiliza la más reciente del histórico.
        var target = assignments.Find(a => a.DeletedAt is null) ?? assignments.FirstOrDefault();

        if (target is null)
        {
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
            return;
        }

        foreach (var other in assignments.Where(a => a != target && a.DeletedAt is null))
            other.DeletedAt = now;

        target.DeletedAt = null;
        target.DeletedBy = null;
        target.RoleId = roleId;
        target.AssignedAt = now;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedSuperAdminAsync(
        FlitDbContext db,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.Email == DemoEmail, cancellationToken))
        {
            await EnsureOtAdminUserAsync(db, passwordHasher, OtDevTenantId, cancellationToken);
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
            TenantType = "FLIT",
            IsActive = true,
            CreatedAt = now,
            RowVersion = 0,
        });

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

        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id). SuperAdmin
        // es transversal a todos los tenants, pero el enum target_entity_type solo admite
        // COMPANY|TRANSIT_OFFICE (no hay un tercer valor "GLOBAL"/"SYSTEM") — se usa COMPANY como
        // default y no se expone en las pantallas de gestión de roles por tipo de entidad
        // (decisión documentada en ADR-0023).
        db.Roles.Add(new Role
        {
            Id = roleId,
            Code = "SuperAdmin",
            Name = "Super Administrador",
            TargetEntityType = "COMPANY",
            IsSystem = true,
            IsActive = true,
            CreatedAt = now,
            RowVersion = 0,
        });

        db.RoleGrants.Add(new RoleGrant
        {
            Id = Guid.CreateVersion7(),
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

        await EnsureOtAdminUserAsync(db, passwordHasher, OtDevTenantId, cancellationToken);
    }

    /// <summary>
    /// Usuario demo ot_admin para validar el módulo OT en Development (HU #10218 / #10133).
    /// Se asigna al tenant fijo <see cref="OtDevTenantId"/>.
    /// </summary>
    private static async Task EnsureOtAdminUserAsync(
        FlitDbContext db,
        IPasswordHasher passwordHasher,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedTenantId = tenantId ?? OtDevTenantId;

        var existingUser = await db.Users
            .FirstOrDefaultAsync(u => u.Email == OtAdminEmail, cancellationToken);

        if (existingUser is not null)
        {
            await EnsureUserCredentialsAsync(
                db, existingUser.Id, OtAdminPassword, passwordHasher, cancellationToken);
            await EnsureOtAdminAssignmentAsync(db, existingUser.Id, resolvedTenantId, cancellationToken);
            return;
        }

        var userId = OtAdminUserId;
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

        await db.SaveChangesAsync(cancellationToken);

        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id) — se
        // busca/crea "ot_admin" por Code + target_entity_type, nunca por tenant (UNIQUE(code,
        // target_entity_type) impediría una segunda fila si ya existe de un tenant OT previo).
        var role = await db.Roles.FirstOrDefaultAsync(
            r => r.Code == "ot_admin" && r.TargetEntityType == "TRANSIT_OFFICE" && r.DeletedAt == null,
            cancellationToken);
        if (role is null)
        {
            role = new Role
            {
                Id = Guid.CreateVersion7(),
                Code = "ot_admin",
                Name = "Administrador OT",
                TargetEntityType = "TRANSIT_OFFICE",
                IsSystem = true,
                IsActive = true,
                CreatedAt = now,
                RowVersion = 0,
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync(cancellationToken);
        }

        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = resolvedTenantId,
            UserId = userId,
            RoleId = role.Id,
            AssignedAt = now,
            CreatedAt = now,
            RowVersion = 0,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Tenant OT Sabaneta + perfil sobre catálogo RUNT 5631000 + usuario
    /// <see cref="OtSabanetaEmail"/> / <see cref="OtSabanetaPassword"/> (DEV).
    /// Idempotente. Requiere que el seed del catálogo RUNT (HU #10659) haya corrido.
    /// </summary>
    private static async Task SeedSabanetaOtAdminAsync(
        FlitDbContext db,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var catalogExists = await db.TransitOffices
            .AnyAsync(o => o.Id == OtSabanetaCatalogOfficeId, cancellationToken);
        if (!catalogExists)
            return;

        var now = DateTimeOffset.UtcNow;

        // 1. Tenant OT-SABANETA
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == OtSabanetaTenantId || t.Code == "OT-SABANETA", cancellationToken);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = OtSabanetaTenantId,
                Code = "OT-SABANETA",
                LegalName = "Secretaría de Tránsito y Transporte de Sabaneta (DEV)",
                TaxId = "900273813-7",
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = now,
                RowVersion = 0,
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);
        }

        var tenantId = tenant.Id;

        // 2. Perfil OT → catálogo Sabaneta (una oficina física = un solo tenant)
        var existingProfileForOffice = await db.TransitOfficeProfiles
            .FirstOrDefaultAsync(p => p.TransitOfficeId == OtSabanetaCatalogOfficeId, cancellationToken);
        if (existingProfileForOffice is null)
        {
            db.TransitOfficeProfiles.Add(new TransitOfficeProfile
            {
                Id = OtSabanetaProfileId,
                TenantId = tenantId,
                TransitOfficeId = OtSabanetaCatalogOfficeId,
                OperationMode = "dashboard",
                QuipuxReadOnly = false,
                CreatedAt = now,
                RowVersion = 0,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // Si ya hay perfil (p. ej. creado por UI), asignar el usuario a ESE tenant.
            tenantId = existingProfileForOffice.TenantId;
        }

        // 3. Usuario + credencial
        var existingUser = await db.Users
            .FirstOrDefaultAsync(u => u.Email == OtSabanetaEmail, cancellationToken);
        if (existingUser is not null)
        {
            await EnsureUserCredentialsAsync(
                db, existingUser.Id, OtSabanetaPassword, passwordHasher, cancellationToken);
            await EnsureOtAdminAssignmentAsync(db, existingUser.Id, tenantId, cancellationToken);
            return;
        }

        db.Users.Add(new User
        {
            Id = OtSabanetaUserId,
            Email = OtSabanetaEmail,
            DisplayName = "Administrador OT Sabaneta",
            Status = "active",
            CreatedAt = now,
            RowVersion = 0,
        });

        db.UserCredentials.Add(new UserCredential
        {
            Id = Guid.CreateVersion7(),
            UserId = OtSabanetaUserId,
            PasswordHash = passwordHasher.Hash(OtSabanetaPassword),
            MustChangePassword = false,
            FailedLoginAttempts = 0,
            CreatedAt = now,
            RowVersion = 0,
        });

        await db.SaveChangesAsync(cancellationToken);
        await EnsureOtAdminAssignmentAsync(db, OtSabanetaUserId, tenantId, cancellationToken);
    }

    /// <summary>
    /// Tenant OT Envigado + perfil sobre catálogo RUNT 5266000 + usuario
    /// <see cref="OtEnvigadoEmail"/> / <see cref="OtEnvigadoPassword"/> (DEV).
    /// Idempotente. Requiere que el seed del catálogo RUNT (HU #10659) haya corrido.
    /// </summary>
    private static async Task SeedEnvigadoOtAdminAsync(
        FlitDbContext db,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var catalogExists = await db.TransitOffices
            .AnyAsync(o => o.Id == OtEnvigadoCatalogOfficeId, cancellationToken);
        if (!catalogExists)
            return;

        var now = DateTimeOffset.UtcNow;

        // 1. Tenant OT-ENVIGADO
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == OtEnvigadoTenantId || t.Code == "OT-ENVIGADO", cancellationToken);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = OtEnvigadoTenantId,
                Code = "OT-ENVIGADO",
                LegalName = "STRIA TTEyTTO ENVIGADO (DEV)",
                TaxId = "900000266-5",
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = now,
                RowVersion = 0,
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);
        }

        var tenantId = tenant.Id;

        // 2. Perfil OT → catálogo Envigado (una oficina física = un solo tenant)
        var existingProfileForOffice = await db.TransitOfficeProfiles
            .FirstOrDefaultAsync(p => p.TransitOfficeId == OtEnvigadoCatalogOfficeId, cancellationToken);
        if (existingProfileForOffice is null)
        {
            db.TransitOfficeProfiles.Add(new TransitOfficeProfile
            {
                Id = OtEnvigadoProfileId,
                TenantId = tenantId,
                TransitOfficeId = OtEnvigadoCatalogOfficeId,
                OperationMode = "dashboard",
                QuipuxReadOnly = false,
                CreatedAt = now,
                RowVersion = 0,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            tenantId = existingProfileForOffice.TenantId;
        }

        // 3. Usuario + credencial
        var existingUser = await db.Users
            .FirstOrDefaultAsync(u => u.Email == OtEnvigadoEmail, cancellationToken);
        if (existingUser is not null)
        {
            await EnsureUserCredentialsAsync(
                db, existingUser.Id, OtEnvigadoPassword, passwordHasher, cancellationToken);
            await EnsureOtAdminAssignmentAsync(db, existingUser.Id, tenantId, cancellationToken);
            return;
        }

        db.Users.Add(new User
        {
            Id = OtEnvigadoUserId,
            Email = OtEnvigadoEmail,
            DisplayName = "Administrador OT Envigado",
            Status = "active",
            HomeTenantId = tenantId,
            CreatedAt = now,
            RowVersion = 0,
        });

        db.UserCredentials.Add(new UserCredential
        {
            Id = Guid.CreateVersion7(),
            UserId = OtEnvigadoUserId,
            PasswordHash = passwordHasher.Hash(OtEnvigadoPassword),
            MustChangePassword = false,
            FailedLoginAttempts = 0,
            CreatedAt = now,
            RowVersion = 0,
        });

        await db.SaveChangesAsync(cancellationToken);
        await EnsureOtAdminAssignmentAsync(db, OtEnvigadoUserId, tenantId, cancellationToken);
    }

    /// <summary>
    /// El SQL seed HU #10200 inserta <see cref="DevOperacionEmail"/> sin fila en
    /// user_credentials; sin esto el login local devuelve 401 aunque el usuario exista.
    /// </summary>
    private static async Task EnsureDevOperacionCredentialsAsync(
        FlitDbContext db,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == DevOperacionEmail, cancellationToken);
        if (user is null)
            return;

        await EnsureUserCredentialsAsync(
            db, user.Id, DevOperacionPassword, passwordHasher, cancellationToken);
    }

    private static async Task EnsureUserCredentialsAsync(
        FlitDbContext db,
        Guid userId,
        string plainPassword,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var hasCredential = await db.UserCredentials
            .AnyAsync(c => c.UserId == userId, cancellationToken);
        if (hasCredential)
            return;

        var now = DateTimeOffset.UtcNow;
        db.UserCredentials.Add(new UserCredential
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            PasswordHash = passwordHasher.Hash(plainPassword),
            MustChangePassword = false,
            FailedLoginAttempts = 0,
            CreatedAt = now,
            RowVersion = 0,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureOtAdminAssignmentAsync(
        FlitDbContext db,
        Guid userId,
        Guid otTenantId,
        CancellationToken cancellationToken)
    {
        var staleAssignments = await db.UserRoleAssignments
            .Where(a => a.UserId == userId && a.TenantId != otTenantId && a.DeletedAt == null)
            .ToListAsync(cancellationToken);

        if (staleAssignments.Count > 0)
        {
            db.UserRoleAssignments.RemoveRange(staleAssignments);
            await db.SaveChangesAsync(cancellationToken);
        }

        var hasOtAssignment = await db.UserRoleAssignments
            .AnyAsync(
                a => a.UserId == userId && a.TenantId == otTenantId && a.DeletedAt == null,
                cancellationToken);

        if (hasOtAssignment)
        {
            return;
        }

        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id) — se
        // busca/crea "ot_admin" por Code + target_entity_type, nunca por tenant.
        var role = await db.Roles
            .FirstOrDefaultAsync(
                r => r.Code == "ot_admin" && r.TargetEntityType == "TRANSIT_OFFICE" && r.DeletedAt == null,
                cancellationToken);

        if (role is null)
        {
            var roleId = Guid.CreateVersion7();
            var now = DateTimeOffset.UtcNow;
            role = new Role
            {
                Id = roleId,
                Code = "ot_admin",
                Name = "Administrador OT",
                TargetEntityType = "TRANSIT_OFFICE",
                IsSystem = true,
                IsActive = true,
                CreatedAt = now,
                RowVersion = 0,
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync(cancellationToken);
        }

        var assignedAt = DateTimeOffset.UtcNow;
        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = otTenantId,
            UserId = userId,
            RoleId = role.Id,
            AssignedAt = assignedAt,
            CreatedAt = assignedAt,
            RowVersion = 0,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedAdminCompanyUserAsync(
        FlitDbContext db,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var existingUser = await db.Users
            .FirstOrDefaultAsync(u => u.Email == DemoAdminCompanyEmail, cancellationToken);

        if (existingUser is not null)
        {
            // Reparar si el role assignment fue soft-deleted en una sesión previa
            await EnsureAdminCompanyRoleAssignmentAsync(db, existingUser.Id, cancellationToken);
            return;
        }

        // AdminCompany vive en su PROPIO tenant (EMPRESA_DEMO), separado del tenant
        // del SuperAdmin (DEMO). Esto garantiza aislamiento real en dev: AdminCompany
        // solo ve usuarios de su propia compañía, no los del SuperAdmin.
        var empresaTenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Code == DemoEmpresaTenantCode, cancellationToken);

        if (empresaTenant is null)
        {
            empresaTenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                Code = DemoEmpresaTenantCode,
                LegalName = "Empresa Demo S.A.S",
                TaxId = "9000000002",
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                RowVersion = 0,
            };
            db.Tenants.Add(empresaTenant);
            await db.SaveChangesAsync(cancellationToken);
        }

        var userId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id) — se
        // busca/crea "AdminCompany" por Code + target_entity_type, nunca por tenant (evita
        // violar UNIQUE(code, target_entity_type) si ya existe de un run/tenant previo).
        var adminCompanyRole = await db.Roles.FirstOrDefaultAsync(
            r => r.Code == "AdminCompany" && r.TargetEntityType == "COMPANY" && r.DeletedAt == null,
            cancellationToken);
        if (adminCompanyRole is null)
        {
            adminCompanyRole = new Role
            {
                Id = Guid.CreateVersion7(),
                Code = "AdminCompany",
                Name = "Administrador de Compañía",
                TargetEntityType = "COMPANY",
                IsSystem = true,
                IsActive = true,
                CreatedAt = now,
                RowVersion = 0,
            };
            db.Roles.Add(adminCompanyRole);
        }

        db.Users.Add(new User
        {
            Id = userId,
            Email = DemoAdminCompanyEmail,
            DisplayName = "Admin Empresa Demo",
            Status = "active",
            HomeTenantId = empresaTenant.Id,
            CreatedAt = now,
            RowVersion = 0,
        });

        db.UserCredentials.Add(new UserCredential
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            PasswordHash = passwordHasher.Hash(DemoAdminCompanyPassword),
            MustChangePassword = false,
            FailedLoginAttempts = 0,
            CreatedAt = now,
            RowVersion = 0,
        });

        await db.SaveChangesAsync(cancellationToken);

        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = empresaTenant.Id,
            UserId = userId,
            RoleId = adminCompanyRole.Id,
            AssignedAt = now,
            CreatedAt = now,
            RowVersion = 0,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureAdminCompanyRoleAssignmentAsync(
        FlitDbContext db,
        Guid userId,
        CancellationToken ct)
    {
        var empresaTenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Code == DemoEmpresaTenantCode, ct);
        if (empresaTenant is null) return;

        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id).
        var adminCompanyRole = await db.Roles
            .FirstOrDefaultAsync(r => r.Code == "AdminCompany" && r.TargetEntityType == "COMPANY" && r.DeletedAt == null, ct);
        if (adminCompanyRole is null) return;

        await EnsureSingleRoleAssignmentAsync(db, userId, empresaTenant.Id, adminCompanyRole.Id, ct);
    }

    private static async Task SeedBaseModulesAsync(
        FlitDbContext db,
        CancellationToken cancellationToken)
    {
        if (await db.SecurityModules.AnyAsync(m => m.Code == "dashboard", cancellationToken))
            return;

        var now = DateTimeOffset.UtcNow;

        var modules = new SecurityModule[]
        {
            new() { Id = Guid.CreateVersion7(), Code = "dashboard",    Name = "Dashboard",                SortOrder = 1, IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), Code = "tramites",     Name = "Trámites",                 SortOrder = 2, IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), Code = "reportes",     Name = "Reportes",                 SortOrder = 3, IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), Code = "validaciones", Name = "Validaciones",             SortOrder = 4, IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), Code = "usuarios",     Name = "Usuarios y Permisos",      SortOrder = 5, IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), Code = "rbac",         Name = "RBAC Admin",               SortOrder = 6, IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), Code = "improntas",    Name = "Improntas",                SortOrder = 7, IsActive = true, CreatedAt = now },
        };

        db.SecurityModules.AddRange(modules);
        await db.SaveChangesAsync(cancellationToken);

        var mid = modules.ToDictionary(m => m.Code, m => m.Id);

        var actions = new RbacAction[]
        {
            new() { Id = Guid.CreateVersion7(), ModuleId = mid["dashboard"],    Slug = "dashboard.read",        Name = "Ver dashboard",                 HttpMethod = "GET",  RoutePattern = "/api/v1/analytics/overview",         IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), ModuleId = mid["tramites"],     Slug = "tramites.read",         Name = "Ver trámites",                  HttpMethod = "GET",  RoutePattern = "/api/v1/tramites",                   IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), ModuleId = mid["tramites"],     Slug = "tramites.create",       Name = "Crear trámite",                 HttpMethod = "POST", RoutePattern = "/api/v1/tramites",                   IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), ModuleId = mid["reportes"],     Slug = "reportes.read",         Name = "Ver reportes",                  HttpMethod = "GET",  RoutePattern = "/api/v1/reportes",                   IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), ModuleId = mid["validaciones"], Slug = "validaciones.read",     Name = "Ver validaciones",              HttpMethod = "GET",  RoutePattern = "/api/v1/validaciones",               IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), ModuleId = mid["validaciones"], Slug = "validaciones.manage",   Name = "Gestionar validaciones",        HttpMethod = "PUT",  RoutePattern = "/api/v1/validaciones/{id}/approve",  IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), ModuleId = mid["usuarios"],     Slug = "usuarios.manage",       Name = "Gestionar usuarios y permisos", HttpMethod = "GET",  RoutePattern = "/api/v1/security/users",             IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), ModuleId = mid["rbac"],         Slug = "rbac.manage",           Name = "Administrar RBAC",              HttpMethod = "GET",  RoutePattern = "/api/v1/superadmin/modules",         IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), ModuleId = mid["improntas"],    Slug = "improntas.read",        Name = "Ver improntas",                 HttpMethod = "GET",  RoutePattern = "/api/v1/admin/improntas",            IsActive = true, CreatedAt = now },
            new() { Id = Guid.CreateVersion7(), ModuleId = mid["improntas"],    Slug = "improntas.generate",    Name = "Generar impronta",              HttpMethod = "POST", RoutePattern = "/api/v1/admin/improntas/generate",   IsActive = true, CreatedAt = now },
        };

        db.RbacActions.AddRange(actions);
        await db.SaveChangesAsync(cancellationToken);

        // SuperAdmin: todos los permisos
        var superAdminRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "SuperAdmin", cancellationToken);
        if (superAdminRole is not null)
        {
            var existingSA = await db.RoleGrants
                .Where(g => g.RoleId == superAdminRole.Id)
                .Select(g => g.PermissionId)
                .ToListAsync(cancellationToken);

            db.RoleGrants.AddRange(actions
                .Where(a => !existingSA.Contains(a.Id))
                .Select(a => new RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    RoleId = superAdminRole.Id,
                    PermissionId = a.Id,
                    CreatedAt = now,
                }));
        }

        // AdminCompany: todo excepto rbac.manage
        var adminCompanyRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "AdminCompany", cancellationToken);
        if (adminCompanyRole is not null)
        {
            var existingAC = await db.RoleGrants
                .Where(g => g.RoleId == adminCompanyRole.Id)
                .Select(g => g.PermissionId)
                .ToListAsync(cancellationToken);

            db.RoleGrants.AddRange(actions
                .Where(a => a.Slug != "rbac.manage" && !existingAC.Contains(a.Id))
                .Select(a => new RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    RoleId = adminCompanyRole.Id,
                    PermissionId = a.Id,
                    CreatedAt = now,
                }));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Reportes 2.0 — permisos por pestaña + administración de programación/alertas
    /// (docs/contratos-reportes-v2.md §3). Idempotente y separado de SeedBaseModulesAsync
    /// (que hace early-return en BDs ya sembradas): agrega solo los slugs que falten al
    /// módulo "reportes" y los concede a SuperAdmin y AdminCompany.
    /// </summary>
    private static async Task SeedReportesPermissionsAsync(FlitDbContext db, CancellationToken ct)
    {
        var reportesModule = await db.SecurityModules
            .FirstOrDefaultAsync(m => m.Code == "reportes" && m.DeletedAt == null, ct);
        if (reportesModule is null)
            return;

        var now = DateTimeOffset.UtcNow;

        var slugs = new (string Slug, string Name, string RoutePattern)[]
        {
            ("reportes.resumen.read",        "Ver pestaña Resumen general",        "/api/v1/analytics/overview"),
            ("reportes.operacion.read",      "Ver pestaña Operación/Trámites",     "/api/v1/analytics/funnel"),
            ("reportes.ot.read",             "Ver pestaña Organismo de Tránsito",  "/api/v1/analytics/ot-metrics"),
            ("reportes.uso.read",            "Ver pestaña Uso del aplicativo",     "/api/v1/analytics/usage"),
            ("reportes.productividad.read",  "Ver pestaña Productividad",          "/api/v1/analytics/productivity/top"),
            ("reportes.consultas.read",      "Ver pestaña Consultas",              "/api/v1/analytics/queries/run"),
            ("reportes.programacion.manage", "Administrar informes programados y alertas", "/api/v1/analytics/report-schedules"),
        };

        var existingSlugs = await db.RbacActions
            .Where(a => a.ModuleId == reportesModule.Id)
            .Select(a => a.Slug)
            .ToListAsync(ct);

        var newActions = slugs
            .Where(s => !existingSlugs.Contains(s.Slug))
            .Select(s => new RbacAction
            {
                Id = Guid.CreateVersion7(),
                ModuleId = reportesModule.Id,
                Slug = s.Slug,
                Name = s.Name,
                HttpMethod = s.Slug.EndsWith(".manage", StringComparison.Ordinal) ? "POST" : "GET",
                RoutePattern = s.RoutePattern,
                IsActive = true,
                CreatedAt = now,
            })
            .ToArray();

        if (newActions.Length == 0)
            return;

        db.RbacActions.AddRange(newActions);
        await db.SaveChangesAsync(ct);

        // Grants: SuperAdmin y AdminCompany reciben todos los permisos nuevos de reportes.
        foreach (var roleCode in new[] { "SuperAdmin", "AdminCompany" })
        {
            var roles = await db.Roles.Where(r => r.Code == roleCode).ToListAsync(ct);
            foreach (var role in roles)
            {
                var existing = await db.RoleGrants
                    .Where(g => g.RoleId == role.Id)
                    .Select(g => g.PermissionId)
                    .ToListAsync(ct);

                db.RoleGrants.AddRange(newActions
                    .Where(a => !existing.Contains(a.Id))
                    .Select(a => new RoleGrant
                    {
                        Id = Guid.CreateVersion7(),
                        RoleId = role.Id,
                        PermissionId = a.Id,
                        CreatedAt = now,
                    }));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Feature #10813 — módulo dock "Reportes Detallados" + permisos de lectura y export.
    /// Idempotente sobre BDs ya sembradas.
    /// </summary>
    private static async Task SeedDetailedReportPermissionsAsync(FlitDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var module = await db.SecurityModules
            .FirstOrDefaultAsync(m => m.Code == "reportes-detallados" && m.DeletedAt == null, ct);

        if (module is null)
        {
            module = new SecurityModule
            {
                Id = Guid.CreateVersion7(),
                Code = "reportes-detallados",
                Name = "Reportes Detallados",
                SortOrder = 8,
                IsActive = true,
                CreatedAt = now,
            };
            db.SecurityModules.Add(module);
            await db.SaveChangesAsync(ct);
        }

        var slugs = new (string Slug, string Name, string RoutePattern, string Method)[]
        {
            ("reportes.detallados.read",   "Ver reportes detallados",   "/api/v1/detailed-report/procedures",        "GET"),
            ("reportes.detallados.export", "Exportar reportes detallados", "/api/v1/detailed-report/procedures/export", "GET"),
        };

        var existingSlugs = await db.RbacActions
            .Where(a => a.ModuleId == module.Id)
            .Select(a => a.Slug)
            .ToListAsync(ct);

        var newActions = slugs
            .Where(s => !existingSlugs.Contains(s.Slug))
            .Select(s => new RbacAction
            {
                Id = Guid.CreateVersion7(),
                ModuleId = module.Id,
                Slug = s.Slug,
                Name = s.Name,
                HttpMethod = s.Method,
                RoutePattern = s.RoutePattern,
                IsActive = true,
                CreatedAt = now,
            })
            .ToArray();

        if (newActions.Length == 0)
            return;

        db.RbacActions.AddRange(newActions);
        await db.SaveChangesAsync(ct);

        foreach (var roleCode in new[] { "SuperAdmin", "AdminCompany" })
        {
            var roles = await db.Roles.Where(r => r.Code == roleCode).ToListAsync(ct);
            foreach (var role in roles)
            {
                var existing = await db.RoleGrants
                    .Where(g => g.RoleId == role.Id)
                    .Select(g => g.PermissionId)
                    .ToListAsync(ct);

                db.RoleGrants.AddRange(newActions
                    .Where(a => !existing.Contains(a.Id))
                    .Select(a => new RoleGrant
                    {
                        Id = Guid.CreateVersion7(),
                        RoleId = role.Id,
                        PermissionId = a.Id,
                        CreatedAt = now,
                    }));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// LOG QX (HU #10794) — módulo <c>logqx</c> + permiso <c>logqx.read</c> que protege
    /// <c>GET /api/v1/admin/log-qx</c>. Idempotente y separado de <see cref="SeedBaseModulesAsync"/>
    /// (que hace early-return en BDs ya sembradas): crea el módulo y el permiso si faltan y concede el
    /// permiso a SuperAdmin. SuperAdmin además bypassa por rol en runtime; el grant deja el permiso
    /// asignado y visible para gestión RBAC (p. ej. para asignarlo luego a un rol de soporte). No se
    /// concede a AdminCompany: el LOG QX es una herramienta de diagnóstico cross-tenant de
    /// soporte/administración FLIT, no de administradores de compañía.
    /// </summary>
    private static async Task SeedLogQxPermissionsAsync(FlitDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var module = await db.SecurityModules
            .FirstOrDefaultAsync(m => m.Code == "logqx" && m.DeletedAt == null, ct);
        if (module is null)
        {
            module = new SecurityModule
            {
                Id = Guid.CreateVersion7(),
                Code = "logqx",
                Name = "LOG QX",
                SortOrder = 8,
                IsActive = true,
                CreatedAt = now,
            };
            db.SecurityModules.Add(module);
            await db.SaveChangesAsync(ct);
        }

        var action = await db.RbacActions
            .FirstOrDefaultAsync(a => a.Slug == "logqx.read", ct);
        if (action is null)
        {
            action = new RbacAction
            {
                Id = Guid.CreateVersion7(),
                ModuleId = module.Id,
                Slug = "logqx.read",
                Name = "Ver LOG QX",
                HttpMethod = "GET",
                RoutePattern = "/api/v1/admin/log-qx",
                IsActive = true,
                CreatedAt = now,
            };
            db.RbacActions.Add(action);
            await db.SaveChangesAsync(ct);
        }

        // Grant a SuperAdmin (idempotente): solo si aún no lo tiene.
        var superAdminRoles = await db.Roles
            .Where(r => r.Code == "SuperAdmin")
            .ToListAsync(ct);
        foreach (var role in superAdminRoles)
        {
            var alreadyGranted = await db.RoleGrants
                .AnyAsync(g => g.RoleId == role.Id && g.PermissionId == action.Id, ct);
            if (!alreadyGranted)
            {
                db.RoleGrants.Add(new RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    RoleId = role.Id,
                    PermissionId = action.Id,
                    CreatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Observabilidad ICT (Feature #10888, §A.11) — módulo <c>ict-logs</c> + permiso <c>ict.logs.read</c>
    /// que protege <c>GET /api/v1/ict/logs</c> (submódulo frontend de logs/alertas ICT). Mismo patrón e
    /// idempotencia que <see cref="SeedLogQxPermissionsAsync"/>: crea módulo y permiso si faltan y concede
    /// el permiso a SuperAdmin (que además bypassa por rol). Sin este seed, ningún usuario no-superadmin
    /// podía recibir el permiso por el flujo RBAC estándar.
    /// </summary>
    private static async Task SeedIctLogsPermissionsAsync(FlitDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var module = await db.SecurityModules
            .FirstOrDefaultAsync(m => m.Code == "ict-logs" && m.DeletedAt == null, ct);
        if (module is null)
        {
            module = new SecurityModule
            {
                Id = Guid.CreateVersion7(),
                Code = "ict-logs",
                Name = "Logs ICT",
                SortOrder = 9,
                IsActive = true,
                CreatedAt = now,
            };
            db.SecurityModules.Add(module);
            await db.SaveChangesAsync(ct);
        }

        var action = await db.RbacActions
            .FirstOrDefaultAsync(a => a.Slug == "ict.logs.read", ct);
        if (action is null)
        {
            action = new RbacAction
            {
                Id = Guid.CreateVersion7(),
                ModuleId = module.Id,
                Slug = "ict.logs.read",
                Name = "Ver logs ICT",
                HttpMethod = "GET",
                RoutePattern = "/api/v1/ict/logs",
                IsActive = true,
                CreatedAt = now,
            };
            db.RbacActions.Add(action);
            await db.SaveChangesAsync(ct);
        }

        // Grant a SuperAdmin (idempotente): solo si aún no lo tiene.
        var superAdminRoles = await db.Roles
            .Where(r => r.Code == "SuperAdmin")
            .ToListAsync(ct);
        foreach (var role in superAdminRoles)
        {
            var alreadyGranted = await db.RoleGrants
                .AnyAsync(g => g.RoleId == role.Id && g.PermissionId == action.Id, ct);
            if (!alreadyGranted)
            {
                db.RoleGrants.Add(new RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    RoleId = role.Id,
                    PermissionId = action.Id,
                    CreatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Administración de clientes ICT (Feature #10888, ronda 2) — módulo <c>ict-clients</c> + permiso
    /// <c>ict.clients.manage</c> que protege el CRUD de <c>ict.integration_clients</c>
    /// (<c>/api/v1/ict/clients</c>, submódulo "Clientes ICT" dentro de Usuarios y Roles). Mismo patrón e
    /// idempotencia que <see cref="SeedIctLogsPermissionsAsync"/>: crea módulo y permiso si faltan y concede
    /// el permiso a SuperAdmin (que además bypassa por rol).
    /// </summary>
    private static async Task SeedIctClientsPermissionsAsync(FlitDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var module = await db.SecurityModules
            .FirstOrDefaultAsync(m => m.Code == "ict-clients" && m.DeletedAt == null, ct);
        if (module is null)
        {
            module = new SecurityModule
            {
                Id = Guid.CreateVersion7(),
                Code = "ict-clients",
                Name = "Clientes ICT",
                SortOrder = 10,
                IsActive = true,
                CreatedAt = now,
            };
            db.SecurityModules.Add(module);
            await db.SaveChangesAsync(ct);
        }

        var action = await db.RbacActions
            .FirstOrDefaultAsync(a => a.Slug == "ict.clients.manage", ct);
        if (action is null)
        {
            action = new RbacAction
            {
                Id = Guid.CreateVersion7(),
                ModuleId = module.Id,
                Slug = "ict.clients.manage",
                Name = "Administrar clientes ICT",
                HttpMethod = "POST",
                RoutePattern = "/api/v1/ict/clients",
                IsActive = true,
                CreatedAt = now,
            };
            db.RbacActions.Add(action);
            await db.SaveChangesAsync(ct);
        }

        // Grant a SuperAdmin (idempotente): solo si aún no lo tiene.
        var superAdminRoles = await db.Roles
            .Where(r => r.Code == "SuperAdmin")
            .ToListAsync(ct);
        foreach (var role in superAdminRoles)
        {
            var alreadyGranted = await db.RoleGrants
                .AnyAsync(g => g.RoleId == role.Id && g.PermissionId == action.Id, ct);
            if (!alreadyGranted)
            {
                db.RoleGrants.Add(new RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    RoleId = role.Id,
                    PermissionId = action.Id,
                    CreatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Reset administrativo de contraseña (HU #10170 + AdminCompany auth-parity): permiso
    /// <c>security.users.reset_password</c> en el módulo <c>usuarios</c>, concedido a
    /// AdminCompany (mismo tenant en runtime) y SuperAdmin (catálogo / bypass por rol).
    /// Idempotente; separado de <see cref="SeedBaseModulesAsync"/> porque ese método hace
    /// early-return si el módulo dashboard ya existe.
    /// </summary>
    private static async Task SeedResetPasswordPermissionsAsync(FlitDbContext db, CancellationToken ct)
    {
        const string slug = "security.users.reset_password";
        var now = DateTimeOffset.UtcNow;

        var module = await db.SecurityModules
            .FirstOrDefaultAsync(m => m.Code == "usuarios" && m.DeletedAt == null, ct);
        if (module is null)
            return;

        var action = await db.RbacActions.FirstOrDefaultAsync(a => a.Slug == slug, ct);
        if (action is null)
        {
            action = new RbacAction
            {
                Id = Guid.CreateVersion7(),
                ModuleId = module.Id,
                Slug = slug,
                Name = "Restablecer contraseña de usuarios del tenant",
                HttpMethod = "POST",
                RoutePattern = "/api/v1/auth/admin/reset-password",
                IsActive = true,
                CreatedAt = now,
            };
            db.RbacActions.Add(action);
            await db.SaveChangesAsync(ct);
        }

        foreach (var roleCode in new[] { "SuperAdmin", "AdminCompany" })
        {
            var roles = await db.Roles.Where(r => r.Code == roleCode).ToListAsync(ct);
            foreach (var role in roles)
            {
                var alreadyGranted = await db.RoleGrants
                    .AnyAsync(g => g.RoleId == role.Id && g.PermissionId == action.Id, ct);
                if (!alreadyGranted)
                {
                    db.RoleGrants.Add(new RoleGrant
                    {
                        Id = Guid.CreateVersion7(),
                        RoleId = role.Id,
                        PermissionId = action.Id,
                        CreatedAt = now,
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

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
