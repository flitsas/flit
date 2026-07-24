using System.Text.Json.Serialization;
using Flit.Ict.Api.Authorization;
using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Api.Endpoints;

/// <summary>
/// Secretarías de tránsito activas (servicio v1 #9). Se conserva el path v1
/// <c>GET /api/v1/secretaries</c> y la forma de respuesta <c>{ data: [ { Code, Name } ] }</c>
/// (claves PascalCase EXACTAS) para no romper clientes. Alias de desarrollo <c>/api/ict/secretaries</c>.
/// </summary>
public static class IctSecretariesEndpoints
{
    /// <summary>Ítem v1: claves PascalCase EXACTAS (Code, Name).</summary>
    private sealed record SecretaryV1Dto(
        [property: JsonPropertyName("Code")] string Code,
        [property: JsonPropertyName("Name")] string Name);

    private static readonly string[] Paths = ["/api/v1/secretaries", "/api/ict/secretaries"];

    public static IEndpointRouteBuilder MapIctSecretariesEndpoints(this IEndpointRouteBuilder app)
    {
        foreach (var path in Paths)
        {
            app.MapGet(path, GetSecretariesAsync).RequireAuthorization(IctSecurityExtensions.IctClientPolicy);
        }

        return app;
    }

    private static async Task<IResult> GetSecretariesAsync(ISecretariesV1Query query, CancellationToken ct)
    {
        var items = await query.GetActiveAsync(ct);
        var data = items.Select(s => new SecretaryV1Dto(s.Code, s.Name)).ToList();
        return Results.Ok(new { data });
    }
}
