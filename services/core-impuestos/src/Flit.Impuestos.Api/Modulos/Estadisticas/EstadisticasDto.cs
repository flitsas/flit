using Flit.Impuestos.Api.Modulos.Liquidaciones;

namespace Flit.Impuestos.Api.Modulos.Estadisticas;

/// <summary><c>Dashboard.periodo</c>: el rango leído, extremos incluidos (hora de Colombia).</summary>
public sealed record PeriodoDashboardDto(DateOnly Desde, DateOnly Hasta);

/// <summary><c>Dashboard.contadores</c>: placas liquidadas en el rango (individuales + masivas) y lotes masivos en proceso.</summary>
public sealed record ContadoresDashboardDto(int Total, int Individuales, int Masivas, int LotesEnProceso);

/// <summary><c>Dashboard.resultados</c>: placas por resultado; las pendientes se separan por modo (webhook o consulta, F4a-CF2).</summary>
public sealed record ResultadosDashboardDto(
    int Exitosas, int ConError, int PendientesWebhook, int PendientesConsulta, int DatosIncompletos, int DepartamentoNoHabilitado, int Inciertas, int SinResultado,
    int NoAplica);

/// <summary><c>Dashboard.deuda</c>: placas exitosas con deuda y al día (paz y salvo).</summary>
public sealed record DeudaDashboardDto(int PlacasConDeuda, int PlacasAlDia);

/// <summary><c>Dashboard.serie[]</c> (delta 10 de la 1.1.0): <see cref="Total"/> = <see cref="Individuales"/> + <see cref="Masivas"/>.</summary>
public sealed record PuntoSerieDto(DateOnly Fecha, int Total, int Individuales, int Masivas);

/// <summary><c>Dashboard.porDepartamento[]</c>: <see cref="Monto"/> suma sin los null; null = «Sin dato» (delta 10).</summary>
public sealed record DepartamentoDashboardDto(string Codigo, string Nombre, int Total, decimal? Monto);

/// <summary><c>Dashboard.ranking[]</c>: liquidaciones de cada compañía (solo Super Admin en la vista global).</summary>
public sealed record RankingDashboardDto(ReferenciaDto Empresa, int Total);

/// <summary>
/// Esquema <c>Dashboard</c> (impuestos.v1.yaml 1.1.0, deltas 10 y 13). Sin datos personales. <see cref="MontoTotal"/> null = «Sin
/// dato» (nunca 0); <see cref="Ranking"/> null salvo para el Super Admin en la vista global.
/// </summary>
public sealed record DashboardDto(
    PeriodoDashboardDto Periodo,
    ContadoresDashboardDto Contadores,
    ResultadosDashboardDto Resultados,
    DeudaDashboardDto Deuda,
    decimal? MontoTotal,
    int LiquidacionesSinMonto,
    bool Parcial,
    IReadOnlyList<PuntoSerieDto> Serie,
    IReadOnlyList<DepartamentoDashboardDto> PorDepartamento,
    IReadOnlyList<ResumenLiquidacionDto> UltimosLotes,
    IReadOnlyList<RankingDashboardDto>? Ranking);
