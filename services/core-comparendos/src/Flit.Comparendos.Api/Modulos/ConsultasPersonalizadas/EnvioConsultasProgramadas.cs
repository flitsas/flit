using System.Globalization;
using System.Net;
using System.Text.Json;
using Flit.Comparendos.Api.Infraestructura.Excel;
using Flit.Comparendos.Api.Modulos.Bandeja;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Plantillas;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Queries.Domain;
using Microsoft.EntityFrameworkCore;
using Cell = Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;
using F = Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas.ComparendoQueryFieldCatalog;

namespace Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas;

/// <summary>
/// El envío por correo de las consultas programadas (lo corre <c>ReportesAutomaticos</c> con
/// <see cref="TipoTrabajo.ConsultasProgramadas"/>, cada 15 minutos). Cada programación vencida se ejecuta con el MISMO
/// servicio de <c>/run</c> y con el tenant de la programación, y su Excel va adjunto con el membrete de los cierres.
///
/// <para><b>Una sola vez por ejecución</b>, aunque corran dos réplicas: antes de ejecutar se reclama la programación con
/// un UPDATE condicional sobre <c>proxima_ejecucion</c>. Solo quien lo gana la envía. Si el proceso muere después, ese
/// envío se pierde y no se duplica.</para>
/// </summary>
public sealed class EnvioConsultasProgramadas(ComparendosDb db, ConsultaBandeja bandeja, ServicioCorreo correo, Reloj reloj, IProductosHabilitados productos)
{
    public async Task EjecutarAsync(Guid? soloEmpresa, CancellationToken ct)
    {
        var ahora = reloj.Ahora;
        var vencidas = await db.ConsultasProgramadas.AsNoTracking()
            .Where(p => p.Activa && p.ProximaEjecucion != null && p.ProximaEjecucion <= ahora && (soloEmpresa == null || p.TenantId == soloEmpresa))
            .OrderBy(p => p.ProximaEjecucion).ToListAsync(ct).ConfigureAwait(false);
        foreach (var p in vencidas)
        {
            var nueva = Programaciones.Proxima(p.Periodicidad, p.DiaSemana, p.DiaMes, p.Hora, ahora);
            var vieja = p.ProximaEjecucion;
            var reclamada = await db.ConsultasProgramadas
                .Where(x => x.Id == p.Id && x.ProximaEjecucion == vieja && x.Activa)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProximaEjecucion, nueva), ct).ConfigureAwait(false);
            if (reclamada == 0)
                continue;

            var resultado = await EnviarAsync(p, ct).ConfigureAwait(false);
            await db.ConsultasProgramadas.Where(x => x.Id == p.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UltimaEjecucion, ahora).SetProperty(x => x.UltimoResultado, resultado), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Arma y registra el correo. Devuelve el resultado en términos de negocio (lo lee la empresa).</summary>
    private async Task<string> EnviarAsync(ConsultaProgramada p, CancellationToken ct)
    {
        var consulta = await db.ConsultasGuardadas.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == p.ConsultaId && q.TenantId == p.TenantId && !q.DeFabrica, ct).ConfigureAwait(false);
        if (consulta is null)
            return "No se envió: la consulta guardada ya no existe.";
        var empresa = await db.ConfiguracionesEmpresa.AsNoTracking().FirstOrDefaultAsync(e => e.TenantId == p.TenantId, ct).ConfigureAwait(false);
        if (empresa is not { HasReportesModule: true } || !await productos.ComparendosHabilitadoAsync(p.TenantId, ct).ConfigureAwait(false))
            return "No se envió: la empresa no tiene activo el módulo de reportes.";

        var alcance = new AlcanceConsulta(p.TenantId);
        QueryDefinition? definicion;
        try
        {
            definicion = JsonSerializer.Deserialize<QueryDefinition>(consulta.Definicion, RepositorioConsultasGuardadas.Json);
        }
        catch (JsonException)
        {
            return "No se envió: la consulta guardada está dañada; ábrela y vuelve a guardarla.";
        }

