using Flit.Modules.Security.Application.Auth.ActivateAccount;
using Flit.Modules.Security.Application.Auth.AdminResetPassword;
using Flit.Modules.Security.Application.Auth.ChangePassword;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Auth.ForgotPassword;
using Flit.Modules.Security.Application.Auth.Login;
using Flit.Modules.Security.Application.Auth.ResetPassword;
using Flit.Modules.Security.Application.Modules;
using Flit.Modules.Security.Application.Permissions;
using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Application.UserManagement.SuspendUser;
using Flit.Modules.Security.Application.UserManagement.UnsuspendUser;
using Flit.Modules.Security.Application.UserRoles;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Modules.Security.Application;

public static class SecurityApplicationExtensions
{
    public static IServiceCollection AddSecurityApplication(this IServiceCollection services)
    {
        services.AddScoped<LoginHandler>();
        services.AddScoped<ForgotPasswordHandler>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<AdminResetPasswordHandler>();
        services.AddScoped<ChangePasswordHandler>();
        services.AddScoped<CreateInvitationHandler>();
        services.AddScoped<ActivateAccountHandler>();

        // HU #10161 — CRUD módulos dinámicos Super Admin
        services.AddScoped<CreateModuleHandler>();
        services.AddScoped<UpdateModuleHandler>();
        services.AddScoped<DeactivateModuleHandler>();
        services.AddScoped<DeleteModuleHandler>();
        services.AddScoped<ListModulesHandler>();

        // HU #10162 — CRUD permisos granulares Super Admin
        services.AddScoped<CreatePermissionHandler>();
        services.AddScoped<DeactivatePermissionHandler>();
        services.AddScoped<DeletePermissionHandler>();
        services.AddScoped<ListPermissionsHandler>();

        // HU #10163 — CRUD roles y asociación de permisos Super Admin
        // HU #10505 — catálogo global de roles por tipo de entidad
        services.AddScoped<CreateRoleHandler>();
        services.AddScoped<DeleteRoleHandler>();
        services.AddScoped<SetRolePermissionsHandler>();
        services.AddScoped<SetRoleActiveHandler>();
        services.AddScoped<ListRolesHandler>();
        services.AddScoped<GetRoleHandler>();

        // HU #10164 — Asignación única de rol por usuario tenant
        // HU #10506 — soporte multi-rol por usuario (modelo aditivo) + quitar rol puntual
        services.AddScoped<AssignRoleHandler>();
        services.AddScoped<RemoveRoleAssignmentHandler>();

        // Fase 2 — Endpoints AdminCompañía
        services.AddScoped<ListAccessibleModulesHandler>();

        // HU #10619 — unificación de suspensión/desactivación indefinida + fix de alcance SuperAdmin
        services.AddScoped<SuspendUserHandler>();
        services.AddScoped<UnsuspendUserHandler>();

        return services;
    }
}
