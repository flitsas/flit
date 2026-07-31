using Flit.Admin.Domain.DocumentRequirementOverrides;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Services;

/// <summary>
/// Implementación EF Core de <see cref="IPrendaDocumentRequirementPolicy"/> (CF-06, HU #10881):
/// resuelve si el Organismo de Tránsito exige el documento de prenda para un tipo de trámite vía
/// el override de obligatoriedad documental por OT (<c>tramites.document_requirement_overrides</c>,
/// HU #10198) sobre el documento canónico <c>inscripcion_prenda</c> (sembrado y asociado a
/// <c>TRASPASO_STANDARD</c> como opcional/<c>condition_group='prenda'</c> por la migración de esta
/// HU — así el override existente puede activarlo sin exigir un documento SIEMPRE obligatorio).
/// <para>
/// <b>SNAPSHOT (AC2):</b> solo cuentan los overrides cuyo <c>created_at</c> es anterior o igual a
/// <c>procedureCreatedAt</c> — un override activado DESPUÉS de crear el trámite no lo afecta (el
/// trámite en curso sigue su matriz vigente al nacer). Este puerto es deliberadamente NO-vivo, a
/// diferencia de <c>IResolvedChecklistMatrixProvider</c> (HU #10522, matriz viva): el requisito
/// puntual de prenda por OT es el único con contrato explícito de snapshot (AC2 de esta HU).
/// </para>
/// </summary>
internal sealed class PrendaDocumentRequirementPolicy : IPrendaDocumentRequirementPolicy
{
    /// <summary>Código canónico del documento de prenda en <c>tramites.document_types</c>.</summary>
    public const string PrendaDocumentTypeCode = "inscripcion_prenda";

    private readonly FlitDbContext _context;

    public PrendaDocumentRequirementPolicy(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> IsRequiredAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        DateTimeOffset procedureCreatedAt,
        CancellationToken cancellationToken = default)
    {
        if (transitOfficeId is null || transitOfficeId == Guid.Empty)
            return false;

        return await (
                from o in _context.DocumentRequirementOverrides.AsNoTracking()
                join d in _context.DocumentTypes.AsNoTracking() on o.DocumentTypeId equals d.Id
                where o.ProcedureTypeId == procedureTypeId
                    && o.TransitOfficeId == transitOfficeId.Value
                    && d.Code == PrendaDocumentTypeCode
                    && o.RequirementState == DocumentRequirementState.Required
                    && o.CreatedAt <= procedureCreatedAt
                select o.Id)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
