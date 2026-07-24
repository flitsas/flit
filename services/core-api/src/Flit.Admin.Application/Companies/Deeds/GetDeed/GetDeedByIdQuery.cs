namespace Flit.Admin.Application.Companies.Deeds.GetDeed;

/// <summary>Consulta de una escritura por id dentro del tenant (con presigned URL de vista).</summary>
public sealed class GetDeedByIdQuery
{
    public required Guid TenantId { get; init; }
    public required Guid Id { get; init; }
}
