using Flit.Modules.Security.Application.Auth.ActivateAccount;
using Flit.Modules.Security.Application.Auth.AdminResetPassword;
using Flit.Modules.Security.Application.Auth.ChangePassword;
using Flit.Modules.Security.Application.Auth.ForgotPassword;
using Flit.Modules.Security.Application.Auth.Login;
using Flit.Modules.Security.Application.Auth.ResetPassword;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Modules.Security.Application;

/// <summary>
/// Epic #13217 (HU #13232) — los casos de uso del login y la recuperación de cuenta. Salieron de
/// <c>SecurityApplicationExtensions.AddSecurityApplication</c>; core-api los registra durante la transición (sus rutas
/// siguen siendo el respaldo del gateway) y core-identity siempre.
/// </summary>
public static class IdentityAuthApplicationExtensions
{
    public static IServiceCollection AddIdentityAuthApplication(this IServiceCollection services)
    {
        services.AddScoped<LoginHandler>();
        services.AddScoped<CredentialVerifier>(); // HU #12990 (A-05): login del hub
        services.AddScoped<ForgotPasswordHandler>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<AdminResetPasswordHandler>();
        services.AddScoped<ChangePasswordHandler>();
        services.AddScoped<ActivateAccountHandler>();
        return services;
    }
}
