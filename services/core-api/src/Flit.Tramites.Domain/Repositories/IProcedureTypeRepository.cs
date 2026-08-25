using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

public interface IProcedureTypeRepository
{
    Task<ProcedureType?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProcedureType?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<ProcedureType?> GetByCodePublishedAsync(string code, CancellationToken ct = default);
    Task<List<ProcedureType>> ListAsync(string? family, string? publicationStatus, CancellationToken ct = default);
    Task AddAsync(ProcedureType procedureType, CancellationToken ct = default);
    Task UpdateAsync(ProcedureType procedureType, CancellationToken ct = default);
    Task<List<ConformationRule>> GetConformationRulesAsync(Guid procedureTypeId, CancellationToken ct = default);
    Task ReplaceConformationRulesAsync(Guid procedureTypeId, List<ConformationRule> rules, CancellationToken ct = default);
    Task<List<ProcedureStep>> GetStepsWithDetailsAsync(Guid procedureTypeId, CancellationToken ct = default);
    Task ReplaceStepsAsync(Guid procedureTypeId, List<ProcedureStep> steps, CancellationToken ct = default);
    Task AddFormFieldsAsync(IEnumerable<FormField> fields, CancellationToken ct = default);
    Task<bool> HasInstancesAsync(Guid procedureTypeId, CancellationToken ct = default);

    /// <summary>¿El código ya está ocupado en el catálogo? Incluye tipos archivados.</summary>
    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
