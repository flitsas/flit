using System.Threading.RateLimiting;
using Flit.Modules.Security.Application.Auth.BulkInvitation;

namespace Flit.Api.RateLimiting;

/// <summary>
/// HU #14424 AC4 (Feature #14420, ADR-0077) — límite de tasa del <c>dryRun</c> de la invitación masiva, POR USUARIO.
/// El dryRun revela si un correo ya tiene cuenta (<c>EMAIL_ALREADY_IN_USE</c>) sin crear nada; repetirlo permitiría enumerar
/// correos, así que se acota a <see cref="BulkInvitationOptions.DryRunRequestsPerUserPerMinute"/> por minuto.
/// <para>
/// No usa <c>AddRateLimiter</c> porque ese middleware corre ANTES de autenticar (no conoce al usuario) y partiría por IP,
/// que castigaría a toda una oficina tras un NAT. Aquí el filtro ya tiene el <c>sub</c> del JWT. El contador es por
/// instancia (en memoria): suficiente como freno de enumeración; no es una cuota de facturación.
/// </para>
/// </summary>
public sealed class BulkInvitationDryRunLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public BulkInvitationDryRunLimiter(BulkInvitationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var permitLimit = Math.Max(1, options.DryRunRequestsPerUserPerMinute);
        _limiter = PartitionedRateLimiter.Create<string, string>(userKey =>
            RateLimitPartition.GetFixedWindowLimiter(
                userKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    }

    /// <summary>Segundos que sugiere <c>Retry-After</c> al rechazar (la ventana es de un minuto).</summary>
    public static int RetryAfterSeconds => 60;

    /// <summary>Intenta consumir un permiso del usuario; <c>false</c> = excedió el límite (responder 429).</summary>
    public bool TryAcquire(Guid userId)
    {
        using var lease = _limiter.AttemptAcquire(userId.ToString("N"));
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}

public static class BulkInvitationDryRunLimiterExtensions
{
    public static IServiceCollection AddBulkInvitationDryRunLimiter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton<BulkInvitationDryRunLimiter>();
    }
}
