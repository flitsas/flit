namespace Flit.Infrastructure.KyverumRunt;

/// <summary>
/// Error del proveedor Kyverum RUNT al resolver una consulta (<c>vehiculos:consultar</c> /
/// <c>personas:consultar</c>, HU #10478). Distingue tres clases de fallo para que los providers de
/// consulta (<c>KyverumRunt*ConsultationProvider</c>) mapeen al <see cref="Flit.Tramites.Application.UseCases.Consultations.ConsultationResult"/>
/// correcto sin filtrar detalle crudo:
/// <list type="bullet">
/// <item><see cref="IsNotFound"/> — <c>200 OK</c> con <c>ok:false</c> (dato no hallado en el RUNT):
/// no es un error HTTP, se mapea a un check de "no encontrado".</item>
/// <item><see cref="IsTransient"/> — <c>UPSTREAM_UNAVAILABLE</c>/5xx/timeout/red: reintentable.</item>
/// <item>resto (4xx auth/validación, respuesta ilegible): definitivo.</item>
/// </list>
/// El mensaje NUNCA contiene la API key del proveedor (mismo patrón que <c>ImprontaRuntException</c>).
/// </summary>
public sealed class KyverumRuntException(string message, bool isTransient, bool isNotFound = false)
    : Exception(message)
{
    public bool IsTransient { get; } = isTransient;

    /// <summary>Respuesta <c>200 OK</c> con <c>ok:false</c>: el RUNT no tiene el dato consultado.</summary>
    public bool IsNotFound { get; } = isNotFound;
}
