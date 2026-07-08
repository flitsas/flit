namespace Flit.Modules.Security.Application.Auth.CreateInvitation;

public sealed class InvitationOptions
{
    public const string SectionName = "Invitations";

    public string ActivateUrlBase { get; set; } = "http://localhost:3000/invite/activate";

    /// <summary>
    /// HU #10625 AC2: tiempo mínimo anti-abuso entre reenvíos consecutivos de la misma
    /// invitación pendiente. Se calcula contra <c>UserInvitation.LastSentAt</c>.
    /// </summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromMinutes(2);
}
