namespace Flit.Admin.Application.Companies.Deeds.DeleteDeed;

/// <summary>Comando de baja (lógica) de una escritura.</summary>
public sealed class DeleteDeedCommand
{
    public required Guid TenantId { get; init; }
    public required Guid Id { get; init; }
    public Guid? ChangedBy { get; init; }
}
