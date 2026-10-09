using System.Diagnostics.Metrics;
using Flit.Consultas.Grpc.Mapping;
using Flit.Consultas.Grpc.V1;
using Flit.Infrastructure.Consultations;
using Flit.Tramites.Application.UseCases.Consultations;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Flit.Api.Consultas;

/// <summary>
/// Trámites consulta proveedores a través de core-consultas (Epic #13316, HU #13346/#13348; ADR-0065). Implementa el
/// registro de proveedores y la cadena de respaldo que ya usan todos los handlers: ninguno cambia. core-api no tiene
/// proveedores: si core-consultas no responde, devuelve un resultado «consulta no disponible» (check <c>error</c>), que
/// la pantalla ya sabe mostrar, sin tumbar el resto del trámite.
/// </summary>
internal sealed class ConsultasRemotasCliente(
    ConsultasService.ConsultasServiceClient client,
    ILogger<ConsultasRemotasCliente> logger)
{
    private static readonly Meter Meter = new("Flit.Consultas.Remoto");
    private static readonly Counter<long> NoDisponibles = Meter.CreateCounter<long>("flit.consultas.no_disponible", description: "Consultas que no se pudieron hacer porque core-consultas no respondió");

    internal enum Categoria { Desconocida, Vehiculo, Conductor, Multas, Rnmc, Rues }

    /// <summary>A qué método de ConsultasService va cada proveedor.</summary>
    internal static Categoria CategoriaDe(string proveedor) => proveedor switch
    {
        "verifik" or "kyverum_runt" or "intempo" => Categoria.Vehiculo,
        "verifik_conductor" or "kyverum_runt_conductor" => Categoria.Conductor,
        "verifik_simit" or "flit_fines" or "kyverum_fines" => Categoria.Multas,
        "verifik_rnmc" => Categoria.Rnmc,
        "verifik_rues" => Categoria.Rues,
        _ => Categoria.Desconocida,
    };

    public async Task<ConsultationResult> ConsultarAsync(Categoria categoria, string? proveedor, ConsultationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        try
        {
            return await LlamarAsync(categoria, proveedor, ctx, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RpcException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            var estado = ex is RpcException rpc ? rpc.StatusCode.ToString() : "sin token";
            ConsultasRemotasLog.NoDisponible(logger, categoria.ToString(), proveedor ?? "cadena", ctx.TenantId, estado, ex);
            NoDisponibles.Add(1, new KeyValuePair<string, object?>("categoria", categoria.ToString()), new KeyValuePair<string, object?>("estado", estado));
            return NoDisponible(proveedor ?? "consultas");
        }
    }

    internal static ConsultationResult NoDisponible(string proveedor) =>
        new(proveedor, "red",
            [new ConsultationCheck("provider", "Consulta", "error", proveedor, "Consulta no disponible: el servicio de consultas no responde. Intenta de nuevo en unos minutos.")],
            []);

    private async Task<ConsultationResult> LlamarAsync(Categoria categoria, string? proveedor, ConsultationContext ctx, CancellationToken ct)
    {
        // La empresa de la consulta (puede no ser la del usuario: un SuperAdmin consulta por otra) va explícita; sin
        // ella, la del usuario de la petición.
        var metadata = DocumentosPorConsultas.Empresa(ctx.TenantId);
        var opciones = new OpcionesConsulta { IncluirRespuestaCruda = true, Referencia = ctx.InstanceId == Guid.Empty ? string.Empty : ctx.InstanceId.ToString() };
        if (!string.IsNullOrWhiteSpace(proveedor))
            opciones.Proveedor = proveedor;

        ResultadoConsulta resultado = categoria switch
        {
            Categoria.Vehiculo => (await client.ConsultarVehiculoAsync(new ConsultarVehiculoRequest
            {
                Opciones = opciones,
                Vin = Valor(ctx, "vin") is { } vin ? new Flit.Platform.Grpc.V1.Vin { Valor = vin } : null,
                Placa = Valor(ctx, "plate") is { } placa ? new Flit.Platform.Grpc.V1.Placa { Valor = placa } : null,
                Propietario = Documento(ctx, "owner_document_type", "owner_document_number"),
            }, metadata, cancellationToken: ct).ConfigureAwait(false)).Resultado,
            Categoria.Conductor => (await client.ConsultarConductorAsync(new ConsultarConductorRequest
            {
                Opciones = opciones,
                Documento = Documento(ctx, "document_type", "document_number"),
            }, metadata, cancellationToken: ct).ConfigureAwait(false)).Resultado,
            Categoria.Multas => (await client.ConsultarMultasAsync(new ConsultarMultasRequest
            {
                Opciones = opciones,
                Documento = Documento(ctx, "owner_document_type", "owner_document_number"),
                Placa = Valor(ctx, "plate") is { } placaMultas ? new Flit.Platform.Grpc.V1.Placa { Valor = placaMultas } : null,
            }, metadata, cancellationToken: ct).ConfigureAwait(false)).Resultado,
            Categoria.Rnmc => (await client.ConsultarRnmcAsync(new ConsultarRnmcRequest
            {
                Opciones = opciones,
                Documento = Documento(ctx, "owner_document_type", "owner_document_number"),
                FechaExpedicionDocumento = Valor(ctx, "document_issue_date") ?? string.Empty,
            }, metadata, cancellationToken: ct).ConfigureAwait(false)).Resultado,
            Categoria.Rues => (await client.ConsultarRuesAsync(new ConsultarRuesRequest
            {
                Opciones = opciones,
                Nit = Valor(ctx, "nit") ?? Valor(ctx, "actor_document_number") ?? string.Empty,
            }, metadata, cancellationToken: ct).ConfigureAwait(false)).Resultado,
            _ => throw new InvalidOperationException($"Categoría sin método remoto: {categoria}."),
        };
        return ResultadoConsultaMapper.FromProto(resultado);
    }

    private static string? Valor(ConsultationContext ctx, string clave) =>
        ctx.FieldValues.TryGetValue(clave, out var valor) && !string.IsNullOrWhiteSpace(valor) ? valor : null;

    private static Flit.Platform.Grpc.V1.DocumentoIdentidad? Documento(ConsultationContext ctx, string tipo, string numero) =>
        Valor(ctx, numero) is { } n ? new Flit.Platform.Grpc.V1.DocumentoIdentidad { Tipo = Valor(ctx, tipo) ?? string.Empty, Numero = n } : null;
}

/// <summary>
/// Registro de proveedores de core-api: cada clave conocida es un proxy a core-consultas (mismo proveedor pedido
/// explícitamente). <c>flit_integrations</c> es un stub sin llamadas externas y se queda en proceso.
/// </summary>
internal sealed class ConsultasRemotasRegistry(ConsultasRemotasCliente remoto) : IConsultationProviderRegistry
{
    private static readonly FlitIntegrationsGatewayProvider Stub = new();

    public IConsultationProvider? Resolve(string providerKey)
    {
        if (string.Equals(providerKey, Stub.Key, StringComparison.Ordinal))
            return Stub;
        var categoria = ConsultasRemotasCliente.CategoriaDe(providerKey);
        return categoria == ConsultasRemotasCliente.Categoria.Desconocida ? null : new ProveedorRemoto(providerKey, categoria, remoto);
    }

    private sealed class ProveedorRemoto(string key, ConsultasRemotasCliente.Categoria categoria, ConsultasRemotasCliente remoto) : IConsultationProvider
    {
        public string Key => key;

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct) =>
            remoto.ConsultarAsync(categoria, key, ctx, ct);
    }
}

