namespace Flit.Api.Endpoints;

/// <summary>
/// Mensaje visible unificado para los conflictos de correo ya ocupado al crear/editar un
/// usuario — HU #11550. Antes del ajuste, <c>SecurityEndpoints</c> y <c>AdminOtEndpoints</c>
/// tenían el mismo literal copiado en cada <c>catch</c>, y con el tiempo divergieron entre sí
/// (invitación pendiente / cuenta activa / cuenta eliminada mostraban textos distintos).
///
/// Los códigos de error (<c>INVITATION_ALREADY_PENDING</c>, <c>USER_ALREADY_EXISTS</c>,
/// <c>EMAIL_BELONGS_TO_DELETED_USER</c>) y los HTTP status se mantienen SIN cambios — solo se
/// unifica el texto que ve el usuario final, para no perder trazabilidad en logs/auditoría.
/// </summary>
internal static class UserEmailConflictMessages
{
    public const string EmailAlreadyInUse = "El correo utilizado ya se encuentra asociado a otra cuenta";
}
