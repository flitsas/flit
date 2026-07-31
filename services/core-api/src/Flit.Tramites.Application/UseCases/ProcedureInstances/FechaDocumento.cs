namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Formato de fecha ÚNICO de los documentos que genera el sistema (HU #11018): <c>AÑO/MES/DÍA</c>, sin
/// hora. Se centraliza aquí para que FUR, compraventa, mandato y trámite virtual no vuelvan a divergir.
/// Las bitácoras técnicas (webhooks, correos de scheduler) NO usan este formato: ahí la hora es
/// información de diagnóstico.
/// </summary>
public static class FechaDocumento
{
    /// <summary>Patrón de fecha de negocio para <c>ToString</c>.</summary>
    public const string Formato = "yyyy/MM/dd";
}
