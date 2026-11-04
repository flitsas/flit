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

    /// <summary>
    /// HU #14067 (Épica #14021) — único permiso de una integración <c>persona_natural</c>: la fachada del portal de
    /// Persona Natural. No figura en <see cref="Todos"/> y ninguna política de <c>/api/v1/external/*</c> lo acepta: el pase
    /// de una integración de este tipo NO lee ni escribe trámites por los endpoints externos.
    /// </summary>
    public const string PersonaNatural = "platform.tramites.persona_natural";

    /// <summary>Permisos que puede llevar un pase según el tipo de la integración (<see cref="ExternalIntegrationTypes"/>).</summary>
    public static bool EsValidoPara(string? tipo, string? scope) =>
        string.Equals(tipo, ExternalIntegrationTypes.PersonaNatural, StringComparison.Ordinal)
            ? string.Equals(scope, PersonaNatural, StringComparison.Ordinal)
            : EsValido(scope);
}

/// <summary>HU #14067 — tipos de integración de <c>integrations.external_clients.tipo</c> y su catálogo de alcances.</summary>
public static class ExternalIntegrationTypes
{
    /// <summary>Cliente de máquina de un sistema externo (feed de trámites): sin compañía. Valor por defecto histórico.</summary>
    public const string Externa = "externa";

    /// <summary>Portal de Persona Natural: ligada a una compañía y con el único alcance <see cref="ExternalScopes.PersonaNatural"/>.</summary>
    public const string PersonaNatural = "persona_natural";

    /// <summary>Tipos que se pueden crear desde la ficha de la compañía.</summary>
    public static readonly IReadOnlyList<string> PorCompania = [PersonaNatural];

    public static bool EsPorCompania(string? tipo) => tipo is not null && PorCompania.Contains(tipo, StringComparer.Ordinal);

    /// <summary>Alcances fijos del tipo (el superadministrador no los elige: así el alcance queda acotado).</summary>
    public static IReadOnlyList<string> AlcancesDe(string tipo) => [ExternalScopes.PersonaNatural];
}