        ComparendoQueryResult r;
        try
        {
            var request = QueryNormalizer.BuildRequest(alcance.Catalogo, definicion, 1, QueryLimits.MaxPageSize);
            r = await new ServicioConsultasPersonalizadas(db, bandeja).EjecutarAsync(alcance, request, ct, todasLasFilas: true).ConfigureAwait(false);
        }
        catch (SuperAdminQueryTooBroadException ex)
        {
            return $"No se envió: la consulta trae demasiados comparendos (unos {Miles(ex.Total)}). Acórtale el rango de fechas o agrégale un filtro.";
        }
        catch (ReglaException ex)
        {
            return $"No se envió: {ex.Message}";
        }

        var nombre = empresa.EmpresaNombre ?? "la empresa";
        var archivo = $"{NombreArchivo(consulta.Nombre)}-{reloj.Hoy:yyyyMMdd}.xlsx";
        var html = Html(consulta.Nombre, nombre, r, empresa.ConjuntoPlantillas);
        await correo.RegistrarAsync(new CorreoNuevo
        {
            TenantId = p.TenantId,
            Tipo = TipoComunicacion.ConsultaProgramada,
            Plantilla = "comparendos.consulta_programada",
            Para = p.Destinatarios.Select(d => new Destinatario(d)).ToList(),
            Asunto = $"{consulta.Nombre} — {nombre} ({Formato.Fecha(r.Desde)} a {Formato.Fecha(r.Hasta)})",
            Html = html,
            Adjuntos = [new AdjuntoCorreo(archivo, TabularWorkbookWriter.ContentType, Excel(definicion, r))],
            RemitenteNombre = "FLIT Comparendos",
            CreadoPor = p.CreadoPor,
        }, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return p.Destinatarios.Count == 1 ? "Enviado a 1 destinatario" : $"Enviado a {p.Destinatarios.Count} destinatarios";
    }

    private string Html(string consulta, string empresa, ComparendoQueryResult r, ConjuntoPlantillas conjunto)
    {
        var flit = conjunto != ConjuntoPlantillas.Renting;
        var cuerpo = $"""
            <p>Adjunto va el resultado de la consulta <b>{WebUtility.HtmlEncode(consulta)}</b> de <b>{WebUtility.HtmlEncode(empresa)}</b>,
            del {Formato.Fecha(r.Desde)} al {Formato.Fecha(r.Hasta)}: {Miles(r.Total)} comparendos.</p>
            {(flit ? HtmlCorreo.Boton("Abrir Reportes", $"{correo.AppUrl}/reportes/consultas") : $"<p><a href=\"{correo.AppUrl}/reportes/consultas\">Abrir Reportes</a></p>")}
            """;
        return HtmlCorreo.DeEmpresa(conjunto, consulta, cuerpo, empresa, $"{correo.AppUrl}/assets/correo");
    }

    /// <summary>Miles con punto, como se escriben en Colombia (12.345), sin depender de la cultura del proceso.</summary>
    private static string Miles(int n) => n.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');

