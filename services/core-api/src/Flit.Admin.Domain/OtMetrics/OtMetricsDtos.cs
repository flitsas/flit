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
