namespace Flit.Modules.Quipux.Application.UseCases.Cola;

/// <summary>
/// Cancelar manualmente una radicación <c>pendiente</c> desde la consola de cola (HU #10774): la
/// lleva a <c>fallido</c> terminal antes de que se radique. <paramref name="ActorUserId"/> solo va
/// al log de aplicación (el actor no se persiste en la bitácora — regla PII del repo).
/// </summary>
public sealed record CancelarSubmissionCommand(Guid SubmissionId, Guid TransitOfficeId, Guid? ActorUserId);