    private static string NombreArchivo(string nombre)
    {
        var limpio = new string(nombre.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
        return limpio.Length == 0 ? "consulta" : limpio[..Math.Min(limpio.Length, 60)];
    }

    // ── Excel ─────────────────────────────────────────────────────────────────────────────────

    private static Cell SiNo(bool? v) => v is null ? Cell.Empty : Cell.Of(v.Value ? "Sí" : "No");

    private static Cell Fecha(DateOnly? d) => d is { } dia ? Cell.Of(dia) : Cell.Empty;

    private static Cell Pesos(decimal? v) => v is { } x ? Cell.Of((double)x) : Cell.Empty;

    private static Cell Etiqueta(string campo, string? valor) => Cell.Of(F.EtiquetaDe(campo, valor));

    /// <summary>Columna del Excel por id de <see cref="ComparendoQueryRow"/> (camelCase): encabezado de negocio y la celda, con ETIQUETAS y no valores.</summary>
    internal static readonly IReadOnlyDictionary<string, (string Encabezado, int Ancho, Func<ComparendoQueryRow, Cell> Celda)> Columnas =
        new Dictionary<string, (string, int, Func<ComparendoQueryRow, Cell>)>(StringComparer.Ordinal)
        {
            ["numero"] = ("No. comparendo", 24, f => Cell.Of(f.Numero)),
            ["placa"] = ("Placa", 10, f => Cell.Of(f.Placa)),
            ["nitMonitoreado"] = ("NIT monitoreado", 16, f => Cell.Of(f.NitMonitoreado)),
            ["titular"] = ("Titular", 30, f => Cell.Of(f.Titular)),
            ["codigoInfraccion"] = ("Código de infracción", 12, f => Cell.Of(f.CodigoInfraccion)),
            ["descripcionInfraccion"] = ("Infracción", 40, f => Cell.Of(f.DescripcionInfraccion)),
            ["esFotomulta"] = ("Fotomulta", 10, f => SiNo(f.EsFotomulta)),
            ["tipoVehiculo"] = ("Tipo del vehículo", 16, f => Cell.Of(f.TipoVehiculo)),
            ["servicioVehiculo"] = ("Servicio del vehículo", 16, f => Cell.Of(f.ServicioVehiculo)),
            ["organismoNombre"] = ("Organismo de tránsito", 34, f => Cell.Of(f.OrganismoNombre)),
            ["departamento"] = ("Departamento", 16, f => Cell.Of(f.Departamento)),
            ["municipio"] = ("Municipio", 16, f => Cell.Of(f.Municipio)),
            ["fechaImposicion"] = ("Fecha de imposición", 14, f => Fecha(f.FechaImposicion)),
            ["fechaNotificacion"] = ("Fecha de notificación", 14, f => Fecha(f.FechaNotificacion)),
            ["fechaIngreso"] = ("Fecha de ingreso", 14, f => Fecha(f.FechaIngreso)),
            ["fechaLimiteGestion"] = ("Límite de gestión", 14, f => Fecha(f.FechaLimiteGestion)),
            ["fechaFinDescuento50"] = ("Fin del descuento del 50 %", 14, f => Fecha(f.FechaFinDescuento50)),
            ["fechaFinDescuento25"] = ("Fin del descuento del 25 %", 14, f => Fecha(f.FechaFinDescuento25)),
            ["fechaCaducidad"] = ("Fecha de caducidad", 14, f => Fecha(f.FechaCaducidad)),
            ["fechaPrescripcion"] = ("Fecha de prescripción", 14, f => Fecha(f.FechaPrescripcion)),
            ["valorCapital"] = ("Valor capital", 14, f => Pesos(f.ValorCapital)),
            ["valorAdicional"] = ("Valor adicional", 14, f => Pesos(f.ValorAdicional)),
            ["valorTotal"] = ("Valor total", 14, f => Pesos(f.ValorTotal)),
            ["activo"] = ("Activo", 8, f => SiNo(f.Activo)),
            ["estadoLegal"] = ("Estado legal", 22, f => Etiqueta(F.EstadoLegal, f.EstadoLegal)),
            ["ventanaDescuento"] = ("Descuento vigente", 24, f => Etiqueta(F.VentanaDescuento, f.VentanaDescuento)),
            ["enCobroCoactivo"] = ("Cobro coactivo", 10, f => SiNo(f.EnCobroCoactivo)),
            ["estadoGestion"] = ("Estado de gestión", 26, f => Etiqueta(F.EstadoGestion, f.EstadoGestion)),
            ["gestionado"] = ("Gestionado", 10, f => SiNo(f.Gestionado)),
            ["pagado"] = ("Pagado", 8, f => SiNo(f.Pagado)),
            ["tieneDp"] = ("Tiene DP", 8, f => SiNo(f.TieneDp)),
            ["dpEstado"] = ("Estado DP", 22, f => Etiqueta(F.DpEstado, f.DpEstado)),
            ["dpRadicado"] = ("Radicado", 24, f => Cell.Of(f.DpRadicado)),
            ["dpTipologia"] = ("Tesis", 30, f => Cell.Of(f.DpTipologia)),
            ["dpEnvioAutomatico"] = ("Enviado automáticamente", 12, f => SiNo(f.DpEnvioAutomatico)),
            ["dpFechaRadicacion"] = ("Fecha de radicación", 14, f => Fecha(f.DpFechaRadicacion)),
            ["dpSentido"] = ("Respuesta del organismo", 24, f => Etiqueta(F.DpSentido, f.DpSentido)),
            ["dpFechaRespuesta"] = ("Fecha de respuesta", 14, f => Fecha(f.DpFechaRespuesta)),
            ["estadoNotificacionConductor"] = ("Avisos al conductor", 18, f => Etiqueta(F.NotificacionConductor, f.EstadoNotificacionConductor)),
            ["notificadoFisicamente"] = ("Notificado físicamente", 12, f => SiNo(f.NotificadoFisicamente)),
            ["estadoCambioContraventor"] = ("Cambio de contraventor", 24, f => Etiqueta(F.CambioContraventor, f.EstadoCambioContraventor)),
            ["estadoConsultaContraventor"] = ("Consulta del contraventor", 30, f => Etiqueta(F.ConsultaContraventor, f.EstadoConsultaContraventor)),
            ["pagoTipo"] = ("Tipo de pago", 22, f => Etiqueta(F.PagoTipo, f.PagoTipo)),
            ["pagoValor"] = ("Valor pagado", 14, f => Pesos(f.PagoValor)),
            ["pagoFecha"] = ("Fecha de pago", 14, f => Fecha(f.PagoFecha)),
        };

    private static readonly string[] PorDefecto = ["numero", "placa", "codigoInfraccion", "organismoNombre", "fechaImposicion", "valorTotal", "estadoLegal", "estadoGestion"];

    /// <summary>
    /// Dos hojas: los comparendos (con autofiltro) y las notas: qué se consultó y qué pasó con cada valor pedido. La nota
    /// de cobertura va en su propia hoja para que no quede dentro del autofiltro de los datos.
    /// </summary>
    internal static byte[] Excel(QueryDefinition? definicion, ComparendoQueryResult r)
    {
        var ids = (definicion?.Columnas ?? []).Where(Columnas.ContainsKey).Distinct().ToList();
        if (ids.Count == 0)
            ids = [.. PorDefecto];
        var datos = new TabularWorkbookWriter.Sheet("Comparendos",
            ids.Select(id => new TabularWorkbookWriter.Column(Columnas[id].Encabezado, Columnas[id].Ancho)).ToList(),
            r.Filas.Select(f => (IReadOnlyList<Cell>)ids.Select(id => Columnas[id].Celda(f)).ToList()).ToList());

        var notas = new List<IReadOnlyList<Cell>>
        {
            new[] { Cell.Of("Periodo"), Cell.Of($"{Formato.Fecha(r.Desde)} a {Formato.Fecha(r.Hasta)}") },
            new[] { Cell.Of("Comparendos"), Cell.Of(r.Total) },
        };
        notas.AddRange(r.Cobertura.Select(c => (IReadOnlyList<Cell>)
            [Cell.Of($"{((IQueryFieldCatalog)F.Instance).LabelOf(c.Campo)} {c.Valor}"), Cell.Of(c.Resultado == QueryCoverageResult.Encontrado ? "Encontrado" : c.Motivo)]));
        var hojaNotas = new TabularWorkbookWriter.Sheet("Notas", [new("Dato", 32), new("Detalle", 70)], notas);
        return TabularWorkbookWriter.Write([datos, hojaNotas]);
    }
}
