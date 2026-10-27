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

    // HU #13345: cada método se mide (consultas.consumo) alrededor de su implementación. Los envoltorios gRPC sacan el llamador, el
    // deadline y la cancelación de la llamada; el consumidor del bus (ADR-0075, HU #13876) llama a las mismas sobrecargas con el
    // llamador del sobre, así la medición, los overrides por empresa y la caché son los mismos por las dos vías.
    public override Task<ConsultarVehiculoResponse> ConsultarVehiculo(ConsultarVehiculoRequest request, ServerCallContext context) =>
        ConsultarVehiculo(request, Llamador(context), context.Deadline, context.CancellationToken);

    public override Task<ConsultarConductorResponse> ConsultarConductor(ConsultarConductorRequest request, ServerCallContext context) =>
        ConsultarConductor(request, Llamador(context), context.Deadline, context.CancellationToken);

    public override Task<ConsultarMultasResponse> ConsultarMultas(ConsultarMultasRequest request, ServerCallContext context) =>
        ConsultarMultas(request, Llamador(context), context.CancellationToken);

    public override Task<ConsultarRnmcResponse> ConsultarRnmc(ConsultarRnmcRequest request, ServerCallContext context)
    {
        var caller = Llamador(context);
        return consumo.MedirAsync(caller, "rnmc", () => ConsultarRnmcCore(request, caller, context.CancellationToken), r => Resumen(r.Resultado), context.CancellationToken);
    }

    public override Task<ConsultarRuesResponse> ConsultarRues(ConsultarRuesRequest request, ServerCallContext context) =>
        ConsultarRues(request, Llamador(context), context.CancellationToken);

    public override Task<ConsultarAvaluosResponse> ConsultarAvaluos(ConsultarAvaluosRequest request, ServerCallContext context) =>
        ConsultarAvaluos(request, Llamador(context), context.CancellationToken);

    /// <summary>Vehículo, medido, con el llamador y el <paramref name="deadline"/> dados (<see cref="DateTime.MaxValue"/> = sin deadline).</summary>
    internal Task<ConsultarVehiculoResponse> ConsultarVehiculo(ConsultarVehiculoRequest request, PlatformServiceCaller caller, DateTime deadline, CancellationToken ct) =>
        consumo.MedirAsync(caller, "vehiculo", () => ConsultarVehiculoCore(request, caller, deadline, ct), r => Resumen(r.Resultado), ct);

    /// <summary>Conductor (RUNT persona), medido, con el llamador y el <paramref name="deadline"/> dados.</summary>
    internal Task<ConsultarConductorResponse> ConsultarConductor(ConsultarConductorRequest request, PlatformServiceCaller caller, DateTime deadline, CancellationToken ct) =>
        consumo.MedirAsync(caller, "conductor", () => ConsultarConductorCore(request, caller, deadline, ct), r => Resumen(r.Resultado), ct);

    /// <summary>Multas, medidas (SIMIT o fuente interna según la empresa de <paramref name="caller"/>).</summary>
    internal Task<ConsultarMultasResponse> ConsultarMultas(ConsultarMultasRequest request, PlatformServiceCaller caller, CancellationToken ct) =>
        consumo.MedirAsync(caller, "multas", () => ConsultarMultasCore(request, caller, ct), r => Resumen(r.Resultado), ct);

    /// <summary>RUES por NIT, medido.</summary>
    internal Task<ConsultarRuesResponse> ConsultarRues(ConsultarRuesRequest request, PlatformServiceCaller caller, CancellationToken ct) =>
        consumo.MedirAsync(caller, "rues", () => ConsultarRuesCore(request, caller, ct), r => Resumen(r.Resultado), ct);

    /// <summary>Avalúos de las fuentes habilitadas para la empresa de <paramref name="caller"/>, medidos.</summary>
    internal Task<ConsultarAvaluosResponse> ConsultarAvaluos(ConsultarAvaluosRequest request, PlatformServiceCaller caller, CancellationToken ct) =>
        consumo.MedirAsync(caller, "avaluos", () => ConsultarAvaluosCore(request, caller, ct),
            r => (r.FuentePrincipal, r.ValorSugerido > 0 ? "verde" : "sin_datos", false), ct);

    private async Task<ConsultarVehiculoResponse> ConsultarVehiculoCore(ConsultarVehiculoRequest request, PlatformServiceCaller caller, DateTime deadline, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var vin = request.Vin?.Valor;
        var placa = request.Placa?.Valor;
        if (string.IsNullOrWhiteSpace(vin) && string.IsNullOrWhiteSpace(placa))
            throw Invalido("Se necesita placa o VIN.");

        var campos = Campos(("vin", vin), ("plate", placa), ("owner_document_type", request.Propietario?.Tipo), ("owner_document_number", request.Propietario?.Numero));
        var kind = string.IsNullOrWhiteSpace(vin) ? ConsultationKind.VehiclePlate : ConsultationKind.VehicleVin;
        var resultado = await ConsultarCadenaAsync(kind, "vehiculo", campos, request.Opciones, caller, deadline, ct).ConfigureAwait(false);
        return new ConsultarVehiculoResponse { Resultado = resultado };
    }

    private async Task<ConsultarConductorResponse> ConsultarConductorCore(ConsultarConductorRequest request, PlatformServiceCaller caller, DateTime deadline, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var documento = Documento(request.Documento);
        var campos = Campos(("document_type", documento.Tipo), ("document_number", documento.Numero));
        var resultado = await ConsultarCadenaAsync(ConsultationKind.Conductor, "conductor", campos, request.Opciones, caller, deadline, ct).ConfigureAwait(false);
        return new ConsultarConductorResponse { Resultado = resultado };
    }

    private async Task<ConsultarMultasResponse> ConsultarMultasCore(ConsultarMultasRequest request, PlatformServiceCaller caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var placa = request.Placa?.Valor;
        var documento = request.Documento is { Numero.Length: > 0 } d ? d : null;
        if (documento is null && string.IsNullOrWhiteSpace(placa))
            throw Invalido("Se necesita el documento (tipo y número) o la placa.");

        var tenantOverride = await overrides.GetAsync(caller.TenantId, ct).ConfigureAwait(false);

        // FEATURE 05: la fuente de la empresa manda; el tipo de persona solo elige el proveedor externo.
        var proveedor = string.IsNullOrWhiteSpace(request.Opciones?.Proveedor)
            ? FinesProviderResolver.Resolve(tenantOverride?.FinesQuerySource, request.TipoPersona != TipoPersona.Juridica)
            : request.Opciones.Proveedor;

        // Bug SIMIT por placa (2026-10-27): con placa, el proveedor que consulta por placa (Verifik SIMIT) recibe SOLO la placa, aunque
        // llegue el documento del propietario: es la consulta del vehículo, no la de la persona. Los que exigen documento (flit_fines,
        // kyverum_fines) usan el recibido (el del propietario del RUNT) y sin él es un InvalidArgument propio.
        Dictionary<string, string?> campos;
        if (!string.IsNullOrWhiteSpace(placa) && ConsultaMultasPorPlaca(proveedor))
            campos = Campos(("plate", placa));
        else if (documento is null)
            throw Invalido($"El proveedor de multas {proveedor} no consulta por placa: se necesita el documento (tipo y número).");
        else
            campos = Campos(("owner_document_type", documento.Tipo), ("owner_document_number", documento.Numero), ("plate", placa));

        var resultado = await ConsultarProveedorAsync(proveedor, campos, request.Opciones, caller, ct).ConfigureAwait(false);
        return new ConsultarMultasResponse { Resultado = resultado };
    }

    private async Task<ConsultarRnmcResponse> ConsultarRnmcCore(ConsultarRnmcRequest request, PlatformServiceCaller caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var documento = Documento(request.Documento);
        var campos = Campos(("owner_document_type", documento.Tipo), ("owner_document_number", documento.Numero), ("document_issue_date", request.FechaExpedicionDocumento));
        var resultado = await ConsultarProveedorAsync(Proveedor(request.Opciones, "verifik_rnmc"), campos, request.Opciones, caller, ct).ConfigureAwait(false);
        return new ConsultarRnmcResponse { Resultado = resultado };
    }

    private async Task<ConsultarRuesResponse> ConsultarRuesCore(ConsultarRuesRequest request, PlatformServiceCaller caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Nit))
            throw Invalido("Se necesita el NIT.");

        var resultado = await ConsultarProveedorAsync(Proveedor(request.Opciones, "verifik_rues"), Campos(("nit", request.Nit)), request.Opciones, caller, ct).ConfigureAwait(false);
        return new ConsultarRuesResponse { Resultado = resultado };
    }

    private async Task<ConsultarAvaluosResponse> ConsultarAvaluosCore(ConsultarAvaluosRequest request, PlatformServiceCaller caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);
        if (string.IsNullOrWhiteSpace(request.Vin?.Valor) && string.IsNullOrWhiteSpace(request.Placa?.Valor))
            throw Invalido("Se necesita placa o VIN.");

        var campos = Campos(
            ("vin", request.Vin?.Valor),
            ("plate", request.Placa?.Valor),
            ("vehicle_year", Numero(request.Modelo)),
            ("vehicle_engine_displacement", Numero(request.Cilindraje)),
            ("vehicle_fuel", request.Combustible),
            ("vehicle_passengers", Numero(request.Pasajeros)));
        var set = await avaluoPolicy.GetAsync(caller.TenantId, ct).ConfigureAwait(false);
        var valor = await AvaluoAggregator.SuggestAsync(avaluos, set, new AvaluoContext(Guid.Empty, caller.TenantId, campos), ct).ConfigureAwait(false);
        return ResultadoConsultaMapper.ToProto(valor);
    }

    /// <summary>
    /// Cadena de respaldo de la empresa (AC1). El primero de la cadena puede gastar a lo sumo
    /// <see cref="PresupuestoPrimario"/> del tiempo que le queda a la llamada: si no responde, el siguiente alcanza a
    /// contestar dentro del deadline (AC3). Un proveedor pedido explícitamente salta la cadena.
    /// </summary>
    private async Task<ResultadoConsulta> ConsultarCadenaAsync(
        ConsultationKind kind, string plantilla, IReadOnlyDictionary<string, string?> campos, OpcionesConsulta? opciones, PlatformServiceCaller caller,
        DateTime deadline, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(opciones?.Proveedor))
            return await ConsultarProveedorAsync(opciones.Proveedor, campos, opciones, caller, ct).ConfigureAwait(false);

        var tenantOverride = await overrides.GetAsync(caller.TenantId, ct).ConfigureAwait(false);
        var configurado = tenantOverride?.FailoverTimeoutMs ?? chainOptions.Value.FailoverTimeoutMs;
        var ajustado = (tenantOverride ?? new ConsultationTenantOverride(null, null)) with { FailoverTimeoutMs = Presupuesto(configurado, deadline) };

        var result = await chain.ConsultAsync(kind, new ConsultationContext(Guid.Empty, caller.TenantId, plantilla, campos), ajustado, ct)
            .ConfigureAwait(false);
        return ResultadoConsultaMapper.ToProto(result, opciones?.IncluirRespuestaCruda == true);
    }

    private async Task<ResultadoConsulta> ConsultarProveedorAsync(
        string clave, IReadOnlyDictionary<string, string?> campos, OpcionesConsulta? opciones, PlatformServiceCaller caller, CancellationToken ct)
    {
        var provider = registry.Resolve(clave) ?? throw Invalido($"Proveedor desconocido: {clave}.");
        var result = await provider.ConsultAsync(new ConsultationContext(Guid.Empty, caller.TenantId, provider.Key, campos), ct)
            .ConfigureAwait(false);
        return ResultadoConsultaMapper.ToProto(result, opciones?.IncluirRespuestaCruda == true);
    }

    /// <summary>Quién llama, ya validado por el interceptor del SDK.</summary>
    private static PlatformServiceCaller Llamador(ServerCallContext context) => PlatformServiceCaller.From(context);

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

    /// <summary>Proveedores de multas que consultan por placa sin documento (Verifik SIMIT: <c>/v2/co/simit/consultar/placa</c>).</summary>
    private static bool ConsultaMultasPorPlaca(string proveedor) =>
        string.Equals(proveedor, FinesProviderResolver.VerifikSimit, StringComparison.OrdinalIgnoreCase);

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
