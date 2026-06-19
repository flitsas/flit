using Flit.Modules.Security.Application.Auth.AdminResetPassword;
using Flit.Modules.Security.Application.Auth.ChangePassword;
using Flit.Modules.Security.Application.Auth.ForgotPassword;
using Flit.Modules.Security.Application.Auth.Login;
using Flit.Modules.Security.Application.Auth.RememberUsername;
using Flit.Modules.Security.Application.Auth.ResetPassword;
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
        services.AddScoped<RememberUsernameHandler>();
        return services;
    }
}
