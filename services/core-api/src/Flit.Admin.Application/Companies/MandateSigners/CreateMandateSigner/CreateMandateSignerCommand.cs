using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
namespace Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;

/// <summary>Alta de un mandatario en un OT. <c>DocumentNumber</c> es PII: no loguear.</summary>
public sealed class CreateMandateSignerCommand
{
    public required Guid TransitOfficeId { get; init; }
    public required string FullName { get; init; }

    /// <summary>Vacío solo para el Formato en blanco (HU #13129).</summary>
    public required string DocumentNumber { get; init; }
    public required IReadOnlyList<Guid> CompanyTenantIds { get; init; }

    /// <summary>Tipo de documento (ADR-0036); por defecto CC.</summary>
    public string DocumentType { get; init; } = "CC";

    /// <summary>Correo para la validación de identidad (ADR-0036, HU #10911). PII.</summary>
    public string? Email { get; init; }

    /// <summary>Cuenta de usuario de OT del mandatario (ADR-0036 §D9).</summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// HU #11201 — organismos donde aplica. Vacío o nulo ⇒ solo <see cref="TransitOfficeId"/>.
    /// </summary>
    public IReadOnlyList<Guid>? TransitOfficeIds { get; init; }

    /// <summary>
    /// HU #13131 (ADR-0061) — OBSOLETO E IGNORADO. La firma física ya no es una forma de firma: el campo se
    /// acepta por compatibilidad con clientes anteriores, pero no se valida ni se persiste como exención.
    /// </summary>
    public IReadOnlyList<Guid>? PhysicalSignatureOfficeIds { get; init; }

    /// <summary>
    /// Firma del baúl elegida para el mandatario. <c>null</c> ⇒ el trámite la resuelve por documento,
    /// que es el comportamiento previo.
    /// </summary>
    public Guid? SignatureVaultId { get; init; }

    /// <summary>
    /// HU #13179 — compañías de FLIT (por tenant) a las que se asocia, POR ORGANISMO. Vacío o ausente ⇒
    /// solo aplica a su propia compañía. En la edición, <c>null</c> no toca nada y cada organismo de la
    /// lista reemplaza su conjunto.
    /// </summary>
    public IReadOnlyList<MandateSignerOfficeCompanies>? OfficeCompanies { get; init; }

    /// <summary>
    /// HU #13129 (ADR-0061) — modelo: <c>natural</c> | <c>juridica</c> | <c>formato_blanco</c>. Ausente ⇒
    /// <c>natural</c>. Persona jurídica y Formato en blanco no admiten forma de firma, fechas ni correo.
    /// </summary>
    public string? SignerModel { get; init; }

    /// <summary>Forma de firma de la Persona natural: <c>baul</c> | <c>biometria</c>. Obligatoria para natural.</summary>
    public string? SignatureMethod { get; init; }

    /// <summary>Vigencia propia: <c>fixed</c> (por defecto) | <c>range</c> (exige <see cref="ValidFrom"/> y <see cref="ValidTo"/>).</summary>
    public string? ValidityKind { get; init; }

    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }

    /// <summary>
    /// HU #13195 — origen del vínculo mandatario-compañía que se escribe: <c>organismo</c> (por defecto, ruta del
    /// OT), <c>super_admin</c> o <c>compania</c> (ruta de la compañía). Ver <c>MandateSignerOrigins</c>.
    /// </summary>
    public string ConfiguredByScope { get; init; } = "organismo";

    public Guid? CreatedBy { get; init; }
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// HU #13123 (Epic #13090, F1) — alta desde el OT: aplica las MISMAS validaciones que la compañía
    /// (firma del baúl contra el tenant de la compañía, medio de firma). Solo se evalúan cuando la
    /// compañía ya pasó su validación de visibilidad/exclusividad, para no confirmar la existencia de una
    /// compañía que el organismo no ve. El flujo de la compañía valida antes de delegar y deja esto en
    /// <c>false</c>.
    /// </summary>
    public bool ValidateSigningMeans { get; init; }

    /// <summary>
    /// Bug #12912 (Ley 1581) — compañías que quien opera puede asignar en el OT (ver
    /// <see cref="OtCompanyVisibility"/>). Con la vista del organismo la operación queda acotada a su
    /// propia fila: no toca organismos ajenos ni compañías que no puede ver.
    /// </summary>
    public required OtCompanyVisibility CompanyVisibility { get; init; }
}
