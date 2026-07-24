namespace Flit.Admin.Application.Companies.LegalRepresentatives.DeleteLegalRepresentative;

/// <summary>Comando de baja lógica (desactivación) de un representante legal (HU #10901).</summary>
public sealed class DeleteLegalRepresentativeCommand
{
    public required Guid TenantId { get; init; }

    public required Guid Id { get; init; }

    public Guid? ChangedBy { get; init; }
}
