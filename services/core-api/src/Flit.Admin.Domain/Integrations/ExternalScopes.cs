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

    /// <summary>Todos los permisos válidos.</summary>
    public static readonly IReadOnlyList<string> Todos = [TramitesRead, TramitesPiiRead];

    public static bool EsValido(string? scope) =>
        scope is not null && Todos.Contains(scope, StringComparer.Ordinal);
}
