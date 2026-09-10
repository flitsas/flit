using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Flit.Ict.Grpc.Contracts;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Infrastructure.Ict;

/// <summary>
/// Plano C (plan ICT §A.3/§A.9): refleja hacia core-ict los cambios de estado de un trámite
/// originado en ICT (<c>procedure_instances.origin='ict'</c>). core-api es el CLIENTE del gRPC
/// <c>IctStateCallback</c>; el disparo lo hace el <c>ProcedureStateChangeOutboxProcessor</c> vía
/// <see cref="IProcedureStateChangeNotifier"/>, reutilizando el outbox transaccional existente
/// (semántica at-least-once). Se registra un notifier COMPUESTO
/// (<see cref="CompositeProcedureStateChangeNotifier"/>) que hace fan-out a los webhooks OT y a ICT,
/// para no romper el canal OT (HU #10216).
/// </summary>
public static class IctStateReflectionExtensions
{
    /// <summary>
    /// Cablea el reflejo de estado hacia core-ict si hay endpoint configurado
    /// (<c>Ict:StateCallback:Address</c>) y SIEMPRE registra el fan-out de
    /// <see cref="IProcedureStateChangeNotifier"/> vía
    /// <see cref="Messaging.ProcedureStateChangeNotifierRegistration"/> (HU #11464).
    /// Debe llamarse DESPUÉS de registrar <c>OtWebhookProcedureStateChangeNotifier</c>
    /// (<c>AddAdminInfrastructure</c>).
    /// </summary>
    public static IServiceCollection AddIctStateReflection(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var address = configuration["Ict:StateCallback:Address"];
        var includeIct = !string.IsNullOrWhiteSpace(address);

        if (includeIct)
        {
            // Secreto HMAC compartido con core-ict. Reutiliza el del canal directo (Ict:ServiceToken:Secret)
            // salvo override explícito. Sin secreto, el provider emite null y core-ict rechaza (fail-closed).
            var secret = configuration["Ict:StateCallback:Secret"] ?? configuration["Ict:ServiceToken:Secret"];
            services.Configure<IctStateCallbackOptions>(opts =>
            {
                opts.Address = address!;
                opts.Secret = secret ?? string.Empty;
                var issuer = configuration["Ict:StateCallback:Issuer"];
                if (!string.IsNullOrWhiteSpace(issuer))
                    opts.Issuer = issuer!;
                var audience = configuration["Ict:StateCallback:Audience"];
                if (!string.IsNullOrWhiteSpace(audience))
                    opts.Audience = audience!;
            });

            services.AddSingleton<IctStateCallbackTokenProvider>();
            services.AddSingleton<IctStateCallbackClientInterceptor>();

            services.AddGrpcClient<IctStateCallback.IctStateCallbackClient>(options => options.Address = new Uri(address!))
                .AddInterceptor<IctStateCallbackClientInterceptor>();

            services.AddScoped<IctProcedureStateChangeNotifier>();
        }

        // Único registro de IProcedureStateChangeNotifier (HU #11464).
        services.AddProcedureStateChangeNotifierFanOut(includeIctReflection: includeIct);

        return services;
    }
}

/// <summary>Config del canal inverso core-api → core-ict (service-token HMAC, aud=core-ict-internal).</summary>
public sealed class IctStateCallbackOptions
{
    /// <summary>URL del gRPC de callback expuesto por core-ict (p. ej. http://core-ict:4020).</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Secreto compartido HMAC (mismo que el canal directo). Vacío ⇒ fail-closed.</summary>
    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "flit-api-svc";

    /// <summary>Audiencia dedicada del canal inverso; core-ict valida exactamente este valor.</summary>
    public string Audience { get; set; } = "core-ict-internal";

    public string Scope { get; set; } = "ict.state";

    public int TtlMinutes { get; set; } = 10;
}

/// <summary>
/// Emite (y cachea) el service-token HMAC con el que core-api se autentica ante el callback de core-ict.
/// Generado localmente (sin I/O), renovado ~1 min antes de expirar. Sin secreto configurado devuelve null
/// (no se adjunta token; core-ict rechaza — fail-closed). Espeja <c>IctServiceTokenProvider</c> de core-ict
/// pero en dirección opuesta (aud=core-ict-internal, scope=ict.state).
/// TODO(ICT-GRPC-AUTH-RSA): migrar ambos service-tokens a RS256 (llave asimétrica).
/// </summary>
public sealed class IctStateCallbackTokenProvider(IOptions<IctStateCallbackOptions> options)
{
    private readonly IctStateCallbackOptions _opts = options.Value;
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
                claims: [new Claim(JwtRegisteredClaimNames.Sub, "core-api"), new Claim("scope", _opts.Scope)],
                notBefore: DateTime.UtcNow,
                expires: expires,
                signingCredentials: credentials);

            _cached = new JwtSecurityTokenHandler().WriteToken(token);
            _expiresUtc = expires;
            return _cached;
        }
    }
}

/// <summary>Interceptor de cliente gRPC que adjunta <c>Authorization: Bearer &lt;service-token&gt;</c> al callback.</summary>
public sealed class IctStateCallbackClientInterceptor(IctStateCallbackTokenProvider provider) : Interceptor
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

