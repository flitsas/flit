namespace Flit.Admin.Application.Companies.Invitations;

/// <summary>
/// HU #12354 AC3 — roles permitidos al invitar a un cliente hijo (por código de catálogo global).
/// Excluye SuperAdmin, ot_admin y cualquier rol fuera de esta lista. La validación en runtime compara
/// el <c>roleId</c> recibido contra los ids resueltos de estos códigos (no por jerarquía calculada).
/// </summary>
public static class GroupHeadChildInvitationRoles
{
    public const string AdminCompany = "AdminCompany";

    public const string Radicador = "Radicador";

    public const string Gestor = "Gestor";

    public const string Documentador = "Documentador";

    public const string Validador = "Validador";

    public const string OperarioFull = "OperarioFull";

    public static readonly IReadOnlyList<string> AllowedCodes =
    [
        AdminCompany,
        Radicador,
        Gestor,
        Documentador,
        Validador,
        OperarioFull,
    ];

    public static readonly IReadOnlySet<string> ForbiddenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SuperAdmin",
        "ot_admin",
    };
}
