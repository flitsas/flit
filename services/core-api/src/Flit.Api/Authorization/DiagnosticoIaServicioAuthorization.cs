using System.Collections.Frozen;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Quién puede pedir la configuración de IA con la llave sellada (ADR-0091, SP-15): únicamente <c>svc-diagnostico</c>, con
/// <c>aud=tramites</c> y el scope dedicado <see cref="Scope"/> (mismo patrón que
/// <see cref="DiagnosticoUsuariosServicioAuthorization"/>). Segunda barrera dentro del método: <see cref="ClienteDe"/>, mapa en
/// código (no en configuración) de las herramientas que cada cliente puede pedir y de su audiencia en el AAD. Ningún cliente
/// obtiene <c>ocr</c> ni <c>dr_flit</c>.
/// </summary>
internal static class DiagnosticoIaServicioAuthorization
{
    public const string Policy = "DiagnosticoIaServicio";

    public const string Scope = "platform.ia.diagnostico";

    private static readonly FrozenDictionary<string, ClienteIa> Clientes = new Dictionary<string, ClienteIa>(StringComparer.Ordinal)
    {
        [DiagnosticoUsuariosServicioAuthorization.ClientId] = new ClienteIa("diagnostico", new HashSet<string>(StringComparer.Ordinal) { "diagnostico_dictamen" }.ToFrozenSet()),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static void Configure(AuthorizationPolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.AddAuthenticationSchemes(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Identities.Any(EsTokenAutorizado));
    }

    /// <summary><c>sub == svc-diagnostico</c>, <c>aud=tramites</c> y el scope, a la vez.</summary>
    internal static bool EsTokenAutorizado(ClaimsIdentity identity) =>
        DiagnosticoUsuariosServicioAuthorization.EsTokenDeDiagnostico(identity, Scope);

    /// <summary>El cliente con sus herramientas, o <c>null</c> si no tiene ninguna.</summary>
    internal static ClienteIa? ClienteDe(string? clientId) =>
        clientId is not null && Clientes.TryGetValue(clientId, out var cliente) ? cliente : null;
}

/// <summary>Audiencia del AAD (fija por cliente) y herramientas que el cliente puede pedir.</summary>
internal sealed record ClienteIa(string Audiencia, IReadOnlySet<string> Herramientas);
