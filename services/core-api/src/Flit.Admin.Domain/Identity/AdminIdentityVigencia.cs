namespace Flit.Admin.Domain.Identity;

/// <summary>
/// HU #11059 / HU #11060 — vocabulario de vigencia de identidad de un sujeto administrativo
/// (representante legal o mandatario del OT) que expone el contrato admin (wire:
/// valid/pending/expired/none).
/// <para>
/// HU #11765 (ADR-0050) — el cálculo de vigencia YA NO vive aquí: <c>Resumir</c> (y su tipo
/// <c>Entrada</c>) se retiraron porque <c>admin.admin_identity_validations</c> dejó de tener
/// lectores (los dos lectores admin migraron al módulo Identidad,
/// <c>IdentityVigenciaPorDocumentoResolver</c> + <c>IdentityVigenciaClassifier</c> vía ADR-0050).
/// Esta clase queda SOLO como el vocabulario del wire (las cuatro constantes) y el shape del
/// resultado (<see cref="Resultado"/>): <c>IdentityVigenciaLegacyMapper</c> (capa Infraestructura)
/// traduce el estado ADR-0050 a este vocabulario para no romper el contrato ni el frontend
/// (<c>identity-vigencia.ts</c>).
/// </para>
/// </summary>
public static class AdminIdentityVigencia
{
    /// <summary>Aprobada y dentro de su vigencia. No hay nada que renovar.</summary>
    public const string Valid = "valid";

    /// <summary>Enviada o en curso: el sujeto todavía no completó la captura.</summary>
    public const string Pending = "pending";

    /// <summary>Hubo validación pero ya no sirve (aprobada vencida, rechazada o expirada) ⇒ RENOVAR.</summary>
    public const string Expired = "expired";

    /// <summary>Nunca se validó ⇒ ENVIAR por primera vez.</summary>
    public const string None = "none";

    /// <param name="Status">Uno de <see cref="Valid"/>/<see cref="Pending"/>/<see cref="Expired"/>/<see cref="None"/>.</param>
    /// <param name="ValidUntil">
    /// Hasta cuándo es válida; solo se informa cuando <paramref name="Status"/> es <see cref="Valid"/>.
    /// <c>null</c> en una aprobada sin caducidad registrada (vigente indefinidamente).
    /// </param>
    public readonly record struct Resultado(string Status, DateTimeOffset? ValidUntil);
}
