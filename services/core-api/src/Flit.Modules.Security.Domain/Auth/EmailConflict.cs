namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// Código y mensaje visibles, únicos, para cualquier conflicto de correo ya ocupado (invitación pendiente,
/// cuenta viva o cuenta eliminada) al crear, invitar, reactivar, editar o activar un usuario.
/// <para>
/// HU #11580 colapsó el código a uno solo para no revelar la causa (las comprobaciones son globales y cruzan
/// la frontera del tenant). HU #13811 lo sube al dominio de Seguridad para que también lo use la activación de
/// cuenta en <c>core-identity</c>, que no ve los tipos internos de <c>Flit.Api</c>.
/// </para>
/// <para>Uso de ejemplo: <c>new ErrorResponse(EmailConflict.Code, EmailConflict.Message)</c> con HTTP 409.</para>
/// </summary>
public static class EmailConflict
{
    public const string Code = "EMAIL_ALREADY_IN_USE";

    public const string Message = "El usuario ya se encuentra previamente creado";
}
