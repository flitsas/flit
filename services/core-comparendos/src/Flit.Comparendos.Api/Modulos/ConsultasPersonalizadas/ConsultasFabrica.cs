using System.Text.Json;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Queries.Domain;
using Microsoft.EntityFrameworkCore;
using C = Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas.ComparendoQueryFieldCatalog;

namespace Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas;

/// <summary>
/// Las consultas de fábrica (CONTRATO §5): las predefinidas de V1, sin empresa ni usuario, que nadie edita ni borra.
///
/// <para><b>El rango.</b> El motor solo guarda rangos con nombre de hasta 90 días o un rango propio. Las de seguimiento
/// miran toda la historia, así que van con un rango propio desde 2015 y sin fin (llega hasta hoy). No explota el tope en
/// Renting porque, sin identificadores, sus condiciones (activo, pagado, estado de gestión…) se empujan a la base. La
/// única sin condiciones, «Seguimiento completo», abre con los últimos 90 días: toda la historia de Renting no cabe.</para>
///
/// <para>Las columnas son los nombres de <see cref="ComparendoQueryRow"/> en camelCase.</para>
/// </summary>
public static class ConsultasFabrica
{
    public static readonly Guid CaducanPronto = new("c0000000-0000-4000-8000-000000000001");
    public static readonly Guid Descuento50 = new("c0000000-0000-4000-8000-000000000002");
    public static readonly Guid SinGestion = new("c0000000-0000-4000-8000-000000000003");
    public static readonly Guid DpSinRespuesta = new("c0000000-0000-4000-8000-000000000004");
    public static readonly Guid SeguimientoRadicados = new("c0000000-0000-4000-8000-000000000005");
    public static readonly Guid InfraccionesC35D02 = new("c0000000-0000-4000-8000-000000000006");
    public static readonly Guid InfraccionesC02C29D04C14 = new("c0000000-0000-4000-8000-000000000007");
    public static readonly Guid SeguimientoCompleto = new("c0000000-0000-4000-8000-000000000008");

    private static readonly DateOnly Origen = new(2015, 1, 1);

    private static QueryDateFilter Historia(string campo) => new(campo, QueryRangePreset.Personalizado, Origen, null);

    private static QueryCondition Es(string campo, params string[] valores) => new(campo, QueryOperator.EsAlguno, valores);

    private static readonly string[] Basicas = ["numero", "placa", "nitMonitoreado", "codigoInfraccion", "organismoNombre", "fechaImposicion", "valorTotal"];

    private static readonly string[] Plazos =
        ["numero", "placa", "codigoInfraccion", "organismoNombre", "fechaNotificacion", "estadoLegal", "fechaLimiteGestion", "fechaCaducidad", "fechaPrescripcion", "valorTotal"];

    private static readonly string[] ColumnasDp =
        ["numero", "placa", "organismoNombre", "valorTotal", "dpRadicado", "dpTipologia", "dpEstado", "dpFechaRadicacion", "dpSentido", "dpFechaRespuesta", "dpEnvioAutomatico"];

    /// <summary>«Seguimiento Comparendos» de V1, con las columnas que existen en V2.</summary>
    private static readonly string[] Seguimiento =
    [
        "numero", "placa", "nitMonitoreado", "titular", "codigoInfraccion", "descripcionInfraccion", "esFotomulta", "tipoVehiculo",
        "servicioVehiculo", "organismoNombre", "departamento", "municipio", "fechaImposicion", "fechaNotificacion", "fechaIngreso",
        "fechaLimiteGestion", "fechaFinDescuento50", "fechaFinDescuento25", "fechaCaducidad", "fechaPrescripcion", "valorTotal",
        "estadoLegal", "ventanaDescuento", "estadoGestion", "dpRadicado", "dpEstado", "dpSentido", "estadoNotificacionConductor",
        "estadoCambioContraventor", "pagoTipo",
    ];

