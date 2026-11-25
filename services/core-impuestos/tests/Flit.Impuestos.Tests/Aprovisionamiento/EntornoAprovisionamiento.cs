using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Impuestos.Tests.Aprovisionamiento;

/// <summary>
/// Entorno común de las pruebas de M07 (HU #14694): la API de verdad sobre una base efímera, el API de liquidación como
/// <see cref="ApiLiquidacionDePrueba"/> e Identidad como <see cref="IdentidadDePrueba"/>. Trae la entrega «como el SDK» (bandeja
/// de entrada en la misma transacción del efecto) para probar los consumidores sin RabbitMQ.
/// </summary>
/// <example>
/// <code>
/// var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
/// await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
/// await EntornoAprovisionamiento.SembrarAsync(api, empresa, EstadoAprovisionamiento.Pendiente, ct);
/// await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);
/// </code>
/// </example>
internal static class EntornoAprovisionamiento
{
    public const string RutaAlta = "/api/v1/integracion/empresas";
    public const string RutaFuentes = "/api/v1/fuentes";
    public const string LlaveDiagnostico = "DiAgNoStIcO-llave-de-prueba-0123456789-abc";

    public static Task<ApiDePrueba> ConApiAsync(ApiLiquidacionDePrueba proveedor, IdentidadDePrueba identidad, RegistroDeLogs? logs = null) =>
        ApiDePrueba.ConBaseAsync(b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Impuestos:Api:BaseUrl"] = ClienteApiLiquidacionTests.Base,
                ["Impuestos:Api:IntegrationToken"] = ClienteApiLiquidacionTests.Token,
            });
            b.Services.AddHttpClient<ClienteApiLiquidacion>().ConfigurePrimaryHttpMessageHandler(() => proveedor);
            b.Services.AddSingleton<IClienteIdentidadAlta>(identidad);
            if (logs is not null)
            {
                b.Logging.AddProvider(logs);
                b.Logging.AddFilter<RegistroDeLogs>(null, LogLevel.Trace);
            }
        });

    /// <summary>Proveedor que responde el alta con <paramref name="status"/> y <c>GET /fuentes</c> con 200.</summary>
    public static ApiLiquidacionDePrueba ProveedorConAlta(HttpStatusCode status, string cuerpo) => new()
    {
        Responder = r => r.RequestUri!.AbsolutePath == RutaAlta
            ? ApiLiquidacionDePrueba.Json(status, cuerpo)
            : ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, ClienteApiLiquidacionTests.Fuentes200),
    };

    public static string ErrorApi(string codigo) => "{\"error\":{\"code\":\"" + codigo + "\",\"message\":\"x\"}}";

    public static async Task SembrarAsync(
        ApiDePrueba api, Guid empresa, EstadoAprovisionamiento estado, CancellationToken ct, Action<AprovisionamientoEmpresa>? ajustar = null)
    {
        await using var db = api.Contexto(empresa);
        var fila = new AprovisionamientoEmpresa { TenantId = empresa, Estado = estado };
        ajustar?.Invoke(fila);
        db.Add(fila);
        await db.SaveChangesAsync(ct);
    }

    public static async Task GuardarCredencialAsync(ApiDePrueba api, Guid empresa, string apiKey, string? secreto, OrigenCredencial origen, CancellationToken ct)
    {
        using var scope = api.App.Services.CreateScope();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenCredenciales>();
        await using var db = almacen.ContextoDe(empresa);
        almacen.Escribir(db, null, empresa, new CredencialesEnClaro(apiKey, secreto), origen, null);
        await db.SaveChangesAsync(ct);
    }

    public static async Task<CredencialesEnClaro?> LeerCredencialAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        using var scope = api.App.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AlmacenCredenciales>().LeerAsync(empresa, ct);
    }

    public static async Task<bool> EvaluarAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Aprovisionador>().EvaluarAsync(empresa, ct);
    }

    public static async Task<AprovisionamientoEmpresa?> FilaAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleOrDefaultAsync(ct);
    }

    public static async Task<List<HistorialAprovisionamiento>> HistorialAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<HistorialAprovisionamiento>().AsNoTracking().OrderBy(h => h.En).ThenBy(h => h.Id).ToListAsync(ct);
    }

    public static EventEnvelope Sobre<T>(string tipo, Guid empresa, T data, string productor = "plataforma") => new(
        Guid.CreateVersion7(), tipo, 1, DateTimeOffset.UtcNow, empresa, productor, "prueba-m07",
        JsonSerializer.SerializeToElement(data, EventEnvelope.JsonOptions));

    public static EventEnvelope Cambio(Guid empresa, bool encendido, string producto = "impuestos") =>
        Sobre(ConsumidorCambioProducto.TipoCambio, empresa, new { tenantId = empresa, productCode = producto, enabled = encendido, changedAt = DateTimeOffset.UtcNow });

    public static EventEnvelope Reintentar(Guid empresa, Guid solicitadoPor) =>
        Sobre(ConsumidorCambioProducto.TipoReintentar, empresa, new { tenantId = empresa, solicitadoPor });

    /// <summary>
    /// Entrega el sobre como lo hace el consumidor del SDK: abre la transacción, confirma sin efecto un <c>eventId</c> que ya está
    /// en la bandeja de <see cref="ConsumidorCambioProducto.Cola"/> y, si no, llama al efecto y guarda la fila de la bandeja en el
    /// mismo <c>SaveChanges</c>. Devuelve si aplicó el efecto.
    /// </summary>
    public static async Task<bool> EntregarAsync(ApiDePrueba api, EventEnvelope sobre, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
        var consumidor = scope.ServiceProvider.GetRequiredService<ConsumidorCambioProducto>();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (await db.Set<InboxMessage>().AnyAsync(m => m.EventId == sobre.EventId && m.Consumer == ConsumidorCambioProducto.Cola, ct))
                return false;
            await consumidor.HandleAsync(sobre, sobre.DataAs<JsonElement>(), ct);
            db.Set<InboxMessage>().Add(new InboxMessage { EventId = sobre.EventId, Consumer = ConsumidorCambioProducto.Cola, Type = sobre.Type, ProcessedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        });
    }

    public static int Altas(ApiLiquidacionDePrueba proveedor) => proveedor.Pedidos.Count(p => p.Uri.EndsWith(RutaAlta, StringComparison.Ordinal));

    public static int Fuentes(ApiLiquidacionDePrueba proveedor) => proveedor.Pedidos.Count(p => p.Uri.EndsWith(RutaFuentes, StringComparison.Ordinal));
}

