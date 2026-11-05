using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>
/// Lo que queda en la fila después de una toma. No incluye el veredicto: el redactor no lo cambia. <see cref="InvalidarPdf"/>
/// solo cuando el texto pasa a <c>COMPLETADA</c>.
/// </summary>
internal readonly record struct CierreDeRedaccion(
    string Alertamientos,
    string RedactadoPor,
    string EstadoRedaccion,
    DateTimeOffset? DisponibleDesde,
    DateTimeOffset? LeaseHasta,
    bool InvalidarPdf);

/// <summary>
/// Redacta una fila ya tomada, fuera de la transacción del cierre (corte 6). Abre un contexto con la empresa de la fila y
/// desde ahí el filtro manda. No vuelve a evaluar. El veredicto guardado no se toca. El <c>UPDATE</c> solo se guarda si el
/// lease sigue siendo el de la toma; si otra réplica ya la recuperó, el texto nuevo se descarta. Al completar, el PDF vigente
/// de ese vehículo pasa a no vigente en la misma transacción. No toca el Excel ni el ZIP y no genera otro PDF.
/// </summary>
internal sealed partial class RedactorEnCola(
    IServiceScopeFactory scopes, IOptions<OpcionesRedaccion> opciones, TimeProvider reloj, ILogger<RedactorEnCola> log)
{
    private const string CodigoImpuesto = "IMPUESTO";

    private static readonly JsonSerializerOptions Json = new();

    /// <summary>
    /// Qué hacer con la fila tomada. <paramref name="resultado"/> null: no se llamó al modelo (suspensión o proveedor apagado).
    /// Un fallo técnico agenda <see cref="EmisorDictamen.DisponibleDesde"/> salvo que <paramref name="intentos"/> ya pasó de
    /// <paramref name="maxReintentos"/>. Contradicción agotada, <see cref="TipoFalloRedactor.NoConfigurado"/> e
    /// <see cref="TipoFalloRedactor.Ilegible"/> no se reintentan. No hay estado ERROR.
    /// </summary>
    public static CierreDeRedaccion Decidir(
        string veredicto, string alertamientos, int intentos, ResultadoRedaccion? resultado, int maxReintentos, DateTimeOffset ahora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(veredicto);
        ArgumentNullException.ThrowIfNull(alertamientos);
        if (resultado is { EstadoRedaccion: ColaRedaccion.Completada, RedactadoPor: "LLM" })
        {
            return new CierreDeRedaccion(ConTexto(alertamientos, resultado.Texto), "LLM", ColaRedaccion.Completada, null, null, true);
        }

        var desde = intentos > maxReintentos || resultado is null ? null : EmisorDictamen.DisponibleDesde(resultado, ahora);
        return new CierreDeRedaccion(alertamientos, "PLANTILLA", ColaRedaccion.Pendiente, desde, null, false);
    }

    /// <summary>
    /// Rearma el pedido con el veredicto de la fila, las reglas de <paramref name="alertamientos"/> en ese orden y el bien.
    /// <paramref name="montoImpuesto"/> solo entra en la regla <c>IMPUESTO</c>; el resto queda null.
    /// </summary>
    public static PedidoRedaccion Rearmar(string veredicto, string alertamientos, DatosBien bien, bool suspendida, long? montoImpuesto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(veredicto);
        ArgumentNullException.ThrowIfNull(alertamientos);
        ArgumentNullException.ThrowIfNull(bien);
        using var documento = JsonDocument.Parse(alertamientos);
        var reglas = new List<ReglaDisparada>();
        foreach (var item in documento.RootElement.EnumerateArray())
        {
            var codigo = item.GetProperty("codigo").GetString() ?? string.Empty;
            var fuente = item.GetProperty("fuente").GetString() ?? string.Empty;
            var efecto = item.GetProperty("efecto").GetString() ?? string.Empty;
            reglas.Add(new ReglaDisparada(codigo, fuente, efecto, codigo == CodigoImpuesto ? montoImpuesto : null));
        }

        return new PedidoRedaccion(veredicto, reglas, bien, suspendida);
    }

    /// <summary>El texto del modelo en cada alertamiento, en el mismo orden. Conserva código, efecto y fuente.</summary>
    public static string ConTexto(string alertamientos, string texto)
    {
        ArgumentNullException.ThrowIfNull(alertamientos);
        ArgumentNullException.ThrowIfNull(texto);
        using var documento = JsonDocument.Parse(alertamientos);
        var alertas = new List<AlertaJson>();
        foreach (var item in documento.RootElement.EnumerateArray())
        {
            alertas.Add(new AlertaJson(
                item.GetProperty("codigo").GetString() ?? string.Empty,
                texto,
                item.GetProperty("efecto").GetString() ?? string.Empty,
                item.GetProperty("fuente").GetString() ?? string.Empty));
        }

        return JsonSerializer.Serialize(alertas, Json);
    }

    /// <summary>
    /// Marca no vigente el PDF vigente de <paramref name="vehiculoId"/>. No borra, no toca el Excel ni el ZIP. Sin PDF no hace nada.
    /// </summary>
    public static void InvalidarPdfVigente(IEnumerable<Evidencia> evidencias, Guid vehiculoId)
    {
        ArgumentNullException.ThrowIfNull(evidencias);
        foreach (var evidencia in evidencias)
        {
            if (evidencia.Tipo == TipoEvidencia.Pdf && evidencia.Vigente && evidencia.VehiculoId == vehiculoId)
                evidencia.Vigente = false;
        }
    }

    /// <summary>Toma la siguiente redacción y la cierra; false si la cola no tenía nada tomable.</summary>
    public async Task<bool> ProcesarSiguienteAsync(CancellationToken ct)
    {
        RedaccionTomada? tomada;
        await using (var scope = scopes.CreateAsyncScope())
        {
            tomada = await scope.ServiceProvider.GetRequiredService<ColaRedaccion>()
                .TomarAsync(opciones.Value.DuracionLease, ct).ConfigureAwait(false);
        }

        if (tomada is null)
            return false;
        await AplicarAsync(tomada, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Cierra la toma <paramref name="tomada"/>. Si el lease ya no es el suyo, no escribe.</summary>
    internal async Task AplicarAsync(RedaccionTomada tomada, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var servicios = scope.ServiceProvider;
        await using var db = new DiagnosticoDb(
            servicios.GetRequiredService<DbContextOptions<DiagnosticoDb>>(), new EmpresaDeLaFila(tomada.TenantId));
        var fila = await db.Set<DictamenVehiculo>().SingleOrDefaultAsync(d => d.Id == tomada.Id, ct).ConfigureAwait(false);
        if (fila is null || fila.RedaccionLeaseHasta != tomada.LeaseHasta)
        {
            LeasePerdido(log, tomada.Id);
            return;
        }

        var suspendida = await db.Set<ConfiguracionEmpresa>()
            .Select(c => (bool?)c.RedaccionIaSuspendida)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? false;
        var proveedor = servicios.GetRequiredService<IProveedorRedactor>();
        ResultadoRedaccion? resultado = null;
        if (!suspendida && proveedor.Configurado)
        {
            var vehiculo = await db.Set<VehiculoDiagnostico>().AsNoTracking()
                .SingleAsync(v => v.Id == fila.VehiculoId, ct).ConfigureAwait(false);
            var celdas = await db.Set<CeldaConsulta>().AsNoTracking()
                .Where(c => c.VehiculoId == fila.VehiculoId)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var runt = celdas.FirstOrDefault(c => c.Fuente == Fuente.Runt);
            var protector = servicios.GetRequiredService<IProtectorRespuestaFuente>();
            var detalle = runt is null ? null : LectorDetalleRunt.Leer(protector, runt.RespuestaFuenteCifrada);
            var pedido = Rearmar(
                fila.Veredicto,
                fila.Alertamientos,
                EmisorDictamen.BienDe(detalle, vehiculo.Placa, vehiculo.Servicio, vehiculo.IdentificadorNorm),
                suspendida,
                MontoImpuesto(celdas));
            resultado = await servicios.GetRequiredService<ServicioRedactor>().RedactarAsync(pedido, ct).ConfigureAwait(false);
        }

        var cierre = Decidir(fila.Veredicto, fila.Alertamientos, tomada.Intentos, resultado, opciones.Value.MaxReintentos, reloj.GetUtcNow());
        var veredicto = fila.Veredicto;
        fila.Alertamientos = cierre.Alertamientos;
        fila.RedactadoPor = cierre.RedactadoPor;
        fila.EstadoRedaccion = cierre.EstadoRedaccion;
        fila.RedaccionDisponibleDesde = cierre.DisponibleDesde;
        fila.RedaccionLeaseHasta = cierre.LeaseHasta;
        if (cierre.InvalidarPdf)
        {
            var pdfs = await db.Set<Evidencia>()
                .Where(e => e.VehiculoId == fila.VehiculoId && e.Tipo == TipoEvidencia.Pdf && e.Vigente)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            InvalidarPdfVigente(pdfs, fila.VehiculoId);
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            LeasePerdido(log, tomada.Id);
            return;
        }

        Cerrada(log, tomada.Id, veredicto, fila.EstadoRedaccion, tomada.Intentos);
    }

    private static long? MontoImpuesto(IReadOnlyList<CeldaConsulta> celdas)
    {
        var impuesto = celdas.FirstOrDefault(c => c.Fuente == Fuente.ImpuestoDepartamental);
        if (impuesto is not { Estado: EstadoCelda.Completado }
            || ProyectorDetalleCelda.ResultadoVehiculo(Fuente.ImpuestoDepartamental, impuesto.Payload) is not ResultadoImpuestoDetalle detalle)
            return null;
        return detalle.MontoTotal;
    }

    [LoggerMessage(EventId = 126786, Level = LogLevel.Information, Message = "La redacción {DictamenId} quedó {EstadoRedaccion} ({Veredicto}) en el intento {Intentos}")]
    private static partial void Cerrada(ILogger logger, Guid dictamenId, string veredicto, string estadoRedaccion, int intentos);

    [LoggerMessage(EventId = 126787, Level = LogLevel.Information, Message = "La redacción {DictamenId} perdió el lease; el texto no se guarda")]
    private static partial void LeasePerdido(ILogger logger, Guid dictamenId);

    /// <summary>La empresa de la fila: el contexto filtra y valida con ella, como una petición con su token.</summary>
    private sealed class EmpresaDeLaFila(Guid empresa) : IPlatformTenantAccessor
    {
        public PlatformTenantContext Current { get; } = new(empresa, IsSuperAdmin: false, IsService: true);
    }

    private sealed record AlertaJson(
        [property: JsonPropertyName("codigo")] string Codigo,
        [property: JsonPropertyName("texto")] string Texto,
        [property: JsonPropertyName("efecto")] string Efecto,
        [property: JsonPropertyName("fuente")] string Fuente);
}
