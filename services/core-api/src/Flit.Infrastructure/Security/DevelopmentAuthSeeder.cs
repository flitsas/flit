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

    /// <summary>Usuario ot_admin fijo — alineado con seed SQL y FK changed_by en trámites.</summary>
    public static readonly Guid OtAdminUserId =
        Guid.Parse("ec4dddb9-ade5-43e8-b33b-c6036eba49d0");

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

        await SeedSuperAdminAsync(db, passwordHasher, cancellationToken);
        await SeedAdminCompanyUserAsync(db, passwordHasher, cancellationToken);
        await EnsureDevOperacionCredentialsAsync(db, passwordHasher, cancellationToken);
        await SeedBaseModulesAsync(db, cancellationToken);
        await SeedTenantModuleGrantsAsync(db, cancellationToken);
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

        // 1. Rol "Radicador" del tenant (idempotente).
        var role = await db.Roles.FirstOrDefaultAsync(
            r => r.TenantId == empresaTenant.Id && r.Code == "Radicador" && r.DeletedAt == null,
            cancellationToken);
        if (role is null)
        {
            role = new Role
            {
                Id = Guid.CreateVersion7(),
                TenantId = empresaTenant.Id,
                Code = "Radicador",
                Name = "Radicador",
                IsSystem = false,
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
                TenantId = empresaTenant.Id,
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

        // 4. Asignación de rol (respeta la constraint UNIQUE(user_id, tenant_id): reusa/realinea).
        var existing = await db.UserRoleAssignments
            .FirstOrDefaultAsync(a => a.UserId == user.Id && a.TenantId == empresaTenant.Id, cancellationToken);
        if (existing is null)
        {
            var now = DateTimeOffset.UtcNow;
            db.UserRoleAssignments.Add(new UserRoleAssignment
            {
                Id = Guid.CreateVersion7(),
                TenantId = empresaTenant.Id,
                UserId = user.Id,
                RoleId = role.Id,
                AssignedAt = now,
                CreatedAt = now,
                RowVersion = 0,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (existing.DeletedAt is not null || existing.RoleId != role.Id)
        {
            existing.DeletedAt = null;
            existing.DeletedBy = null;
            existing.RoleId = role.Id;
            existing.AssignedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
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

        db.Roles.Add(new Role
        {
            Id = roleId,
            TenantId = tenantId,
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

        var role = await db.Roles
            .FirstOrDefaultAsync(
                r => r.TenantId == otTenantId && r.Code == "ot_admin",
                cancellationToken);

        if (role is null)
        {
            var roleId = Guid.CreateVersion7();
            var now = DateTimeOffset.UtcNow;
            role = new Role
            {
                Id = roleId,
                TenantId = otTenantId,
                Code = "ot_admin",
                Name = "Administrador OT",
                IsSystem = true,
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
        var roleId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        db.Roles.Add(new Role
        {
            Id = roleId,
            TenantId = empresaTenant.Id,
            Code = "AdminCompany",
            Name = "Administrador de Compañía",
            IsSystem = true,
            CreatedAt = now,
            RowVersion = 0,
        });

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
            RoleId = roleId,
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

        var adminCompanyRole = await db.Roles
            .FirstOrDefaultAsync(r => r.TenantId == empresaTenant.Id && r.Code == "AdminCompany" && r.DeletedAt == null, ct);
        if (adminCompanyRole is null) return;

        // La constraint uq_user_role_assignments_user_id_tenant_id es UNIQUE(user_id, tenant_id)
        // SIN filtrar por deleted_at: solo puede existir UNA fila por (usuario, tenant). Por eso hay
        // que buscar también las soft-deleted; si filtramos por DeletedAt == null, una fila borrada
        // lógicamente queda invisible y el INSERT choca con la constraint (23505) en cada arranque.
        var existing = await db.UserRoleAssignments
            .FirstOrDefaultAsync(a => a.UserId == userId && a.TenantId == empresaTenant.Id, ct);

        if (existing is not null)
        {
            // Reactiva/realinea la fila existente en lugar de insertar (idempotente).
            if (existing.DeletedAt is not null || existing.RoleId != adminCompanyRole.Id)
            {
                existing.DeletedAt = null;
                existing.DeletedBy = null;
                existing.RoleId = adminCompanyRole.Id;
                existing.AssignedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return;
        }

        var now = DateTimeOffset.UtcNow;
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
        await db.SaveChangesAsync(ct);
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
                    TenantId = superAdminRole.TenantId,
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
                    TenantId = adminCompanyRole.TenantId,
                    RoleId = adminCompanyRole.Id,
                    PermissionId = a.Id,
                    CreatedAt = now,
                }));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedTenantModuleGrantsAsync(FlitDbContext db, CancellationToken ct)
    {
        var empresaTenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Code == DemoEmpresaTenantCode, ct);
        if (empresaTenant is null) return;

        // Módulos que EMPRESA_DEMO tiene habilitados por defecto (omitimos rbac intencionalmente)
        var grantedCodes = new[] { "tramites", "usuarios", "dashboard", "reportes", "validaciones" };

        var modules = await db.SecurityModules
            .Where(m => grantedCodes.Contains(m.Code) && m.DeletedAt == null)
            .ToListAsync(ct);

        var existingModuleIds = await db.TenantModuleGrants
            .Where(g => g.TenantId == empresaTenant.Id)
            .Select(g => g.ModuleId)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var m in modules.Where(m => !existingModuleIds.Contains(m.Id)))
        {
            db.TenantModuleGrants.Add(new Flit.Infrastructure.Persistence.Entities.Security.TenantModuleGrant
            {
                TenantId = empresaTenant.Id,
                ModuleId = m.Id,
                GrantedAt = now,
            });
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
