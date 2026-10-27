using Flit.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Api.Endpoints.Internal;

/// <summary>
/// JWKS con la llave PÚBLICA con que core-api firma el token de usuario del modo vigente (<c>RsaJwtTokenIssuer</c>,
/// <c>kid</c> = <c>Jwt:SigningKeyId</c>, p. ej. <c>flit-api-v1</c>). Lo consumen los servicios que validan ese token sin
/// tener la base de Identidad (core-ict). Solo parámetros públicos (n, e); anónimo, como cualquier JWKS. YARP no
/// publica <c>/api/v1/internal/*</c>: se consulta por la red interna (<c>http://core-api:&lt;puerto&gt;</c>).
/// </summary>
public static class InternalJwksEndpoints
{
    public const string Route = "/api/v1/internal/jwks.json";

    public static IEndpointRouteBuilder MapInternalJwksEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet(Route, GetJwks)
            .WithName("InternalJwks")
            .WithTags("Internal")
            .Produces(StatusCodes.Status200OK)
            .AllowAnonymous();
        return app;
    }

    public static IResult GetJwks(JwtKeyMaterial keyMaterial, IOptions<JwtSettings> settings) =>
        Results.Json(BuildJwks(keyMaterial, settings.Value.SigningKeyId));

    public static object BuildJwks(JwtKeyMaterial keyMaterial, string keyId)
    {
        var parameters = keyMaterial.SigningKey.Rsa?.ExportParameters(includePrivateParameters: false)
            ?? keyMaterial.SigningKey.Parameters;
        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = SecurityAlgorithms.RsaSha256,
                    kid = keyId,
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent),
                },
            },
        };
    }
}
