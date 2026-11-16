using System.Net.Mail;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;

namespace Flit.Modules.Security.Application.UserRoles.ProductRoles;

public sealed record InviteProductUserCommand(Guid TenantId, ProductActor Actor, string ProductCode, string Email, string Name, string RoleCode);

/// <summary>Qué pasó de verdad al invitar. Solo va a la auditoría: hacia afuera la respuesta es la misma (SP-15).</summary>
public enum InviteOutcome
{
    InvitationCreated,
    RoleAssigned,
    AlreadyWithRole,
    PendingInvitation,
    OtherTenant,
    ProtectedTarget,
}

public sealed record InviteProductUserResult(InviteOutcome Outcome, Guid? TargetId, bool EmailSent);

/// <summary>Crea una invitación nueva con UN rol (lo implementa la API sobre <c>CreateInvitationHandler</c>).</summary>
public interface IProductInvitationSender
{
    /// <returns>El id de la invitación y si el correo salió; <c>null</c> si otra invitación o cuenta con ese correo se adelantó.</returns>
    Task<ProductInvitationSent?> SendAsync(Guid tenantId, string email, string fullName, Guid roleId, Guid invitedBy, CancellationToken ct);
}

public sealed record ProductInvitationSent(Guid InvitationId, bool EmailSent);

/// <summary>
/// ADR-0089 regla 7 / SP-15 — invitar desde el producto. Correo nuevo: invitación con el rol resuelto por código. Miembro de
/// la empresa sin rol del producto: se le asigna (conserva los de otros productos). Ya con rol, invitación pendiente o
/// correo de otra empresa: no hace nada (jamás mueve a un usuario de empresa). AdminCompany o SuperAdmin de la empresa: no hace
/// nada (su acceso se gestiona en el hub, CR-8). El resultado real solo es para auditoría.
/// </summary>
public sealed class InviteProductUserHandler(IProductRoleRepository repo, IProductInvitationSender sender)
{
    public const int MaxEmail = 254;
    public const int MaxName = 150;

    public async Task<InviteProductUserResult> HandleAsync(InviteProductUserCommand cmd, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var email = EmailNormalizer.Normalize(cmd.Email ?? string.Empty);
        if (!IsValidEmail(email))
            throw ProductRoleRules.Fail(ProductRoleErrorCodes.CorreoInvalido, ProductRoleErrorKind.InvalidArgument, "Correo inválido.");
        var name = cmd.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > MaxName)
            throw ProductRoleRules.Fail(ProductRoleErrorCodes.NombreRequerido, ProductRoleErrorKind.InvalidArgument, "El nombre es obligatorio (hasta 150 caracteres).");
        ProductRoleRules.EnsureAllowedRole(cmd.ProductCode, cmd.RoleCode);
        ProductRoleRules.EnsureCanGrant(cmd.Actor, cmd.ProductCode, cmd.RoleCode);

        var role = await repo.FindAssignableRoleAsync(cmd.ProductCode, cmd.RoleCode, ct).ConfigureAwait(false)
            ?? throw ProductRoleRules.Fail(ProductRoleErrorCodes.RolNoPermitido, ProductRoleErrorKind.InvalidArgument, "El rol no está disponible en el catálogo.");

        var target = await repo.ResolveInviteTargetAsync(cmd.TenantId, email, cmd.ProductCode, ct).ConfigureAwait(false);
        switch (target.Kind)
        {
            case InviteTargetKind.MemberWithoutRole when target.UserId is { } userId:
                await repo.AddProductRoleAsync(cmd.TenantId, userId, role.Id, cmd.Actor.UserId, ct).ConfigureAwait(false);
                return new InviteProductUserResult(InviteOutcome.RoleAssigned, userId, EmailSent: false);
            case InviteTargetKind.MemberWithRole:
                return new InviteProductUserResult(InviteOutcome.AlreadyWithRole, target.UserId, EmailSent: false);
            case InviteTargetKind.PendingInvitation:
                return new InviteProductUserResult(InviteOutcome.PendingInvitation, null, EmailSent: false);
            case InviteTargetKind.OtherTenant:
                return new InviteProductUserResult(InviteOutcome.OtherTenant, null, EmailSent: false);
            case InviteTargetKind.ProtectedMember:
                return new InviteProductUserResult(InviteOutcome.ProtectedTarget, target.UserId, EmailSent: false);
            default:
                var sent = await sender.SendAsync(cmd.TenantId, email, name, role.Id, cmd.Actor.UserId, ct).ConfigureAwait(false);
                return sent is null
                    ? new InviteProductUserResult(InviteOutcome.PendingInvitation, null, EmailSent: false)
                    : new InviteProductUserResult(InviteOutcome.InvitationCreated, sent.InvitationId, sent.EmailSent);
        }
    }

    internal static bool IsValidEmail(string email)
    {
        if (email.Length is 0 or > MaxEmail || email.Contains(' ', StringComparison.Ordinal))
            return false;
        if (!MailAddress.TryCreate(email, out var parsed) || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
            return false;
        var at = email.LastIndexOf('@');
        return at > 0 && email.IndexOf('.', at) > at + 1 && !email.EndsWith('.');
    }
}