    /// <summary>En el orden en que se ofrecen.</summary>
    public static IReadOnlyList<(Guid Id, string Nombre, string Descripcion, QueryDefinition Definicion)> Todas { get; } =
    [
        (CaducanPronto, "Caducan o prescriben en 30 días", "Comparendos activos ordenados por su fecha límite de gestión, el más urgente primero.",
            new(Historia(C.FechaNotificacion), [Es(C.Activo, "true")], Plazos, C.OrdenFechaLimiteGestion, Descending: false)),
        (Descuento50, "Con descuento del 50 % vigente", "Sin pagar y todavía con el 50 % de descuento.",
            new(Historia(C.FechaImposicion), [Es(C.VentanaDescuento, "Descuento50"), Es(C.Pagado, "false")], [.. Basicas, "fechaFinDescuento50"], C.OrdenFecha)),
        (SinGestion, "Sin gestión", "Activos, sin pagar y sin ninguna gestión.",
            new(Historia(C.FechaImposicion), [Es(C.EstadoGestion, "SinGestion"), Es(C.Activo, "true"), Es(C.Pagado, "false")], [.. Basicas, "estadoLegal"], C.OrdenFecha)),
        (DpSinRespuesta, "DP sin respuesta", "Derechos de petición enviados que el organismo no ha respondido.",
            new(Historia(C.FechaDpRadicacion), [Es(C.DpEstado, "Enviado")], ColumnasDp, C.OrdenFecha)),
        (SeguimientoRadicados, "Seguimiento de radicados", "Todos los comparendos con derecho de petición y su estado.",
            new(Historia(C.FechaImposicion), [Es(C.TieneDp, "true")], ColumnasDp, C.OrdenFecha)),
        (InfraccionesC35D02, "Infracciones C35 y D02", "Comparendos con los códigos C35 o D02.",
            new(Historia(C.FechaImposicion), [Es(C.CodigoInfraccion, "C35", "D02")], Basicas, C.OrdenFecha)),
        (InfraccionesC02C29D04C14, "Infracciones C02, C29, D04 y C14", "Comparendos con los códigos C02, C29, D04 o C14.",
            new(Historia(C.FechaImposicion), [Es(C.CodigoInfraccion, "C02", "C29", "D04", "C14")], Basicas, C.OrdenFecha)),
        (SeguimientoCompleto, "Seguimiento completo", "Todas las columnas de seguimiento de cada comparendo.",
            new(new QueryDateFilter(C.FechaImposicion, QueryRangePreset.Ultimos90), [], Seguimiento, C.OrdenFecha)),
    ];

    public static bool EsDeFabrica(Guid id) => Todas.Any(q => q.Id == id);
}

/// <summary>
/// Siembra idempotente de las consultas de fábrica (corre en cada arranque desde <c>Semillas.SembrarAsync</c>): agrega
/// las que faltan por id y deja como están las que ya existen.
/// </summary>
public static class SiembraConsultasFabrica
{
    public static async Task SembrarAsync(ComparendosDb db, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var ids = ConsultasFabrica.Todas.Select(q => q.Id).ToList();
        var existentes = await db.ConsultasGuardadas.Where(q => ids.Contains(q.Id)).Select(q => q.Id).ToListAsync(ct).ConfigureAwait(false);
        var orden = 0;
        foreach (var (id, nombre, descripcion, definicion) in ConsultasFabrica.Todas)
        {
            // El orden de oferta sale de creado_en: un segundo por posición.
            var creado = ahora.AddSeconds(orden++);
            if (existentes.Contains(id))
                continue;
            db.ConsultasGuardadas.Add(new ConsultaGuardada
            {
                Id = id,
                Nombre = nombre,
                Descripcion = descripcion,
                DeFabrica = true,
                Definicion = JsonSerializer.Serialize(QueryNormalizer.Normalize(C.Instance, definicion), RepositorioConsultasGuardadas.Json),
                CreadoEn = creado,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
