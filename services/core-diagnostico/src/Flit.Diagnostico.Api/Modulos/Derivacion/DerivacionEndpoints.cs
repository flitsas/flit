using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using DerivacionTramitesService = Flit.Tramites.Grpc.V1.DerivacionTramitesService;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// «Crear trámite» desde el diagnóstico (Punto 4, incremento 2, ADR-0084, diseño §5.1). Con <c>Diagnostico:Derivacion:Habilitada</c>
/// (apagada por defecto) se mapea el individual:
/// <list type="bullet">
/// <item><c>POST /diagnosticos/{id}/vehiculos/{vid}/tramite</c> — <see cref="DerivacionIndividual"/>; permiso
/// <c>diagnostico.derivacion.crear</c> (el rol de backoffice no lo tiene) y, además, 403 <c>DERIVACION_DESDE_BACKOFFICE</c> si la
/// petición trae la empresa explícita del backoffice (<c>?empresa=</c>): desde el backoffice no se deriva, ni siquiera un SuperAdmin.</item>
/// </list>
/// y cada fila de la grilla gana <c>derivacion</c> (<see cref="CompletarGrillaAsync"/>). Incremento 3 (masivo, mismas guardas):
/// <list type="bullet">
/// <item><c>POST /diagnosticos/{id}/tramites</c> — <see cref="DerivacionMasiva"/> (202 + <c>Location</c>); <c>diagnostico.derivacion.crear</c>
/// y 403 desde el backoffice.</item>
/// <item><c>GET /diagnosticos/{id}/tramites/operaciones[?ultima=true]</c> y <c>GET …/operaciones/{opId}[?estado=&amp;pagina=]</c> —
/// <see cref="ProgresoOperacion"/>; <c>diagnostico.diagnosticos.ver</c>.</item>
/// <item><c>POST …/operaciones/{opId}/reintentar-fallidas</c> — 202 o 409; <c>diagnostico.derivacion.crear</c> y 403 desde el backoffice.</item>
/// </list>
/// El consumidor de <c>diagnostico.derivaciones</c> (<see cref="Bus.ConsumidorDerivacion"/>) y su reconciliador se registran con
/// <c>Habilitada</c> y <c>ConsumidorHabilitado</c>.
/// </summary>
internal static class DerivacionEndpoints
{
    public const string Ruta = DiagnosticosEndpoints.Ruta + "/{id:guid}/vehiculos/{vid:guid}/tramite";

    /// <summary>Masivo (incremento 3).</summary>
    public const string RutaMasivo = DiagnosticosEndpoints.Ruta + "/{id:guid}/tramites";

    public const string RutaOperaciones = RutaMasivo + "/operaciones";

    public const string RutaOperacion = RutaOperaciones + "/{opId:guid}";

    public const string RutaReintentarFallidas = RutaOperacion + "/reintentar-fallidas";

    public const string CodigoDesdeBackoffice = "DERIVACION_DESDE_BACKOFFICE";

    /// <summary>Scope del token de svc-diagnostico para el rpc de derivación de core-api (incremento 1).</summary>
    public const string Scope = "platform.tramites.derivacion";

