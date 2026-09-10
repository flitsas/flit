using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Infrastructure.Services;

/// <summary>
/// Documento de prenda: default OBLIGATORIO por compañía+OT.
/// Opt-out vía <c>admin.tenant_transit_office_prenda_document_policies.document_optional</c>
/// (check activo ⇒ deja de ser obligatorio). Snapshot: solo opt-outs vigentes al crear el trámite.
/// </summary>
internal sealed class PrendaDocumentRequirementPolicy : IPrendaDocumentRequirementPolicy
{
    public const string PrendaDocumentTypeCode = "inscripcion_prenda";

    private readonly IOtPrendaDocumentPolicyRepository _policies;

    public PrendaDocumentRequirementPolicy(IOtPrendaDocumentPolicyRepository policies)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
    }

    public async Task<bool> IsRequiredAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        DateTimeOffset procedureCreatedAt,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            return false;
        if (transitOfficeId is null || transitOfficeId == Guid.Empty)
            return false;

        var optional = await _policies
            .IsDocumentOptionalAtAsync(tenantId, transitOfficeId.Value, procedureCreatedAt, cancellationToken)
            .ConfigureAwait(false);

        return !optional;
    }
}
