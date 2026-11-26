namespace Flit.Admin.Application.Companies.RuntPersonOverrides;

/// <summary>
/// Códigos FLIT del override (<c>CC|CE|PAS|TI|PPT</c>) y alias de proveedor que puede
/// traer el lookup de conductor tras <c>MapDocumentType</c> (bug #14721: <c>PAS→PA</c>).
/// </summary>
public static class TenantRuntPersonOverrideDocTypes
{
    /// <summary>
    /// Canoniza el tipo al código guardado en <c>admin.tenant_runt_person_overrides</c>.
    /// </summary>
    public static string Normalize(string documentType) =>
        documentType.ToUpperInvariant().Trim() switch
        {
            "PAS" or "PA" or "P" => "PAS",
            "CE" or "E" => "CE",
            "CC" or "C" => "CC",
            "TI" or "T" => "TI",
            "PPT" => "PPT",
            var other => other,
        };
}
