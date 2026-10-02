using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
namespace Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;

/// <summary>
/// Edición de un mandatario (RF23). Regenera la huella de integridad con la fecha de registro
/// original. <c>DocumentNumber</c> es PII: no loguear.
/// </summary>
public sealed class UpdateMandateSignerCommand
{
    /// <summary>
    /// Organismo bajo el que se edita: se valida su operabilidad y su tenant firma la auditoría. Tras la
    /// edición queda como PRIMARIO del mandatario.
    /// </summary>
    public required Guid TransitOfficeId { get; init; }

    /// <summary>
    /// Organismo primario que el mandatario tiene AHORA en base de datos, cuando difiere del anterior.
    ///
    /// <para>Existe porque la edición desde el configurador de la compañía (HU #11202) no se hace "bajo
    /// un organismo": el gestor manda la lista completa de organismos donde aplica. Usar el primero de
    /// esa lista como identidad hacía que la edición respondiera 404 en cuanto ese primero no coincidía
    /// con el primario guardado —por ejemplo al añadir un organismo nuevo—. La identidad se comprueba
    /// contra ESTE valor; <c>null</c> ⇒ contra <see cref="TransitOfficeId"/>, que es el comportamiento de
    /// la edición desde el perfil del organismo.</para>
    /// </summary>
    public Guid? OrganismoPrimarioActual { get; init; }
    public required Guid MandateSignerId { get; init; }
    public required string FullName { get; init; }
    public required string DocumentNumber { get; init; }
    public required IReadOnlyList<Guid> CompanyTenantIds { get; init; }

    /// <summary>Tipo de documento (ADR-0036); por defecto CC.</summary>
    public string DocumentType { get; init; } = "CC";

    /// <summary>Correo para la validación de identidad (ADR-0036, HU #10911). PII.</summary>
    public string? Email { get; init; }

    /// <summary>Cuenta de usuario de OT del mandatario (ADR-0036 §D9).</summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// HU #11201 — conjunto deseado de organismos. <c>null</c> ⇒ no se tocan; una lista los reemplaza.
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
    /// El llamante gestiona la firma del baúl y <see cref="SignatureVaultId"/> es su valor deseado
    /// (incluido <c>null</c>, que la desvincula). En <c>false</c> la firma NO se toca.
    ///
    /// <para>Hace falta porque <c>Guid?</c> no distingue "no la gestiono" de "quítala": la edición desde
    /// el perfil del organismo no maneja este campo, y sin esta señal cada guardado suyo borraría la
    /// firma que la compañía acababa de elegir.</para>
    /// </summary>
    public bool ActualizaFirma { get; init; }

    /// <summary>HU #13129 — modelo; ausente ⇒ se conserva el guardado.</summary>
    public string? SignerModel { get; init; }

    /// <summary>HU #13129 — forma de firma; ausente ⇒ se conserva la guardada (solo natural).</summary>
    public string? SignatureMethod { get; init; }

    /// <summary>HU #13129 — <c>fixed</c> | <c>range</c>; ausente ⇒ se conserva la guardada (solo natural).</summary>
    public string? ValidityKind { get; init; }

    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }

    /// <summary>
    /// HU #13195 — origen del vínculo mandatario-compañía que se escribe: <c>organismo</c> (por defecto, ruta del
    /// OT), <c>super_admin</c> o <c>compania</c> (ruta de la compañía). Ver <c>MandateSignerOrigins</c>.
    /// </summary>
    public string ConfiguredByScope { get; init; } = "organismo";

    public Guid? UpdatedBy { get; init; }
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// Bug #12912 (Ley 1581) — compañías que quien opera puede asignar en el OT (ver
    /// <see cref="OtCompanyVisibility"/>). Con la vista del organismo la operación queda acotada a su
    /// propia fila: no toca organismos ajenos ni compañías que no puede ver.
    /// </summary>
    public required OtCompanyVisibility CompanyVisibility { get; init; }
}
