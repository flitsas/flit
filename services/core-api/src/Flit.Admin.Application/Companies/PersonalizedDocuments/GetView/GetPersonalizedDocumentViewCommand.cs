namespace Flit.Admin.Application.Companies.PersonalizedDocuments.GetView;

/// <summary>
/// Comando de vista previa (HU #11314, AC3): presigned GET inline de una versión SIN activarla. No
/// exige el canal <c>TENANT_API</c> — es una ruta de lectura, igual que el historial (§8 DT-7).
/// </summary>
public sealed class GetPersonalizedDocumentViewCommand
{
    public required Guid TenantId { get; init; }
    public required Guid Id { get; init; }
}
