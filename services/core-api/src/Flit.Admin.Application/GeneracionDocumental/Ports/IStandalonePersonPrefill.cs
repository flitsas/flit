namespace Flit.Admin.Application.GeneracionDocumental.Ports;

/// <summary>
/// Resultado de la consulta de persona natural en la cadena RUNT (conductor).
/// </summary>
/// <param name="Found"><c>true</c> si el RUNT devolvió nombre. Sin antecedente: <c>false</c> sin error.</param>
/// <param name="FullName">Nombre completo tal como lo reporta el RUNT.</param>
/// <param name="Error"><c>provider_unavailable</c> / <c>unsupported_document_type</c>, o <c>null</c>.</param>
public sealed record StandaloneRuntPersonResult(
    bool Found,
    string? FullName,
    string? FirstName,
    string? LastName,
    string? Provider,
    string? Error);

/// <summary>
/// Datos de CONTACTO ya conocidos de una persona dentro del tenant (respaldo del RUNT). Nunca trae
/// nombre ni documento: eso viene siempre de la fuente de identidad.
/// </summary>
public sealed record StandaloneContactLookupResult(
    bool Found,
    string? Ciudad,
    string? Direccion,
    string? Error);

/// <summary>
/// Puerto acotado de la consulta de persona natural por documento (CF-25), <b>sin instancia</b>.
///
/// <para>El adaptador reusa la MISMA cadena de proveedores del wizard (kyverum_runt_conductor →
/// verifik_conductor) y guarda el resultado en la caché de consultas externas con
/// <c>sourceProcedureInstanceId = null</c>, que es lo que ese servicio ya admite. No valida ninguna
/// instancia, no escribe eventos de trámite y no toca <c>field_values</c>.</para>
/// </summary>
public interface IStandaloneRuntPersonPrefill
{
    Task<StandaloneRuntPersonResult> LookupAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Puerto acotado del respaldo de contacto (<c>contact-lookup</c>): la última ficha de esa persona
/// como actor de un trámite del tenant. Es el segundo eslabón de la precedencia para persona
/// natural, y también completa el domicilio cuando el RUNT sí respondió (el RUNT no lo trae).
/// </summary>
public interface IStandaloneActorContactLookup
{
    Task<StandaloneContactLookupResult> LookupAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default);
}
