namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// FEATURE 05 — valores de <c>admin.tenant_operational_policies.fines_query_source</c> (creado en
/// FEATURE 02), vistos desde Trámites.
///
/// Espejo deliberado de <c>TenantSettingsCodes</c> (Flit.Admin.Domain): Application de Trámites no
/// referencia Admin.Domain — el cruce ocurre en Infraestructura, vía
/// <c>TenantConsultationOverrideProvider</c>, que sí ve ambos lados. Si cambian los valores del
/// CHECK de la columna, hay que tocar los dos.
/// </summary>
public static class FinesSourceCodes
{
    /// <summary>Consulta contra el API interno de FLIT (heredado de FLIT 1).</summary>
    public const string Internal = "internal";

    /// <summary>Consulta en línea contra el proveedor externo. Default de la columna.</summary>
    public const string External = "external";

    /// <summary>
    /// Normaliza el valor de la columna. Nulo, vacío o no reconocido ⇒ <see cref="External"/>:
    /// mismo default seguro que el DDL, para que una compañía sin configuración (o con un valor
    /// corrupto) siga consultando en línea en vez de quedarse sin consulta.
    /// </summary>
    public static string Normalize(string? raw)
    {
        var value = raw?.Trim();
        return string.Equals(value, Internal, StringComparison.OrdinalIgnoreCase)
            ? Internal
            : External;
    }
}
