using Flit.Consultas.Grpc.Mapping;
using Flit.Consultas.Grpc.V1;
using Flit.Modules.Improntas.Domain;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Flit.Consultas.Api.Grpc;

/// <summary>
/// <c>flit.consultas.v1.ConsultasService</c> (Epic #13316, HU #13343; ADR-0065, contrato v1.3 §6.1): las consultas a
/// proveedores para cualquier producto, con la cadena de respaldo, los overrides y las credenciales de un solo lugar.
/// Fachada delgada sobre el módulo de consultas: traduce el pedido a los campos que leen los proveedores
/// (<c>vin</c>, <c>plate</c>, <c>owner_document_*</c>, …) y el resultado a <c>ResultadoConsulta</c>. La empresa y quién
/// llama los validó el interceptor del SDK (token de servicio con <see cref="Scope"/>).
/// </summary>
internal sealed partial class ConsultasGrpcService(
    IConsultationProviderChainResolver chain,
    IConsultationProviderRegistry registry,
    IConsultationTenantOverrideProvider overrides,
    IAvaluoProviderRegistry avaluos,
    IAvaluoProviderPolicy avaluoPolicy,
    IOptions<ConsultationChainOptions> chainOptions,
    ConsumoRecorder consumo,
    TimeProvider time,
    IRuntVehicleRawClient runtCrudo,
    IImprontaExternalClient improntas,
    IEnumerable<IRuesExternalClient> rues) : ConsultasService.ConsultasServiceBase
{
    public const string Scope = "platform.consultas";

    /// <summary>Del tiempo que queda de la llamada, lo que puede gastar el primero de la cadena antes del respaldo.</summary>
    internal const double PresupuestoPrimario = 0.6;

    // HU #13345: cada método se mide (consultas.consumo) alrededor de su implementación.
    public override Task<ConsultarVehiculoResponse> ConsultarVehiculo(ConsultarVehiculoRequest request, ServerCallContext context) =>
        consumo.MedirAsync(context, "vehiculo", () => ConsultarVehiculoCore(request, context), r => Resumen(r.Resultado));

    public override Task<ConsultarConductorResponse> ConsultarConductor(ConsultarConductorRequest request, ServerCallContext context) =>
        consumo.MedirAsync(context, "conductor", () => ConsultarConductorCore(request, context), r => Resumen(r.Resultado));

    public override Task<ConsultarMultasResponse> ConsultarMultas(ConsultarMultasRequest request, ServerCallContext context) =>
        consumo.MedirAsync(context, "multas", () => ConsultarMultasCore(request, context), r => Resumen(r.Resultado));

    public override Task<ConsultarRnmcResponse> ConsultarRnmc(ConsultarRnmcRequest request, ServerCallContext context) =>
        consumo.MedirAsync(context, "rnmc", () => ConsultarRnmcCore(request, context), r => Resumen(r.Resultado));

    public override Task<ConsultarRuesResponse> ConsultarRues(ConsultarRuesRequest request, ServerCallContext context) =>
        consumo.MedirAsync(context, "rues", () => ConsultarRuesCore(request, context), r => Resumen(r.Resultado));

    public override Task<ConsultarAvaluosResponse> ConsultarAvaluos(ConsultarAvaluosRequest request, ServerCallContext context) =>
        consumo.MedirAsync(context, "avaluos", () => ConsultarAvaluosCore(request, context), r => (r.FuentePrincipal, r.ValorSugerido > 0 ? "verde" : "sin_datos", false));

    private async Task<ConsultarVehiculoResponse> ConsultarVehiculoCore(ConsultarVehiculoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var vin = request.Vin?.Valor;
        var placa = request.Placa?.Valor;
        if (string.IsNullOrWhiteSpace(vin) && string.IsNullOrWhiteSpace(placa))
            throw Invalido("Se necesita placa o VIN.");

        var campos = Campos(("vin", vin), ("plate", placa), ("owner_document_type", request.Propietario?.Tipo), ("owner_document_number", request.Propietario?.Numero));
        var kind = string.IsNullOrWhiteSpace(vin) ? ConsultationKind.VehiclePlate : ConsultationKind.VehicleVin;
        var resultado = await ConsultarCadenaAsync(kind, "vehiculo", campos, request.Opciones, context).ConfigureAwait(false);
        return new ConsultarVehiculoResponse { Resultado = resultado };
    }

    private async Task<ConsultarConductorResponse> ConsultarConductorCore(ConsultarConductorRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var documento = Documento(request.Documento);
        var campos = Campos(("document_type", documento.Tipo), ("document_number", documento.Numero));
        var resultado = await ConsultarCadenaAsync(ConsultationKind.Conductor, "conductor", campos, request.Opciones, context).ConfigureAwait(false);
        return new ConsultarConductorResponse { Resultado = resultado };
    }

    private async Task<ConsultarMultasResponse> ConsultarMultasCore(ConsultarMultasRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var documento = Documento(request.Documento);
        var caller = PlatformServiceCaller.From(context);
        var tenantOverride = await overrides.GetAsync(caller.TenantId, context.CancellationToken).ConfigureAwait(false);

        // FEATURE 05: la fuente de la empresa manda; el tipo de persona solo elige el proveedor externo.
        var proveedor = string.IsNullOrWhiteSpace(request.Opciones?.Proveedor)
            ? FinesProviderResolver.Resolve(tenantOverride?.FinesQuerySource, request.TipoPersona != TipoPersona.Juridica)
            : request.Opciones.Proveedor;
        var campos = Campos(("owner_document_type", documento.Tipo), ("owner_document_number", documento.Numero), ("plate", request.Placa?.Valor));
        var resultado = await ConsultarProveedorAsync(proveedor, campos, request.Opciones, context).ConfigureAwait(false);
        return new ConsultarMultasResponse { Resultado = resultado };
    }

    private async Task<ConsultarRnmcResponse> ConsultarRnmcCore(ConsultarRnmcRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var documento = Documento(request.Documento);
        var campos = Campos(("owner_document_type", documento.Tipo), ("owner_document_number", documento.Numero), ("document_issue_date", request.FechaExpedicionDocumento));
        var resultado = await ConsultarProveedorAsync(Proveedor(request.Opciones, "verifik_rnmc"), campos, request.Opciones, context).ConfigureAwait(false);
        return new ConsultarRnmcResponse { Resultado = resultado };
    }

    private async Task<ConsultarRuesResponse> ConsultarRuesCore(ConsultarRuesRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Nit))
            throw Invalido("Se necesita el NIT.");

        var resultado = await ConsultarProveedorAsync(Proveedor(request.Opciones, "verifik_rues"), Campos(("nit", request.Nit)), request.Opciones, context).ConfigureAwait(false);
        return new ConsultarRuesResponse { Resultado = resultado };
    }

    private async Task<ConsultarAvaluosResponse> ConsultarAvaluosCore(ConsultarAvaluosRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(request.Vin?.Valor) && string.IsNullOrWhiteSpace(request.Placa?.Valor))
            throw Invalido("Se necesita placa o VIN.");

        var caller = PlatformServiceCaller.From(context);
        var campos = Campos(
            ("vin", request.Vin?.Valor),
            ("plate", request.Placa?.Valor),
            ("vehicle_year", Numero(request.Modelo)),
            ("vehicle_engine_displacement", Numero(request.Cilindraje)),
            ("vehicle_fuel", request.Combustible),
            ("vehicle_passengers", Numero(request.Pasajeros)));
        var set = await avaluoPolicy.GetAsync(caller.TenantId, context.CancellationToken).ConfigureAwait(false);
        var valor = await AvaluoAggregator.SuggestAsync(avaluos, set, new AvaluoContext(Guid.Empty, caller.TenantId, campos), context.CancellationToken).ConfigureAwait(false);
        return ResultadoConsultaMapper.ToProto(valor);
    }

    /// <summary>
    /// Cadena de respaldo de la empresa (AC1). El primero de la cadena puede gastar a lo sumo
    /// <see cref="PresupuestoPrimario"/> del tiempo que le queda a la llamada: si no responde, el siguiente alcanza a
    /// contestar dentro del deadline (AC3). Un proveedor pedido explícitamente salta la cadena.
    /// </summary>
    private async Task<ResultadoConsulta> ConsultarCadenaAsync(
        ConsultationKind kind, string plantilla, IReadOnlyDictionary<string, string?> campos, OpcionesConsulta? opciones, ServerCallContext context)
    {
        if (!string.IsNullOrWhiteSpace(opciones?.Proveedor))
            return await ConsultarProveedorAsync(opciones.Proveedor, campos, opciones, context).ConfigureAwait(false);

        var caller = PlatformServiceCaller.From(context);
        var tenantOverride = await overrides.GetAsync(caller.TenantId, context.CancellationToken).ConfigureAwait(false);
        var configurado = tenantOverride?.FailoverTimeoutMs ?? chainOptions.Value.FailoverTimeoutMs;
        var ajustado = (tenantOverride ?? new ConsultationTenantOverride(null, null)) with { FailoverTimeoutMs = Presupuesto(configurado, context.Deadline) };

        var result = await chain.ConsultAsync(kind, new ConsultationContext(Guid.Empty, caller.TenantId, plantilla, campos), ajustado, context.CancellationToken)
            .ConfigureAwait(false);
        return ResultadoConsultaMapper.ToProto(result, opciones?.IncluirRespuestaCruda == true);
    }

    private async Task<ResultadoConsulta> ConsultarProveedorAsync(
        string clave, IReadOnlyDictionary<string, string?> campos, OpcionesConsulta? opciones, ServerCallContext context)
    {
        var provider = registry.Resolve(clave) ?? throw Invalido($"Proveedor desconocido: {clave}.");
        var caller = PlatformServiceCaller.From(context);
        var result = await provider.ConsultAsync(new ConsultationContext(Guid.Empty, caller.TenantId, provider.Key, campos), context.CancellationToken)
            .ConfigureAwait(false);
        return ResultadoConsultaMapper.ToProto(result, opciones?.IncluirRespuestaCruda == true);
    }

    private static (string Proveedor, string Resultado, bool DesdeCache) Resumen(ResultadoConsulta r) =>
        (r.Proveedor, ConsumoRecorder.Resultado(r.Semaforo), r.DesdeCache);

    internal int Presupuesto(int configuradoMs, DateTime deadline)
    {
        if (deadline == DateTime.MaxValue)
            return configuradoMs;

        var restante = deadline - time.GetUtcNow().UtcDateTime;
        var maximo = (int)Math.Max(1, restante.TotalMilliseconds * PresupuestoPrimario);
        return Math.Min(configuradoMs, maximo);
    }

    private static string Proveedor(OpcionesConsulta? opciones, string porDefecto) =>
        string.IsNullOrWhiteSpace(opciones?.Proveedor) ? porDefecto : opciones.Proveedor;

    private static Flit.Platform.Grpc.V1.DocumentoIdentidad Documento(Flit.Platform.Grpc.V1.DocumentoIdentidad? documento) =>
        documento is { Numero.Length: > 0 } ? documento : throw Invalido("Se necesita el documento (tipo y número).");

    private static string? Numero(int valor) => valor > 0 ? valor.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    private static Dictionary<string, string?> Campos(params (string Clave, string? Valor)[] campos)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (clave, valor) in campos)
        {
            if (!string.IsNullOrWhiteSpace(valor))
                dict[clave] = valor.Trim();
        }

        return dict;
    }

    private static RpcException Invalido(string detalle) => new(new Status(StatusCode.InvalidArgument, detalle));
}
