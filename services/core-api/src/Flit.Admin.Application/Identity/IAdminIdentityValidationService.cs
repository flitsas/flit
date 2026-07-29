using Flit.Admin.Domain.Identity;

namespace Flit.Admin.Application.Identity;

/// <summary>
/// Descriptor AGNÓSTICO del sujeto a validar (HU #10907, ADR-0034). El endpoint (que sí conoce el
/// sujeto, p.ej. un representante legal) lo arma leyendo el registro del sujeto; el servicio no consulta
/// el sujeto directamente. <c>DocumentNumber</c>/<c>Email</c> son PII: no loguear.
/// </summary>
public sealed record AdminIdentitySubjectDescriptor(
    Guid TenantId,
    string SubjectType,
    Guid SubjectRef,
    string Name,
    string DocumentType,
    string DocumentNumber,
    string Email,
    Guid? ActorBy);

/// <summary>
/// Resultado de iniciar/reenviar una validación: la validación resultante + si el envío se reutilizó
/// una vigente (<see cref="Reused"/> = no se reenvió porque ya había una aprobada y vigente).
/// </summary>
public sealed record AdminIdentityValidationResult(
    AdminIdentityValidation Validation,
    bool Reused);

/// <summary>
/// Servicio del bloque de validación de identidad administrativa DESACOPLADA de un trámite (HU #10907,
/// ADR-0034). AGNÓSTICO del sujeto: opera sobre <see cref="AdminIdentitySubjectDescriptor"/> y ancla la
/// validación por <c>subjectType</c> + <c>subjectRef</c>, reutilizable por cualquier sujeto (hoy el
/// representante legal; mañana el mandatario) sin cambiar el servicio.
/// </summary>
public interface IAdminIdentityValidationService
{
    /// <summary>
    /// Inicia una validación: llama al proveedor (que notifica el enlace de captura por correo) y persiste
    /// la validación en <c>enviado</c>. Lanza <see cref="AdminIdentityProviderException"/> si el proveedor
    /// falla.
    /// </summary>
    Task<AdminIdentityValidationResult> SendAsync(
        AdminIdentitySubjectDescriptor subject,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reenvía la validación del sujeto: reenvía si NUNCA se hizo o si la última NO está aprobada+vigente
    /// (SIN la guarda <c>biometria_activa</c> del flujo de trámite: una en curso SÍ se puede reenviar). Si
    /// ya hay una aprobada y vigente, la reutiliza sin reenviar (<see cref="AdminIdentityValidationResult.Reused"/>).
    /// </summary>
    Task<AdminIdentityValidationResult> ResendAsync(
        AdminIdentitySubjectDescriptor subject,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// APALANCA la identidad de la persona al registrar un sujeto NUEVO (HU #11000). Precedencia:
    /// 1) el propio sujeto ya tiene una aprobada y vigente ⇒ se reutiliza;
    /// 2) la PERSONA (mismo tipo+número de documento en el tenant) tiene una aprobada y vigente en otro
    ///    sujeto ⇒ se ancla esa validación al sujeto nuevo y NO se envía correo;
    /// 3) en cualquier otro caso ⇒ se inicia una validación nueva (envía el correo).
    /// <see cref="AdminIdentityValidationResult.Reused"/> distingue 1 y 2 de 3. Lanza
    /// <see cref="AdminIdentityProviderException"/> solo en el caso 3 si el proveedor falla.
    /// </summary>
    Task<AdminIdentityValidationResult> EnsureAsync(
        AdminIdentitySubjectDescriptor subject,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aprueba la validación en <paramref name="now"/>: estampa <c>valid_until</c> (30 días) +
    /// <paramref name="certificateHash"/> y VINCULA la validación al sujeto
    /// (<c>LegalRepresentative.LinkIdentity</c>). Idempotente. <c>false</c> si la validación no existe.
    /// </summary>
    Task<bool> ApproveAsync(
        Guid tenantId,
        Guid validationId,
        string? certificateHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconcilia contra el proveedor: consulta el estado real y, si aprobó, aprueba + vincula; si rechazó,
    /// terminaliza. Es la vía de aprobación del bloque administrativo (desacoplada del webhook del trámite).
    /// Devuelve <c>true</c> si el estado cambió. Lanza <see cref="AdminIdentityProviderException"/> ante
    /// fallo del proveedor.
    /// </summary>
    Task<bool> ReconcileAsync(
        Guid tenantId,
        Guid validationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