/// <summary>
/// Sink de <see cref="IProcedureStateChangeNotifier"/> que empuja el cambio de estado a core-ict SOLO si el
/// trámite se originó en ICT (<c>origin='ict'</c> con <c>external_ref</c>). Resuelve origin/external_ref con
/// aislamiento RLS (GUC <c>app.current_tenant_id</c>) y hace un push unario gRPC; los fallos se propagan para
/// que el outbox reintente (at-least-once). Trámites no-ICT ⇒ no-op silencioso.
/// </summary>
public sealed partial class IctProcedureStateChangeNotifier(
    IctStateCallback.IctStateCallbackClient client,
    FlitDbContext db,
    ILogger<IctProcedureStateChangeNotifier> logger) : IProcedureStateChangeNotifier
{
    private const string IctOrigin = "ict";

    public async Task NotifyAsync(ProcedureStateChangeEvent change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        var (isIct, externalRef) = await ResolveIctRefAsync(change.TenantId, change.ProcedureInstanceId, cancellationToken);
        if (!isIct || string.IsNullOrEmpty(externalRef))
        {
            return; // trámite no-ICT (o sin external_ref): no se refleja a core-ict.
        }

        var notification = new StateChangeNotification
        {
            TenantId = change.TenantId.ToString(),
            ProcedureInstanceId = change.ProcedureInstanceId.ToString(),
            ExternalRef = externalRef,
            FromStatus = change.FromStatus ?? string.Empty,
            ToStatus = change.ToStatus,
            OccurredAt = Timestamp.FromDateTimeOffset(change.ChangedAt.ToUniversalTime()),
            Reason = change.Reason ?? string.Empty,
        };

        await client.NotifyProcedureStateChangedAsync(notification, cancellationToken: cancellationToken);
        Log.Reflected(logger, externalRef, change.FromStatus, change.ToStatus);
    }

    /// <summary>
    /// Lee <c>origin</c>/<c>external_ref</c> de <c>tramites.procedure_instances</c> con el GUC de RLS fijado
    /// al tenant del evento (para que el rol de servicio con RLS no lo filtre a cero). En InMemory (tests)
    /// consulta por EF sin RLS.
    /// </summary>
    private async Task<(bool IsIct, string? ExternalRef)> ResolveIctRefAsync(
        Guid tenantId, Guid instanceId, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            var row = await db.ProcedureInstances.AsNoTracking()
                .Where(p => p.Id == instanceId)
                .Select(p => new { p.Origin, p.ExternalRef })
                .FirstOrDefaultAsync(ct);
            return row is null
                ? (false, null)
                : (string.Equals(row.Origin, IctOrigin, StringComparison.OrdinalIgnoreCase), row.ExternalRef);
        }

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var tx = await connection.BeginTransactionAsync(ct);

            await using (var setGuc = connection.CreateCommand())
            {
                setGuc.Transaction = tx;
                setGuc.CommandText = "SELECT set_config('app.current_tenant_id', @tenant, true)";
                var pTenant = setGuc.CreateParameter();
                pTenant.ParameterName = "tenant";
                pTenant.Value = tenantId.ToString();
                setGuc.Parameters.Add(pTenant);
                await setGuc.ExecuteNonQueryAsync(ct);
            }

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT origin, external_ref FROM tramites.procedure_instances WHERE id = @id";
            var pId = cmd.CreateParameter();
            pId.ParameterName = "id";
            pId.Value = instanceId;
            cmd.Parameters.Add(pId);

            string? origin = null;
            string? externalRef = null;
            var found = false;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    found = true;
                    origin = await reader.IsDBNullAsync(0, ct) ? null : reader.GetString(0);
                    externalRef = await reader.IsDBNullAsync(1, ct) ? null : reader.GetString(1);
                }
            }

            await tx.CommitAsync(ct);
            return found
                ? (string.Equals(origin, IctOrigin, StringComparison.OrdinalIgnoreCase), externalRef)
                : (false, null);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "Reflejo ICT: ext_ref={ExternalRef} {From} → {To} empujado a core-ict.")]
        public static partial void Reflected(ILogger logger, string externalRef, string? from, string to);
    }
}

/// <summary>
/// Notifier compuesto: hace fan-out del cambio de estado a varios sinks (webhooks OT + reflejo ICT),
/// aislando la ejecución de cada uno. Si alguno falla, agrega los errores y relanza para que el outbox
/// reintente (at-least-once); los sinks que sí funcionaron se re-ejecutan en el reintento, por lo que
/// deben ser idempotentes (misma garantía que ya tenía el canal OT).
/// </summary>
public sealed partial class CompositeProcedureStateChangeNotifier(
    IReadOnlyList<IProcedureStateChangeNotifier> sinks,
    ILogger<CompositeProcedureStateChangeNotifier> logger) : IProcedureStateChangeNotifier
{
    public async Task NotifyAsync(ProcedureStateChangeEvent change, CancellationToken cancellationToken = default)
    {
        List<Exception>? failures = null;
        foreach (var sink in sinks)
        {
            try
            {
                await sink.NotifyAsync(change, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (failures ??= []).Add(ex);
                Log.SinkFailed(logger, sink.GetType().Name, ex);
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException(
                "Uno o más sinks de cambio de estado fallaron; el outbox reintentará.", failures);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error,
            Message = "Sink de cambio de estado {Sink} falló; se reintentará vía outbox.")]
        public static partial void SinkFailed(ILogger logger, string sink, Exception ex);
    }
}
