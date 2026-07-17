namespace Flit.Modules.Quipux.Application.UseCases.Cola;

/// <summary>
/// Re-encolar manualmente una radicación <c>fallido</c> desde la consola de cola (HU #10774).
/// <paramref name="ActorUserId"/> solo se usa para el log de aplicación —el actor no se persiste en
/// la bitácora (regla PII del repo)—; puede ser null si el token no traía <c>sub</c>.
/// </summary>
public sealed record ReintentarSubmissionCommand(Guid SubmissionId, Guid TransitOfficeId, Guid? ActorUserId);
