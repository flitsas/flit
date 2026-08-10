namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Deactivate;

/// <summary>
/// Comando de «volver al documento del sistema» (HU #11314): desactiva la versión activa de
/// <c>DocumentType</c> sin borrar ninguna fila ni archivo. Idempotente.
/// </summary>
public sealed class DeactivatePersonalizedDocumentCommand
{
    public required Guid TenantId { get; init; }
    public required string DocumentType { get; init; }
    public Guid? DeactivatedBy { get; init; }
}
