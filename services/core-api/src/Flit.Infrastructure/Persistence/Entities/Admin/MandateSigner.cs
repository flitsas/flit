namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Mandatario (firmante de mandato) gestionado dentro de un organismo de tránsito
/// (ADR-0023, ampliado por ADR-0036). <c>DocumentNumber</c> y <c>Email</c> son PII (Ley 1581): no
/// deben registrarse en logs ni exponerse en mensajes de error. <c>IntegrityHash</c> es una huella
/// determinista <c>SHA-256(full_name + document_number + registered_at)</c>, no un anonimizador.
/// </summary>
public sealed class MandateSigner
{
    public Guid Id { get; set; }
    public Guid TransitOfficeId { get; set; }
    public string FullName { get; set; } = string.Empty;

    /// <summary>Tipo de documento del mandatario (por defecto CC). ADR-0036.</summary>
    public string DocumentType { get; set; } = "CC";

    /// <summary>
    /// Número de documento (PII). Nulo solo cuando <see cref="SignerModel"/> es <c>formato_blanco</c>
    /// (HU #13128 AC8); el CHECK <c>ck_mandate_signers_document_required</c> lo exige para los demás modelos.
    /// </summary>
    public string? DocumentNumber { get; set; } = string.Empty;
    public string IntegrityHash { get; set; } = string.Empty;

    /// <summary>Correo del mandatario para la validación de identidad (ADR-0036, HU #10911). PII.</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Firma del baúl vinculada (ADR-0025), si está resuelta. <c>ON DELETE SET NULL</c>: al borrar la
    /// firma del baúl el mandatario queda sin firma (cae al sello de texto). ADR-0036 §D6.
    /// </summary>
    public Guid? SignatureVaultId { get; set; }

    /// <summary>Validación de identidad admin vigente vinculada (ADR-0034), si está resuelta.</summary>
    public Guid? IdentityValidationRef { get; set; }

    /// <summary>
    /// Cuenta de usuario de OT del mandatario (ADR-0036 §D9): llave del cotejo del firmante al aprobar
    /// (<c>user_id == usuario autenticado</c>). <c>ON DELETE SET NULL</c>: al borrar el usuario el
    /// mandatario queda sin cuenta (se comporta como "sin match").
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>Insumo del hash; se fija en el registro y no cambia al editar.</summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>Inactivación (reversible): al inactivar se liberan las compañías del mandatario. No es «Eliminar».</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// HU #13128 (ADR-0061) — modelo del mandatario: <c>natural</c> | <c>juridica</c> | <c>formato_blanco</c>.
    /// Distinto del tipo de mandato (<c>assignment_mode</c>: signer, institutional, open).
    /// </summary>
    public string SignerModel { get; set; } = "natural";

    /// <summary>
    /// Forma de firma: <c>baul</c> | <c>biometria</c>; nulo permitido en BD (legados, juridica, formato_blanco).
    /// Que <c>natural</c> la tenga lo exige la API, no la BD.
    /// </summary>
    public string? SignatureMethod { get; set; }

    /// <summary>Vigencia propia: <c>fixed</c> (sin fechas) | <c>range</c> (<see cref="ValidFrom"/> y <see cref="ValidTo"/>).</summary>
    public string ValidityKind { get; set; } = "fixed";

    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    /// <summary>Baja lógica de «Eliminar»: oculta de listas y selectores sin borrar historial. Distinta de <see cref="IsActive"/>.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
