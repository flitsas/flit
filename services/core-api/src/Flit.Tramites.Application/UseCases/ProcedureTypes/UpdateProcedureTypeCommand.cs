using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed record UpdateProcedureTypeRequest(
    string Name,
    string? Description,
    bool IsActive,
    /// <summary>
    /// ADR-0050 — familia del tipo. Opcional: <c>null</c> la deja como está, para no obligar a los
    /// clientes anteriores a enviarla. Gobierna clasificación, filtros, causales de rechazo y el
    /// bloqueo por compañía, así que reclasificar un tipo mal ubicado es una corrección real.
    /// </summary>
    string? Family = null);

public sealed class UpdateProcedureTypeHandler(IProcedureTypeRepository repository)
{
    public async Task<(ProcedureTypeSummary? Result, string? Error)> HandleAsync(
        Guid id,
        UpdateProcedureTypeRequest request,
        CancellationToken ct = default)
    {
        var entity = await repository.GetByIdAsync(id, ct);
        if (entity is null)
            return (null, "not_found");

        // ADR-0050 — un tipo PUBLICADO se puede corregir; un ARCHIVADO no. Mismo razonamiento que en
        // `UpdateConformationProfileHandler`: los trámites en curso leen su snapshot congelado, así
        // que renombrar o reclasificar el tipo no los alcanza. Corregir el nombre importa más de lo
        // que parece: es el rótulo legal del mandato y de la portada del expediente.
        if (entity.PublicationStatus == PublicationStatus.Archived)
            return (null, "conflict");

        if (entity.PublicationStatus == PublicationStatus.Published)
            entity.Version += 1;

        if (!string.IsNullOrWhiteSpace(request.Family))
        {
            var familia = request.Family.Trim().ToUpperInvariant();
            if (!ProcedureFamilyCodes.IsValid(familia))
                return (null, "invalid_family");
            entity.Family = familia;
        }

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(entity, ct);
        await repository.SaveChangesAsync(ct);

        return (CreateProcedureTypeHandler.ToSummary(entity), null);
    }
}
