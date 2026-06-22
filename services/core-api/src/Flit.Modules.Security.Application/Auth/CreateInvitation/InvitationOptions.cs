namespace Flit.Modules.Security.Application.Auth.CreateInvitation;

public sealed class InvitationOptions
{
    public const string SectionName = "Invitations";

    public string ActivateUrlBase { get; set; } = "http://localhost:3000/invite/activate";
}
