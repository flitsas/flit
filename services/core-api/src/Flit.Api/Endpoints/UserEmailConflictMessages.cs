namespace Flit.Api.Endpoints;

/// <summary>
/// Mensaje y código visibles, unificados, para los conflictos de correo ya ocupado al
/// crear/editar un usuario — HU #11550 (mensaje) + HU #11580 (código).
///
/// Antes de HU #11550, <c>SecurityEndpoints</c> y <c>AdminOtEndpoints</c> tenían el mismo
/// literal copiado en cada <c>catch</c>, y con el tiempo divergieron entre sí (invitación
/// pendiente / cuenta activa / cuenta eliminada mostraban textos distintos).
///
/// HU #11550 unificó el mensaje pero conservó tres códigos distintos
/// (<c>INVITATION_ALREADY_PENDING</c>, <c>USER_ALREADY_EXISTS</c>,
/// <c>EMAIL_BELONGS_TO_DELETED_USER</c>) alegando que servían «para logs y auditoría». Es
/// falso: <c>CreateInvitationHandler</c> no inyecta <c>IAdminAuditWriter</c>, y el filtro que
/// audita estos endpoints (<c>AdminAuditFilter.ResolveErrorCode</c>) cae a un fallback por
/// status HTTP — para cualquier 409 registra literalmente <c>"conflict"</c>. Los tres códigos
/// nunca llegaron a la auditoría; su único destino era el cuerpo de la respuesta, y ahí sí
/// permitían a un atacante distinguir la causa de la ocupación de un correo cruzando incluso
/// la frontera del tenant (las comprobaciones de existencia de usuario/cuenta eliminada son
/// globales). HU #11580 colapsa el código de cara al cliente a uno solo; la causa concreta
/// ahora se registra en auditoría vía <c>ConfigAuditFailureContext.SetErrorCode</c>, que sí es
/// el canal real de trazabilidad.
/// </summary>
internal static class UserEmailConflictMessages
{
    public const string EmailAlreadyInUse = "El correo utilizado ya se encuentra asociado a otra cuenta";

    /// <summary>
    /// Código HTTP único que ve el cliente ante cualquiera de las tres causas de conflicto de
    /// correo (invitación pendiente, cuenta activa, cuenta eliminada). HU #11580 AC1/AC2.
    /// </summary>
    public const string EmailAlreadyInUseCode = "EMAIL_ALREADY_IN_USE";
}
