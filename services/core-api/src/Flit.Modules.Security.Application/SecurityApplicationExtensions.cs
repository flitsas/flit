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
        services.AddScoped<CreateRoleHandler>();
        services.AddScoped<DeleteRoleHandler>();
        services.AddScoped<SetRolePermissionsHandler>();
        services.AddScoped<ListRolesHandler>();

        return services;
    }
}
