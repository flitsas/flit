using System.Security.Claims;
using Flit.Admin.Application.Auditing;
using Microsoft.AspNetCore.Http;

namespace Flit.Infrastructure.Auditing;

/// <summary>
/// Implementación de <see cref="IAuditContextAccessor"/> sobre <see cref="IHttpContextAccessor"/>
/// (RNF01, ADR-0024). Resuelve la IP con prioridad <c>X-Forwarded-For</c> (primer hop, porque
/// hay un gateway <c>Flit.Gateway</c> delante) → <c>Connection.RemoteIpAddress</c>. Fuera de una
/// petición HTTP (procesos en background, tests que no montan el pipeline) devuelve <c>null</c>.
/// </summary>
internal sealed class HttpAuditContextAccessor : IAuditContextAccessor
{
    private const string ForwardedForHeader = "X-Forwarded-For";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpAuditContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public string? ClientIp
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context is null)
            {
                return null;
            }

            // Primer hop de X-Forwarded-For: el cliente real detrás del gateway.
            if (context.Request.Headers.TryGetValue(ForwardedForHeader, out var forwarded))
            {
                var firstHop = forwarded.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (firstHop.Length > 0)
                {
                    var candidate = firstHop[0].Trim();
                    if (candidate.Length > 0)
                    {
                        return candidate;
                    }
                }
            }

            return context.Connection.RemoteIpAddress?.ToString();
        }
    }

    public Guid? UserId
    {
        get
        {
            var sub = _httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
            return Guid.TryParse(sub, out var userId) ? userId : null;
        }
    }
}
