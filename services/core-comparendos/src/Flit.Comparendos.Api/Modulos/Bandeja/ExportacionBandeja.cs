using System.Globalization;
using System.Text.Json;
using Flit.Comparendos.Api.Infraestructura.Archivos;
using Flit.Comparendos.Api.Infraestructura.Excel;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;
using Cell = Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;

namespace Flit.Comparendos.Api.Modulos.Bandeja;

/// <summary>
/// Exportación de la bandeja (§6.20, RN-184): exactamente lo filtrado, sin paginación, con el estado legal calculado a la
/// fecha de generación. Hasta 5.000 filas se descarga directo; más, se genera en segundo plano y llega un correo con el
/// enlace («Mis exportaciones», vigente 7 días, S-27).
/// </summary>
public sealed class ExportacionBandeja(ConsultaBandeja consulta)
{
    public const int LimiteDirecto = 5000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly (string Titulo, int Ancho)[] Columnas =
    [
        ("Número", 22), ("Placa", 10), ("NIT consultado", 14), ("Titular", 30), ("Organismo de tránsito", 34), ("Fecha imposición", 13), ("Fecha notificación", 13),
        ("Valor total", 14), ("Fuente", 11), ("Estado legal", 13), ("Días hábiles restantes", 10), ("Ventana de descuento", 14), ("Fin del descuento", 13),
        ("Estado del proceso", 18), ("Contraventor", 28), ("Último aviso", 12), ("Cambio de contraventor", 16), ("Radicado DP", 18), ("Resultado DP", 14),
        ("Pagado", 8), ("Activo", 8), ("Cobro coactivo", 10),
    ];

    public async Task<byte[]> GenerarAsync(Guid? tenant, FiltrosBandeja filtros, CancellationToken ct)
    {
        var h = await consulta.HoyAsync(ct).ConfigureAwait(false);
        var q = ConsultaBandeja.Ordenar(consulta.Filtrar(tenant, filtros, h), filtros.Orden, filtros.Descendente, h.Dia);
        var filas = await consulta.ProyectarAsync(q, h, ct).ConfigureAwait(false);
        var hoja = new TabularWorkbookWriter.Sheet($"Comparendos {h.Dia:yyyy-MM-dd}",
            Columnas.Select(c => new TabularWorkbookWriter.Column(c.Titulo, c.Ancho)).ToList(),
            filas.Select(f => (IReadOnlyList<Cell>)
            [
                Cell.Of(f.Numero), Cell.Of(f.Placa), Cell.Of(f.NitConsultado), Cell.Of(f.Titular), Cell.Of(f.Organismo ?? f.SecretariaTexto),
                Fecha(f.FechaImposicion), Fecha(f.FechaNotificacion), f.ValorTotal is { } v ? Cell.Of((double)v) : Cell.Empty, Cell.Of(f.Fuente),
                Cell.Of(f.EstadoLegal), f.DiasHabilesRestantes is { } d ? Cell.Of(d) : Cell.Empty, Cell.Of(f.VentanaDescuento), Fecha(f.FinDescuentoVigente),
                Cell.Of(f.EstadoProceso), Cell.Of(f.ContraventorNombre), Cell.Of(f.UltimoAviso), Cell.Of(f.CambioContraventor), Cell.Of(f.DpRadicado),
                Cell.Of(f.DpResultado), Cell.Of(f.Pagado ? "Sí" : "No"), Cell.Of(f.Activo ? "Sí" : "No"), Cell.Of(f.EnCobroCoactivo ? "Sí" : "No"),
            ]).ToList());
        return TabularWorkbookWriter.Write([hoja]);
    }

    public static string Serializar(Guid? tenant, FiltrosBandeja filtros, string correo) =>
        JsonSerializer.Serialize(new { tenant, correo, filtros }, Json);

    public static (Guid? Tenant, FiltrosBandeja Filtros, string Correo) Deserializar(string texto)
    {
        using var doc = JsonDocument.Parse(texto);
        var root = doc.RootElement;
        return (root.TryGetProperty("tenant", out var t) && t.ValueKind == JsonValueKind.String ? t.GetGuid() : null,
            root.GetProperty("filtros").Deserialize<FiltrosBandeja>(Json) ?? new FiltrosBandeja(),
            root.TryGetProperty("correo", out var c) ? c.GetString() ?? string.Empty : string.Empty);
    }

    private static Cell Fecha(DateOnly? d) => d is { } x ? Cell.Of(x) : Cell.Empty;
}

/// <summary>Genera en segundo plano las exportaciones grandes y avisa por correo cuando quedan listas.</summary>
internal sealed partial class ExportadorEnSegundoPlano(IServiceScopeFactory scopes, TimeProvider time, ILogger<ExportadorEnSegundoPlano> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await ProcesarSiguienteAsync(stoppingToken).ConfigureAwait(false))
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFallo(logger, ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(15), time, stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> ProcesarSiguienteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var ids = await db.Database.SqlQuery<Guid>($"""
            UPDATE comparendos.exportacion SET estado = 'Generando'
            WHERE id = (SELECT id FROM comparendos.exportacion WHERE estado = 'EnCola' ORDER BY creado_en LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct).ConfigureAwait(false);
        if (ids.Count == 0)
            return false;

        var e = await db.Exportaciones.FirstAsync(x => x.Id == ids[0], ct).ConfigureAwait(false);
        var (tenant, filtros, correo) = ExportacionBandeja.Deserializar(e.Filtros);
        try
        {
            var bytes = await scope.ServiceProvider.GetRequiredService<ExportacionBandeja>().GenerarAsync(tenant, filtros, ct).ConfigureAwait(false);
            var servicio = scope.ServiceProvider.GetRequiredService<ServicioCorreo>();
            e.ArchivoId = await servicio.GuardarAsync(e.TenantId, "exportacion", $"comparendos-{time.GetUtcNow():yyyyMMddHHmm}.xlsx", TabularWorkbookWriter.ContentType, bytes, ct).ConfigureAwait(false);
            e.Estado = EstadoExportacion.Lista;
            e.TerminadoEn = time.GetUtcNow();
            e.ExpiraEn = e.TerminadoEn.Value.AddDays(7);
            if (correo.Length > 0)
            {
                await servicio.RegistrarAsync(new CorreoNuevo
                {
                    TenantId = e.TenantId,
                    Tipo = TipoComunicacion.ExportacionLista,
                    Plantilla = "comparendos.exportacion_lista",
                    Para = [new Destinatario(correo)],
                    Asunto = "Tu exportación de comparendos está lista",
                    Html = HtmlCorreo.Envolver("Exportación lista", $"<p>La exportación de la bandeja que pediste ya está lista.</p><p><a href=\"{servicio.AppUrl}/exportaciones\">Descárgala en «Mis exportaciones»</a>. Estará disponible 7 días.</p>"),
                }, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFallo(logger, ex);
            e.Estado = EstadoExportacion.Fallida;
            e.TerminadoEn = time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló una exportación de la bandeja de Comparendos.")]
    private static partial void LogFallo(ILogger logger, Exception ex);
}
