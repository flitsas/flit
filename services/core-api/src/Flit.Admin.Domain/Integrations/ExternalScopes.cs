namespace Flit.Admin.Domain.Integrations;

/// <summary>
/// HU #13084 — permisos que puede tener un cliente de integración externo (contrato de sincronización
/// de trámites v3.1, §1).
/// </summary>
public static class ExternalScopes
{
    /// <summary>Lectura del feed de trámites y de la URL de factura.</summary>
    public const string TramitesRead = "external.tramites.read";

    /// <summary>Datos personales sin enmascarar en el feed.</summary>
    public const string TramitesPiiRead = "external.tramites.pii.read";

    /// <summary>
    /// HU #13263 (Feature #13261, Épica #12741) — escritura: adjuntar al trámite el comprobante de pago del
    /// impuesto departamental (<c>POST /api/v1/external/tramites/{id}/adjuntos</c>, contrato v3.2 §7).
    /// </summary>
    public const string AttachmentsWrite = "external.tramites.attachments.write";

    /// <summary>Todos los permisos válidos.</summary>
    public static readonly IReadOnlyList<string> Todos = [TramitesRead, TramitesPiiRead, AttachmentsWrite];

    public static bool EsValido(string? scope) =>
        scope is not null && Todos.Contains(scope, StringComparer.Ordinal);
}