/// <summary>
/// Cadena de respaldo: la ejecuta core-consultas con la cadena de la empresa (HU #13344). <see cref="ResolveChain"/>
/// solo la describe (pantalla de configuración) con las mismas reglas de siempre.
/// </summary>
internal sealed class ConsultasRemotasChainResolver(
    IConsultationProviderRegistry registry, IOptions<ConsultationChainOptions> options, ConsultasRemotasCliente remoto) : IConsultationProviderChainResolver
{
    public IReadOnlyList<string> ResolveChain(ConsultationKind kind, ConsultationTenantOverride? tenantOverride = null) =>
        new ConsultationProviderChainResolver(registry, options.Value).ResolveChain(kind, tenantOverride);

    public Task<ConsultationResult> ConsultAsync(ConsultationKind kind, ConsultationContext ctx, ConsultationTenantOverride? tenantOverride, CancellationToken ct) =>
        remoto.ConsultarAsync(
            kind == ConsultationKind.Conductor ? ConsultasRemotasCliente.Categoria.Conductor : ConsultasRemotasCliente.Categoria.Vehiculo,
            proveedor: null, ctx, ct);
}

internal static partial class ConsultasRemotasLog
{
    [LoggerMessage(EventId = 7412, Level = LogLevel.Error,
        Message = "Consultas remoto no respondió ({Estado}) para {Categoria}/{Proveedor} de {TenantId}: consulta no disponible")]
    public static partial void NoDisponible(ILogger logger, string categoria, string proveedor, Guid tenantId, string estado, Exception ex);

    [LoggerMessage(EventId = 7413, Level = LogLevel.Error,
        Message = "Consultas remoto no respondió ({Estado}) el avalúo de {TenantId}: la sugerencia queda vacía")]
    public static partial void AvaluosNoDisponibles(ILogger logger, Guid tenantId, string estado, Exception ex);
}
