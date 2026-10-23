namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Qué se consulta del impuesto departamental (HU #13862, F09-C5): la placa definitiva que dejó el RUNT y el departamento
/// derivado de su organismo (<c>vehiculos.departamento_codigo</c>, DIVIPOLA de dos dígitos). Son datos del bien, sin documentos.
/// </summary>
/// <example>
/// <code>
/// var solicitud = new SolicitudImpuesto("TST301", "05");
/// </code>
/// </example>
public sealed record SolicitudImpuesto(string Placa, string DepartamentoCodigo);

/// <summary>
/// Puerto del impuesto vehicular departamental (HU #13862, F09-C5, F09-C6): <c>ConsultasService.ConsultarImpuestoVehicular</c>
/// de core-consultas a nombre de la empresa de la celda. Devuelve <see cref="ResultadoImpuesto"/> o el error tipado, con el
/// proveedor que respondió y su transacción solo para el intento auditable (R-20: nunca al payload). Implementaciones:
/// <see cref="FuenteImpuestoReal"/>, <see cref="FuenteImpuestoSimulada"/> (LOCAL o DEV) y <see cref="FuenteImpuestoNoConectada"/>,
/// elegidas por <see cref="SeleccionAdaptadores"/>. La usa <see cref="EjecutorCeldaImpuesto"/>.
/// </summary>
/// <example>
/// <code>
/// var r = await fuente.ConsultarAsync(celda.TenantId, new SolicitudImpuesto(placa, departamento), ct);
/// if (r.Error is { } error) return ResultadoCelda.Error(error.TipoError);
/// </code>
/// </example>
public interface IFuenteImpuesto
{
    Task<RespuestaFuente<ResultadoImpuesto>> ConsultarAsync(Guid empresa, SolicitudImpuesto solicitud, CancellationToken ct);
}