    /// <summary>
    /// Opciones validadas al arrancar y el cliente del rpc por el mismo <c>Diagnostico:Tramites:Address</c> del catálogo (token de
    /// svc-diagnostico, <c>aud=tramites</c>, scope <see cref="Scope"/>, deadline <see cref="OpcionesDerivacion.PlazoRpcSegundos"/>); sin
    /// dirección, <see cref="ClienteDerivacionNoConectado"/>. TryAdd: las pruebas ponen su doble antes.
    /// </summary>
    public static IServiceCollection AddDerivacion(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<OpcionesDerivacion>()
            .Bind(configuration.GetSection(OpcionesDerivacion.Seccion))
            .Validate(OpcionesDerivacion.Validar, $"{OpcionesDerivacion.Seccion} tiene valores fuera de rango (PlazoRpcSegundos 1-120, UrlTramites absoluta http(s) o vacía, Concurrencia >= 1, Prefetch >= Concurrencia, 1 <= CreditoPorCompania <= Concurrencia, TasaPorSegundo 1-10000, PausaTrasFallos >= 1, PausaSegundos 1-3600, MaxIntentos 1-3, MaxItemsPorOperacion 1-50000, Reconciliador: Intervalo > 0, Margen >= 1 min, Maximo 1-5000).")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        if (configuration[TiposTramiteEndpoints.ClaveDireccion] is { Length: > 0 } tramites)
        {
            var plazo = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue($"{OpcionesDerivacion.Seccion}:{nameof(OpcionesDerivacion.PlazoRpcSegundos)}", 15), 1, 120));
            services.AddFlitGrpcClient<DerivacionTramitesService.DerivacionTramitesServiceClient>(configuration, new Uri(tramites), Scope, o => o.Deadline = plazo);
            services.TryAddScoped<IClienteDerivacionTramites, ClienteDerivacionTramitesGrpc>();
        }
        else
        {
            services.TryAddSingleton<IClienteDerivacionTramites, ClienteDerivacionNoConectado>();
        }

        services.AddScoped<DerivacionIndividual>();

        // Incremento 3 (ADR-0084): masivo encolado con crédito por compañía.
        services.AddSingleton<FrenoTramites>();
        services.AddSingleton<ReintentoDerivacion>();
        services.AddScoped<DespachadorCredito>();
        services.AddScoped<EjecutorItemDerivacion>();
        services.AddScoped<DerivacionMasiva>();
        services.AddScoped<ProgresoOperacion>();
        services.TryAddScoped<Bus.ConsumidorDerivacion>();
        services.AddSingleton<Trabajos.ReconciliadorDerivacion>();
        var leidas = configuration.GetSection(OpcionesDerivacion.Seccion).Get<OpcionesDerivacion>() ?? new();
        if (leidas.Habilitada && leidas.ConsumidorHabilitado && OpcionesDerivacion.Validar(leidas))
        {
            services.AddFlitConsumer<DiagnosticoDb, Bus.ConsumidorDerivacion, Bus.DerivacionSolicitada>(
                configuration, Bus.DerivacionSolicitada.Cola, Bus.DerivacionSolicitada.Productor, [Bus.DerivacionSolicitada.Tipo], o =>
                {
                    o.Concurrencia = leidas.Concurrencia;
                    o.Prefetch = leidas.Prefetch;
                    // Solo el propio Diagnóstico (user_id validado por el broker) deja eventos en esta cola.
                    o.ProductoresPermitidos.Add(Bus.DerivacionSolicitada.Productor);
                    o.RetryDelays.Clear();
                    foreach (var espera in ReintentoDerivacion.Esperas)
                        o.RetryDelays.Add(espera);
                });
            // La red de seguridad de los ítems colgados corre con el consumidor.
            services.AddHostedService(sp => sp.GetRequiredService<Trabajos.ReconciliadorDerivacion>());
        }

        return services;
    }

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (!((IEndpointRouteBuilder)api).ServiceProvider.GetRequiredService<IOptions<OpcionesDerivacion>>().Value.Habilitada)
            return;
        api.MapPost(Ruta, Crear)
            .RequirePermiso(Permisos.DerivacionCrear)
            .AddEndpointFilter(SinBackoffice)
            .WithName("CrearTramiteDesdeVehiculo");

        // Incremento 3: masivo, progreso y reintentar fallidas.
        api.MapPost(RutaMasivo, CrearMasivo)
            .RequirePermiso(Permisos.DerivacionCrear)
            .AddEndpointFilter(SinBackoffice)
            .WithName("CrearTramitesDesdeDiagnostico");
        api.MapGet(RutaOperaciones, (Guid id, bool? ultima, ProgresoOperacion progreso, CancellationToken ct) => progreso.ListarAsync(id, ultima ?? false, ct))
            .RequirePermiso(Permisos.DiagnosticosVer)
            .WithName("ListarOperacionesDerivacion");
        api.MapGet(RutaOperacion, (Guid id, Guid opId, string? estado, int? pagina, ProgresoOperacion progreso, CancellationToken ct) => progreso.LeerAsync(id, opId, estado, pagina, ct))
            .RequirePermiso(Permisos.DiagnosticosVer)
            .WithName("LeerOperacionDerivacion");
        api.MapPost(RutaReintentarFallidas, (Guid id, Guid opId, ProgresoOperacion progreso, CancellationToken ct) => progreso.ReintentarFallidasAsync(id, opId, ct))
            .RequirePermiso(Permisos.DerivacionCrear)
            .AddEndpointFilter(SinBackoffice)
            .WithName("ReintentarFallidasDerivacion");
    }

    /// <summary>
    /// Pone <c>derivacion</c> en cada fila de la grilla de la compañía con la regla de <see cref="LecturaElegibilidad"/>; con la
    /// derivación apagada la deja en null. Lee solo las filas que viajan (en el sondeo incremental, las cambiadas).
    /// </summary>
    public static async Task<GrillaDto> CompletarGrillaAsync(GrillaDto grilla, DiagnosticoDb db, OpcionesDerivacion opciones, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grilla);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(opciones);
        if (!opciones.Habilitada || grilla.Vehiculos.Count == 0)
            return grilla;
        // HU #14563 B1: la grilla de la compañía ya leyó la elegibilidad de todo el lote; solo sin ella se lee aquí.
        var (tenant, elegibilidad) = grilla.Elegibilidad is { } leida
            ? (leida.TenantId, leida.PorVehiculo)
            : await LeerElegibilidadAsync(grilla, db, ct).ConfigureAwait(false);
        return grilla with
        {
            Vehiculos = [.. grilla.Vehiculos.Select(v => elegibilidad.TryGetValue(v.Id, out var e)
                ? v with { Derivacion = LecturaElegibilidad.Fila(e, opciones, tenant) }
                : v)],
        };
    }

    private static async Task<(Guid TenantId, IReadOnlyDictionary<Guid, ElegibilidadVehiculo> PorVehiculo)> LeerElegibilidadAsync(GrillaDto grilla, DiagnosticoDb db, CancellationToken ct)
    {
        var lote = await db.Set<CabeceraDiagnostico>().AsNoTracking().Where(c => c.Id == grilla.Id)
            .Select(c => new { c.TenantId, c.Modo }).SingleAsync(ct).ConfigureAwait(false);
        return (lote.TenantId, await LecturaElegibilidad.LeerAsync(db, grilla.Id, lote.Modo, [.. grilla.Vehiculos.Select(v => v.Id)], ct).ConfigureAwait(false));
    }

    /// <summary>El cuerpo es opcional (en Con análisis no hace falta): sin cuerpo, null; JSON inválido, 400 <c>CUERPO_INVALIDO</c>.</summary>
    private static async Task<IResult> Crear(Guid id, Guid vid, HttpRequest request, DerivacionIndividual individual, CancellationToken ct) =>
        await individual.CrearAsync(id, vid, await LeerCuerpoAsync<CrearTramiteEntrada>(request, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <summary>Masivo: el cuerpo (lista y máximo) se valida antes de tocar la base.</summary>
    private static async Task<IResult> CrearMasivo(Guid id, HttpRequest request, DerivacionMasiva masiva, CancellationToken ct) =>
        await masiva.CrearAsync(id, await LeerCuerpoAsync<CrearTramitesEntrada>(request, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <summary>Sin cuerpo, null; JSON inválido, 400 <see cref="CodigoCuerpoInvalido"/>.</summary>
    private static async Task<T?> LeerCuerpoAsync<T>(HttpRequest request, CancellationToken ct)
        where T : class
    {
        if (request.ContentLength is 0 || !request.Body.CanRead)
            return null;
        using var lector = new StreamReader(request.Body);
        var texto = await lector.ReadToEndAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(texto);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ReglaException(CodigoCuerpoInvalido, "El cuerpo no es un JSON válido.", StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Desde el backoffice (empresa explícita) no se deriva, ni siquiera un SuperAdmin con el permiso.</summary>
    private static async ValueTask<object?> SinBackoffice(EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente) =>
        contexto.HttpContext.Request.Query.ContainsKey(EmpresaExplicita.Parametro)
            ? throw ReglaException.Prohibido(CodigoDesdeBackoffice, "Desde el backoffice no se crean trámites.")
            : await siguiente(contexto).ConfigureAwait(false);

    public const string CodigoCuerpoInvalido = "CUERPO_INVALIDO";
}
