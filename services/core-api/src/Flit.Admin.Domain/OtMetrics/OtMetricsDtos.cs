namespace Flit.Admin.Domain.OtMetrics;

/// <summary>
/// Filtros comunes de los reportes del organismo. El rango aplica a los HECHOS del periodo
/// (entregas, decisiones); el panel de cola describe el estado ACTUAL y no lo usa.
/// </summary>
public sealed record OtMetricsFilter(
    DateOnly From,
    DateOnly To,
    string? Modalidad = null,
    Guid? ClientTenantId = null);

// ── A.1 Panel operativo ───────────────────────────────────────────────────────────────────────

/// <summary>Movimiento del día (día calendario America/Bogotá).</summary>
public sealed record OtDayMovementDto(
    int EntregadosHoy,
    int DecididosHoy,
    int PendientesTotal,
    double? TiempoMedianoDecisionHoras);

/// <summary>
/// En qué espera cada trámite pendiente. Solo se exponen las esperas del ORGANISMO, que son sobre
/// las que puede actuar.
/// <para><see cref="EnEsperaDelCliente"/> no se desglosa: es la diferencia entre
/// <c>PendientesTotal</c> y lo accionable por el organismo (trámites esperando SOAT del cliente o
/// pausados). Se devuelve como un único número para que el panel pueda explicar honestamente por
/// qué el desglose no suma el total, y para poder decidir más adelante si se excluyen del conteo.</para>
/// </summary>
public sealed record OtQueueBreakdownDto(
    int PorRevisar,
    int EsperandoAsignarPlaca,
    int EnEsperaDelCliente);

/// <summary>Antigüedad de lo pendiente, en días desde que entró a <c>entregado</c>.</summary>
public sealed record OtAgingDto(
    int Hasta1Dia,
    int Entre2y3Dias,
    int Entre4y7Dias,
    int MasDe7Dias,
    int PrioritariosEstancados);

public sealed record OtOperationalPanelDto(
    OtDayMovementDto Movimiento,
    OtQueueBreakdownDto Cola,
    OtAgingDto Antiguedad);

// ── A.2 Desempeño ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Un revisor del organismo y su desempeño en el periodo. Volumen SIEMPRE acompañado de calidad:
/// el conteo por sí solo premia a quien decide rápido y mal.
/// </summary>
public sealed record OtReviewerDto(
    Guid UserId,
    string DisplayName,
    int Decididos,
    int Aprobados,
    double AprobacionPct,
    int Rechazados,
    double RechazoPct,
    double? TiempoMedianoHoras,
    double VuelvenARechazarsePct);

/// <summary>
/// Calidad de lo que envía cada empresa cliente.
/// <para><see cref="PasanPrimeraPct"/> se calcula sobre los trámites APROBADOS en el periodo: de
/// los que llegaron a buen puerto, cuántos lo hicieron sin ningún rechazo previo. Medirlo sobre
/// todos los entregados castigaría a la empresa por trámites que aún están en revisión.</para>
/// </summary>
public sealed record OtClientCompanyQualityDto(
    Guid TenantId,
    string Name,
    int Entregados,
    /// <summary>
    /// Base sobre la que se calcula <see cref="PasanPrimeraPct"/>. Se expone para que la consola
    /// pueda mostrar «—» en vez de un «100 %» sin base: una empresa con cero aprobados en el
    /// periodo no «pasa el 100 % a la primera», simplemente no hay nada que medir.
    /// </summary>
    int Aprobados,
    double PasanPrimeraPct,
    double DevolucionesPromedio);

public sealed record OtPerformanceDto(
    IReadOnlyList<OtReviewerDto> Revisores,
    IReadOnlyList<OtClientCompanyQualityDto> Empresas);

// ── A.3 Motivos de rechazo ────────────────────────────────────────────────────────────────────

/// <summary>
/// Una causal y su peso.
/// <para><see cref="Pct"/> es el porcentaje de RECHAZOS que incluyen esta causal, no el reparto de
/// un total: como un rechazo puede llevar varias causales, la suma de los porcentajes puede pasar
/// del 100 %. Decirlo importa: leído como reparto, «31 %» se entiende mal.</para>
/// </summary>
public sealed record OtRejectionReasonStatDto(
    Guid ReasonId,
    string Code,
    string Description,
    int Rechazos,
    double Pct);

