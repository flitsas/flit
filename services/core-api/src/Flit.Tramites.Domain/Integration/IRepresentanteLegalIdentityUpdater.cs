namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Puerto para actualizar, desde trámites, la identidad vigente que el directorio de Admin tiene
/// anclada a un representante legal (HU #11196, AC4). Cuando la validación diferida se aprueba y el lote
/// se firma, la compañía debe quedar apuntando a esa identidad: si no, el directorio seguiría diciendo
/// que el representante no tiene con qué firmar y el siguiente trámite volvería a pedirle la validación.
///
/// <para>Se llavea por DOCUMENTO, no por id del directorio: el representante puede no estar registrado
/// (ese es justo el caso de la HU #11195), y entonces no hay nada que actualizar.</para>
/// </summary>
public interface IRepresentanteLegalIdentityUpdater
{
    /// <summary>
    /// Ancla <paramref name="validationId"/> como identidad vigente de la persona en el tenant. Devuelve
    /// cuántos registros del directorio se actualizaron (0 si esa persona no está registrada).
    /// <paramref name="documentNumber"/> es PII (Ley 1581): no loguear.
    /// </summary>
    Task<int> ActualizarIdentidadVigenteAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        Guid validationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación inerte (no actualiza nada) para los tests que no ejercitan el anclaje en el
/// directorio de Admin.
/// </summary>
public sealed class NullRepresentanteLegalIdentityUpdater : IRepresentanteLegalIdentityUpdater
{
    public static NullRepresentanteLegalIdentityUpdater Instance { get; } = new();

    public Task<int> ActualizarIdentidadVigenteAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        Guid validationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
