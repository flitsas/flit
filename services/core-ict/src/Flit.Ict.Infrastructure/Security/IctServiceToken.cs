using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Ict.Infrastructure.Security;

/// <summary>
/// Config del SERVICE-TOKEN gRPC este-oeste (core-ict → core-api). Es un JWT de SISTEMA (client-credentials),
/// distinto del token del cliente ICT y del token de plataforma: autentica que la llamada gRPC la hace
/// core-ict, no un tercero cualquiera que alcance el puerto interno. Secreto compartido (HMAC) con core-api.
/// En prod el secreto llega por variable de entorno (Ict__ServiceToken__Secret); en dev, appsettings.
/// TODO(ICT-GRPC-AUTH-RSA): migrar a RS256 (llave asimétrica) si se requiere que core-api no pueda emitir.
/// </summary>
public sealed class IctServiceTokenOptions
{
    public const string SectionName = "Ict:ServiceToken";

    public string Secret { get; init; } = string.Empty;

    public string Issuer { get; init; } = "flit-ict-svc";

    public string Audience { get; init; } = "flit-internal";

    public string Scope { get; init; } = "ict.orchestration";

    public int TtlMinutes { get; init; } = 10;
}

/// <summary>
/// Emite (y cachea) el service-token HMAC con el que core-ict se autentica ante el gRPC de core-api.
/// El token se genera localmente (sin I/O) y se renueva ~1 min antes de expirar. Si no hay secreto
/// configurado devuelve null: no se adjunta token y core-api lo rechaza (fail-closed).
/// </summary>
public sealed class IctServiceTokenProvider(IOptions<IctServiceTokenOptions> options)
{
    private readonly IctServiceTokenOptions _opts = options.Value;
    private readonly object _lock = new();
    private string? _cached;
    private DateTime _expiresUtc = DateTime.MinValue;

    public string? GetToken()
    {
        if (string.IsNullOrWhiteSpace(_opts.Secret))
        {
            return null;
        }

        lock (_lock)
        {
            if (_cached is not null && DateTime.UtcNow < _expiresUtc.AddSeconds(-60))
            {
                return _cached;
            }

            var expires = DateTime.UtcNow.AddMinutes(_opts.TtlMinutes <= 0 ? 10 : _opts.TtlMinutes);
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: _opts.Issuer,
                audience: _opts.Audience,
                claims: [new Claim(JwtRegisteredClaimNames.Sub, "core-ict"), new Claim("scope", _opts.Scope)],
                notBefore: DateTime.UtcNow,
                expires: expires,
                signingCredentials: credentials);

            _cached = new JwtSecurityTokenHandler().WriteToken(token);
            _expiresUtc = expires;
            return _cached;
        }
    }
}

/// <summary>
/// Interceptor de cliente gRPC que adjunta <c>Authorization: Bearer &lt;service-token&gt;</c> a cada llamada
/// hacia core-api. Todas las RPC de ICT son unarias, así que solo se intercepta <c>AsyncUnaryCall</c>.
/// </summary>
public sealed class IctServiceTokenClientInterceptor(IctServiceTokenProvider provider) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation) =>
        continuation(request, WithToken(context));

    private ClientInterceptorContext<TRequest, TResponse> WithToken<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        var token = provider.GetToken();
        if (string.IsNullOrEmpty(token))
        {
            return context;
        }

        var headers = context.Options.Headers ?? [];
        headers.Add("Authorization", "Bearer " + token);
        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host, context.Options.WithHeaders(headers));
    }
}