/// <summary>
/// Motivos de rechazo del periodo.
/// <para><see cref="PromedioCausalesPorRechazo"/> es el indicador de salud del dato: si se acerca
/// al tamaño del catálogo, alguien está marcando todo y el reporte deja de discriminar. En FLIT 1
/// ese número era 8,97 sobre un catálogo de 9 y por eso su distribución salía plana.</para>
/// </summary>
public sealed record OtRejectionReasonsDto(
    IReadOnlyList<OtRejectionReasonStatDto> Causales,
    int TotalRechazos,
    int RechazosSinCausal,
    double PromedioCausalesPorRechazo);

// ── Drill-down: de qué está hecho cada número ─────────────────────────────────────────────────

/// <summary>
/// Bloques del panel que se pueden abrir. Son los MISMOS predicados con los que se cuenta cada
/// tarjeta: si el drill-down recalculara por su cuenta, la lista y el número dejarían de cuadrar
/// y el reporte perdería toda credibilidad.
/// </summary>
public static class OtDrilldownBuckets
{
    public const string EntregadosHoy = "entregados_hoy";
    public const string DecididosHoy = "decididos_hoy";
    public const string Pendientes = "pendientes";
    public const string PorRevisar = "por_revisar";
    public const string EsperandoPlaca = "esperando_placa";
    public const string EnEsperaDelCliente = "espera_cliente";
    public const string Hasta1Dia = "antiguedad_0_1";
    public const string Entre2y3Dias = "antiguedad_2_3";
    public const string Entre4y7Dias = "antiguedad_4_7";
    public const string MasDe7Dias = "antiguedad_mas_7";
    public const string PrioritariosEstancados = "prioritarios_estancados";

    public static bool IsKnown(string? bucket) => bucket is
        EntregadosHoy or DecididosHoy or Pendientes or PorRevisar or EsperandoPlaca
        or EnEsperaDelCliente or Hasta1Dia or Entre2y3Dias or Entre4y7Dias or MasDe7Dias
        or PrioritariosEstancados;
}

/// <summary>Un trámite dentro de un bloque, con lo justo para reconocerlo y poder ir a decidirlo.</summary>
public sealed record OtDrilldownItemDto(
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string? Placa,
    string? Vin,
    Guid ClientTenantId,
    string ClientTenantName,
    string Status,
    string? ModalidadEntrada,
    bool Prioritario,
    double? DiasEsperando);

/// <summary>
/// Contenido de un bloque.
/// <para><see cref="Total"/> es el conteo completo y <see cref="Items"/> puede venir recortado:
/// <see cref="Omitidos"/> lo dice explícitamente para que la lista nunca aparente ser todo lo que
/// hay.</para>
/// </summary>
public sealed record OtDrilldownDto(
    string Bucket,
    int Total,
    int Omitidos,
    IReadOnlyList<OtDrilldownItemDto> Items);

/// <summary>Empresa cliente del organismo, para el filtro del reporte.</summary>
public sealed record OtClientCompanyOptionDto(Guid TenantId, string Name);

// ── B. Informe del periodo ────────────────────────────────────────────────────────────────────

/// <summary>
/// Estados del informe. NO son los estados crudos de <c>procedure_instances</c>: son la lectura del
/// trámite <b>desde el organismo</b>. Los estados anteriores a la radicación (borrador, preparado)
/// no aparecen porque el organismo nunca los vio, y <c>rechazado</c> se parte en dos según si el
/// gestor ya abrió la subsanación — que para el organismo son dos situaciones distintas: una vuelve
/// y la otra no.
/// <para>Los buckets son excluyentes y exhaustivos: su suma es SIEMPRE el total del informe. Es una
/// diferencia deliberada con el panel operativo, cuyo desglose no cierra porque solo expone lo
/// accionable.</para>
/// </summary>
public static class OtReportEstado
{
    public const string EnRevision = "en_revision";
    public const string EsperandoPlaca = "esperando_placa";
    public const string EsperandoCliente = "esperando_cliente";
    public const string Aprobado = "aprobado";
    public const string EnSubsanacion = "en_subsanacion";
    public const string Rechazado = "rechazado";
    public const string Anulado = "anulado";

    /// <summary>Radicado y de vuelta en un estado previo a la radicación: no debería ocurrir, pero si ocurre se ve.</summary>
    public const string Otro = "otro";
}

/// <summary>Granularidad de la serie temporal del informe. La elige el servidor según el ancho del rango.</summary>
public static class OtReportGranularity
{
    public const string Dia = "dia";
    public const string Semana = "semana";
    public const string Mes = "mes";
}

