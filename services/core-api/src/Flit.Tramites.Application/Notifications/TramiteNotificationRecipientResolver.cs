using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.Notifications;

/// <summary>
/// HU #11462 — implementación canónica de la resolución de destinatarios.
/// </summary>
public sealed class TramiteNotificationRecipientResolver : ITramiteNotificationRecipientResolver
{
    public const string RoleComprador = "comprador";
    public const string RoleVendedor = "vendedor";
    public const string ModalidadTraspaso = "traspaso";

    public const string RoleLocatario = "locatario";
    public const string RoleRadicador = "radicador";
    public const string RoleConfiguracionEmpresa = "configuracion_empresa";

    public TramiteRecipientResolution Resolve(
        ProcedureInstance instance,
        IReadOnlyList<ProcedureInstanceActor> actors,
        IReadOnlyList<ProcedureInstanceParticipant> participants,
        TramiteStateEmailRecipientPolicy? policy = null,
        TramiteEmailRecipient? radicador = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(participants);

        policy ??= new TramiteStateEmailRecipientPolicy(
            Comprador: true, VendedorOPropietario: true, Radicador: false, ExtraEmail: null);

        var recipients = new List<TramiteEmailRecipient>();
        var gaps = new List<TramiteRecipientGap>();

        foreach (var role in RolesToNotify(policy))
        {
            var actor = actors.FirstOrDefault(a =>
                string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));
            if (actor is null)
            {
                continue;
            }

            var participant = participants.FirstOrDefault(p =>
                string.Equals(p.Rol, role, StringComparison.OrdinalIgnoreCase));

            if (TreatAsJuridical(actor))
            {
                ResolveJuridical(role, actor, recipients, gaps);
            }
            else
            {
                ResolveNatural(role, actor, participant, recipients, gaps);
            }
        }

        if (policy.Radicador)
        {
            if (radicador is not null && !string.IsNullOrWhiteSpace(radicador.Email))
            {
                recipients.Add(radicador);
            }
            else if (radicador is not null)
            {
                gaps.Add(new TramiteRecipientGap(
                    RoleRadicador, TramiteRecipientKind.Persona, radicador.DisplayName));
            }
        }

        var extra = TramiteStateEmailRecipientsNormalize(policy.ExtraEmail);
        if (extra is not null)
        {
            recipients.Add(new TramiteEmailRecipient(
                RoleConfiguracionEmpresa,
                TramiteRecipientKind.Persona,
                extra,
                extra));
        }

        return new TramiteRecipientResolution(recipients, gaps);
    }

    private static string? TramiteStateEmailRecipientsNormalize(string? extraEmail)
    {
        if (string.IsNullOrWhiteSpace(extraEmail))
            return null;
        return extraEmail.Trim();
    }

    private static IEnumerable<string> RolesToNotify(TramiteStateEmailRecipientPolicy policy)
    {
        if (policy.Comprador)
        {
            yield return RoleComprador;
            yield return RoleLocatario;
        }

        if (policy.VendedorOPropietario)
        {
            yield return RoleVendedor;
        }
    }

    /// <summary>
    /// PersonType jurídico, o legacy sin PersonType con documento NIT (decisión PO §5.3).
    /// </summary>
    internal static bool TreatAsJuridical(ProcedureInstanceActor actor)
    {
        if (ActorPersonTypes.IsJuridical(actor.PersonType))
            return true;
        if (ActorPersonTypes.IsNatural(actor.PersonType))
            return false;

        return string.Equals(actor.DocumentType?.Trim(), "NIT", StringComparison.OrdinalIgnoreCase);
    }

    private static void ResolveJuridical(
        string role,
        ProcedureInstanceActor actor,
        List<TramiteEmailRecipient> recipients,
        List<TramiteRecipientGap> gaps)
    {
        var empresaEmail = NormalizeEmail(actor.Email);
        var empresaName = actor.FullName?.Trim() ?? string.Empty;
        if (empresaEmail is not null)
        {
            recipients.Add(new TramiteEmailRecipient(
                role, TramiteRecipientKind.Empresa, empresaEmail, empresaName));
        }
        else
        {
            gaps.Add(new TramiteRecipientGap(role, TramiteRecipientKind.Empresa, empresaName));
        }

        // Cupo RL: SOLO metadata. Nunca actor.Email ni Participant (Ley 1581).
        var rl = IdentitySubjectResolver.ParseRepresentanteLegal(actor.Metadata);
        var rlEmail = NormalizeEmail(rl?.Email);
        var rlName = string.IsNullOrWhiteSpace(rl?.NombreCompleto)
            ? null
            : rl!.NombreCompleto!.Trim();

        if (rlEmail is not null)
        {
            recipients.Add(new TramiteEmailRecipient(
                role,
                TramiteRecipientKind.RepresentanteLegal,
                rlEmail,
                rlName ?? empresaName));
        }
        else
        {
            gaps.Add(new TramiteRecipientGap(
                role, TramiteRecipientKind.RepresentanteLegal, rlName));
        }
    }

    private static void ResolveNatural(
        string role,
        ProcedureInstanceActor actor,
        ProcedureInstanceParticipant? participant,
        List<TramiteEmailRecipient> recipients,
        List<TramiteRecipientGap> gaps)
    {
        // Precedencia: participante del portal → actor.
        var email = NormalizeEmail(participant?.Email) ?? NormalizeEmail(actor.Email);
        var name = !string.IsNullOrWhiteSpace(participant?.Nombre)
            ? participant!.Nombre.Trim()
            : (actor.FullName?.Trim() ?? string.Empty);

        if (email is not null)
        {
            recipients.Add(new TramiteEmailRecipient(
                role, TramiteRecipientKind.Persona, email, name));
        }
        else
        {
            gaps.Add(new TramiteRecipientGap(role, TramiteRecipientKind.Persona, name));
        }
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        return email.Trim();
    }
}