/// <summary>Doble de Identidad: respuestas fijadas por la prueba y registro de cada llamada (con la compañía de la cabecera).</summary>
internal sealed class IdentidadDePrueba : IClienteIdentidadAlta
{
    private readonly ConcurrentQueue<(Guid Empresa, ReporteEstadoAprovisionamiento Reporte)> _reportes = new();
    private int _datos;
    private int _llaves;

    public DatosAltaProveedor Datos { get; set; } = new("Flota Andina SAS", "900123456", EstadoNitAlta.Valido, "ops@flota.example", true);

    public LlaveDiagnostico Llave { get; set; } = new(false, null, 0);

    /// <summary>Si se da, la llamada del tipo lanza <see cref="IdentidadNoDisponibleException"/>.</summary>
    public bool DatosCaido { get; set; }

    public bool LlaveCaida { get; set; }

    public bool ReporteCaido { get; set; }

    /// <summary>Respuesta del reporte; por defecto aplicado con la versión reportada.</summary>
    public Func<ReporteEstadoAprovisionamiento, RespuestaReporte> Responder { get; set; } = r => new RespuestaReporte(true, r.Version);

    public int LlamadasDatos => _datos;

    public int LlamadasLlave => _llaves;

    public IReadOnlyList<(Guid Empresa, ReporteEstadoAprovisionamiento Reporte)> Reportes => [.. _reportes];

    public Task<DatosAltaProveedor> ObtenerDatosAltaAsync(Guid empresa, CancellationToken ct)
    {
        Interlocked.Increment(ref _datos);
        return DatosCaido ? throw new IdentidadNoDisponibleException("caída de prueba") : Task.FromResult(Datos);
    }

    public Task<LlaveDiagnostico> ObtenerLlaveDiagnosticoAsync(Guid empresa, CancellationToken ct)
    {
        Interlocked.Increment(ref _llaves);
        return LlaveCaida ? throw new IdentidadNoDisponibleException("caída de prueba") : Task.FromResult(Llave);
    }

    public Task<RespuestaReporte> ReportarAsync(Guid empresa, ReporteEstadoAprovisionamiento reporte, CancellationToken ct)
    {
        if (ReporteCaido)
            throw new IdentidadNoDisponibleException("caída de prueba");
        _reportes.Enqueue((empresa, reporte));
        return Task.FromResult(Responder(reporte));
    }
}
