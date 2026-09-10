namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Confirm;

/// <summary>
/// Comando de confirmación de una versión (HU #11313): re-lee el objeto subido, recalcula el SHA-256
/// en servidor y valida el PDF. Única oportunidad de ver los bytes.
/// </summary>
public sealed class ConfirmPersonalizedDocumentVersionCommand
{
    public required Guid TenantId { get; init; }
    public required Guid Id { get; init; }
    public Guid? ConfirmedBy { get; init; }
}
