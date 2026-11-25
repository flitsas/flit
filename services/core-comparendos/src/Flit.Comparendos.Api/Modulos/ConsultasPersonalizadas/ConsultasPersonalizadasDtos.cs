using Flit.Queries.Domain;

namespace Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas;

/// <summary>
/// Una fila de la consulta (CONTRATO §4). Los estados viajan como el VALOR de la opción del catálogo
/// (<c>/fields</c>); la UI los muestra con la etiqueta de esa opción. <see cref="EmpresaId"/> y
/// <see cref="EmpresaNombre"/> solo vienen para el SuperAdmin con «todas las empresas».
/// </summary>
public sealed record ComparendoQueryRow(
    Guid Id,
    string Numero,
    string? Placa,
    string? NitMonitoreado,
    string? Titular,
    Guid? EmpresaId,
    string? EmpresaNombre,
    string? CodigoInfraccion,
    string? DescripcionInfraccion,
    bool EsFotomulta,
    string? TipoVehiculo,
    string? ServicioVehiculo,
    Guid? OrganismoId,
    string? OrganismoNombre,
    string? Departamento,
    string? Municipio,
    DateOnly? FechaImposicion,
    DateOnly? FechaNotificacion,
    DateOnly FechaIngreso,
    DateOnly? FechaLimiteGestion,
    DateOnly? FechaFinDescuento50,
    DateOnly? FechaFinDescuento25,
    DateOnly? FechaCaducidad,
    DateOnly? FechaPrescripcion,
    decimal? ValorCapital,
    decimal? ValorAdicional,
    decimal? ValorTotal,
    bool Activo,
    string EstadoLegal,
    string VentanaDescuento,
    bool EnCobroCoactivo,
    string EstadoGestion,
    bool Gestionado,
    bool Pagado,
    bool TieneDp,
    string? DpEstado,
    string? DpRadicado,
    string? DpTipologia,
    bool? DpEnvioAutomatico,
    DateOnly? DpFechaRadicacion,
    string? DpSentido,
    DateOnly? DpFechaRespuesta,
    string EstadoNotificacionConductor,
    bool NotificadoFisicamente,
    string EstadoCambioContraventor,
    string EstadoConsultaContraventor,
    string? PagoTipo,
    decimal? PagoValor,
    DateOnly? PagoFecha);

/// <summary>
/// El resultado, con la misma forma que el de Trámites (<c>frontend/lib/api/queries.ts</c>, <c>QueryResult</c>).
/// <see cref="TotalPeriodoAnterior"/> es null cuando no se calcula: en las páginas siguientes a la primera (el export
/// encadena páginas y no lo necesita) o cuando el periodo anterior es demasiado amplio para traerlo.
/// </summary>
public sealed record ComparendoQueryResult(
    int Total,
    int Page,
    int PageSize,
    DateOnly Desde,
    DateOnly Hasta,
    int? TotalPeriodoAnterior,
    IReadOnlyList<ComparendoQueryRow> Filas,
    IReadOnlyList<QueryCoverageItemDto> Cobertura);

/// <summary>Cuerpo de <c>POST /saved</c>. Sin <see cref="Id"/> crea; con el de una propia la reemplaza; con el de una de fábrica la duplica.</summary>
public sealed record GuardarConsultaRequest(Guid? Id, string? Nombre, string? Descripcion, QueryDefinition? Definition);
