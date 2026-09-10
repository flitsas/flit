namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Activate;

/// <summary>
/// Comando de reactivación de una versión histórica (HU #11314): la elegida pasa a <c>activo</c> y
/// la anterior activa (si existe) a <c>historico</c>. Repetible en cualquier orden.
/// </summary>
public sealed class ActivatePersonalizedDocumentVersionCommand
{
    public required Guid TenantId { get; init; }
    public required Guid Id { get; init; }
    public Guid? ActivatedBy { get; init; }
}