/// <summary>Topes del informe.</summary>
public static class OtReportLimits
{
    /// <summary>Filas por página. Suficiente para una jornada de trabajo sin volcar el histórico en una respuesta.</summary>
    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 50;
}

/// <summary>Un tramo de la distribución de tiempos. El histograma que la tabla no puede contar.</summary>
public sealed record OtReportTimeBucketDto(string Key, string Label, int Tramites);

/// <summary>
/// Un punto de la serie temporal del informe.
/// <para>La serie se emite COMPLETA, con los periodos sin actividad en cero. Omitirlos deja una
/// gráfica que miente por partida doble: los huecos se leen como continuidad y una sola quincena
/// activa se dibuja como un punto suelto sin línea.</para>
/// </summary>
public sealed record OtReportSeriesPointDto(
    string Bucket,
    string Label,
    int Radicados,
    int Aprobados,
    int Rechazados);

/// <summary>
/// Cabecera del informe.
/// <para>Los tiempos se miden desde la ÚLTIMA radicación hasta la decisión: es el turno que el
/// organismo realmente trabajó. Medir desde la primera radicación mezclaría en el mismo número el
/// tiempo del organismo y el que el gestor tardó en subsanar.</para>
/// <para>Se acompañan de mediana, promedio y p90 a la vez a propósito: la mediana dice el caso
/// típico, el promedio se desplaza con los casos extremos y el p90 es el compromiso que el
/// organismo puede sostener. Solo la mediana escondería la cola.</para>
/// </summary>
public sealed record OtReportSummaryDto(
    int Total,
    int EnRevision,
    int EsperandoPlaca,
    int EsperandoCliente,
    int Aprobados,
    int EnSubsanacion,
    int Rechazados,
    int Anulados,
    int Otros,
    int Decididos,
    int Devoluciones,
    double DevolucionesPromedio,
    double? TiempoMedianoHoras,
    double? TiempoPromedioHoras,
    double? TiempoP90Horas,
    double? TiempoMedianoAprobacionHoras,
    IReadOnlyList<OtReportTimeBucketDto> DistribucionTiempos,
    string Granularidad,
    IReadOnlyList<OtReportSeriesPointDto> Serie);

/// <summary>
/// Una fila del informe: un trámite con TODO lo que alguna columna pueda pedir.
/// <para>Se devuelven todos los campos aunque la consola muestre pocos: la selección de columnas es
/// del usuario y cambia sin recargar, así que recortarla en el servidor obligaría a volver a
/// consultar cada vez que alguien marca una casilla.</para>
/// </summary>
public sealed record OtReportRowDto(
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string? Placa,
    string? Vin,
    Guid ClientTenantId,
    string ClientTenantName,
    string Modalidad,
    string Status,
    string EstadoOt,
    bool Prioritario,
    bool SubsanacionActiva,
    DateTimeOffset RadicadoEn,
    DateTimeOffset? UltimaRadicacionEn,
    DateTimeOffset? DecididoEn,
    string? DecididoPor,
    double? HorasHastaDecision,
    double? DiasEnOrganismo,
    int Devoluciones,
    IReadOnlyList<string> CausalesUltimoRechazo);

/// <summary>Campos por los que se puede ordenar el informe. Lista cerrada: un campo libre sería inyección de orden.</summary>
public static class OtReportSort
{
    public const string Radicado = "radicado";
    public const string Decidido = "decidido";
    public const string Dias = "dias";
    public const string Empresa = "empresa";
    public const string Referencia = "referencia";
    public const string Devoluciones = "devoluciones";
    public const string Estado = "estado";

    public static bool IsKnown(string? sort) => sort is
        Radicado or Decidido or Dias or Empresa or Referencia or Devoluciones or Estado;
}

/// <summary>Parámetros del informe: el filtro común más paginación y orden.</summary>
public sealed record OtReportQuery(
    OtMetricsFilter Filter,
    int Page,
    int PageSize,
    string? SortBy,
    bool Descending);

/// <summary>
/// El informe: cabecera calculada sobre el universo COMPLETO y una página de filas.
/// <para><see cref="Total"/> es el universo, no las filas devueltas: el resumen nunca describe solo
/// la página que se está viendo.</para>
/// </summary>
public sealed record OtReportDto(
    OtReportSummaryDto Resumen,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<OtReportRowDto> Filas);
