using Flit.Modules.Security.Domain.UserRoles.ProductRoles;

namespace Flit.Modules.Security.Application.Tests.UserRoles.ProductRoles;

/// <summary>
/// Repositorio en memoria de las operaciones por producto (ADR-0089). Modela asignaciones por (empresa, usuario, producto)
/// como en <c>uq_ura_active_user_tenant_product</c>: así los tests comprueban que el rol de otro producto se conserva.
/// </summary>
/// <remarks>Uso de ejemplo: <c>var repo = new FakeProductRoleRepository(); repo.Miembro(empresa, id, rolDiagnostico: "usuario_diagnostico", rolTramites: "user");</c></remarks>
internal sealed class FakeProductRoleRepository : IProductRoleRepository
{
    public const string Producto = "diagnostico";

    public static readonly ProductRoleRef UsuarioRol = new(Guid.Parse("00000000-0000-4000-8000-0000000000a1"), "usuario_diagnostico");
    public static readonly ProductRoleRef AdminRol = new(Guid.Parse("00000000-0000-4000-8000-0000000000a2"), "admin_diagnostico");
    public static readonly ProductRoleRef TramitesRol = new(Guid.Parse("00000000-0000-4000-8000-0000000000b1"), "user");

    /// <summary>Rol propio de la compañía con product_code = diagnostico: NO es de la lista blanca (CR-1).</summary>
    public static readonly ProductRoleRef RolPropio = new(Guid.Parse("00000000-0000-4000-8000-0000000000c1"), "rol_propio_diagnostico");

    /// <summary>Código de rol que pidió la guarda atómica del último admin en cada escritura (null = sin guarda).</summary>
    public List<string?> Guardas { get; } = [];

    private readonly Dictionary<Guid, Usuario> _usuarios = [];

    public List<string> Escrituras { get; } = [];

    public List<(Guid Id, Guid Tenant, string Nombre, string Email, string Rol)> Invitaciones { get; } = [];

    public HashSet<string> CorreosPendientes { get; } = new(StringComparer.Ordinal);

    public sealed class Usuario
    {
        public Guid Id { get; init; }
        public Guid Tenant { get; init; }
        public string Nombre { get; init; } = "Persona";
        public string Email { get; init; } = "persona@ejemplo.test";
        public bool CuentaActiva { get; set; } = true;
        public bool AdminCompany { get; init; }
        public bool SuperAdmin { get; init; }

        /// <summary>Asignaciones vigentes por producto (diagnostico, tramites…).</summary>
        public Dictionary<string, ProductRoleRef> Roles { get; } = new(StringComparer.Ordinal);

        public ProductRoleRef? Suspendido { get; set; }
    }

    public Usuario Miembro(Guid tenant, Guid id, string? rolDiagnostico = "usuario_diagnostico", string? rolTramites = null,
        bool adminCompany = false, bool superAdmin = false, string? suspendidoCon = null, string nombre = "Persona", string? email = null)
    {
        var u = new Usuario
        {
            Id = id, Tenant = tenant, Nombre = nombre, Email = email ?? $"{id:N}@ejemplo.test", AdminCompany = adminCompany, SuperAdmin = superAdmin,
        };
        if (rolDiagnostico is not null)
            u.Roles[Producto] = Rol(rolDiagnostico);
        if (rolTramites is not null)
            u.Roles["tramites"] = TramitesRol;
        if (suspendidoCon is not null)
            u.Suspendido = Rol(suspendidoCon);
        _usuarios[id] = u;
        return u;
    }

    public Usuario Get(Guid id) => _usuarios[id];

    private static ProductRoleRef Rol(string code) => code == AdminRol.Code ? AdminRol : code == UsuarioRol.Code ? UsuarioRol : RolPropio;

    public Task<ProductRoleRef?> FindAssignableRoleAsync(string productCode, string roleCode, CancellationToken ct) =>
        Task.FromResult<ProductRoleRef?>(productCode == Producto && roleCode == UsuarioRol.Code ? UsuarioRol
            : productCode == Producto && roleCode == AdminRol.Code ? AdminRol : null);

    public Task<ProductMemberSnapshot?> GetMemberAsync(Guid tenantId, Guid userId, string productCode, CancellationToken ct) =>
        Task.FromResult(_usuarios.TryGetValue(userId, out var u) && u.Tenant == tenantId ? Snapshot(u, productCode) : null);

    public Task<ProductInvitationSnapshot?> GetPendingInvitationAsync(Guid tenantId, Guid invitationId, string productCode, CancellationToken ct) =>
        Task.FromResult(Invitaciones.Where(i => i.Id == invitationId && i.Tenant == tenantId)
            .Select(i => new ProductInvitationSnapshot(i.Id, i.Nombre, i.Email, i.Rol)).FirstOrDefault());

    public Task<IReadOnlyList<ProductMemberSnapshot>> ListMembersAsync(Guid tenantId, string productCode, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProductMemberSnapshot>>(_usuarios.Values
            .Where(u => u.Tenant == tenantId && (u.Roles.ContainsKey(productCode) || u.Suspendido is not null))
            .Select(u => Snapshot(u, productCode)).ToList());

    public Task<IReadOnlyList<ProductInvitationSnapshot>> ListPendingInvitationsAsync(Guid tenantId, string productCode, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProductInvitationSnapshot>>(Invitaciones.Where(i => i.Tenant == tenantId)
            .Select(i => new ProductInvitationSnapshot(i.Id, i.Nombre, i.Email, i.Rol)).ToList());

    public Task<int> CountOtherActiveAdminsAsync(Guid tenantId, string adminRoleCode, Guid excludingUserId, CancellationToken ct) =>
        Task.FromResult(_usuarios.Values.Count(u => u.Tenant == tenantId && u.Id != excludingUserId && u.CuentaActiva
            && u.Roles.TryGetValue(Producto, out var r) && r.Code == adminRoleCode));

    public Task ReplaceProductRoleAsync(Guid tenantId, Guid userId, string productCode, Guid roleId, Guid actorId, string? lastAdminGuardRoleCode, CancellationToken ct)
    {
        Guardas.Add(lastAdminGuardRoleCode);
        _usuarios[userId].Roles[productCode] = roleId == AdminRol.Id ? AdminRol : UsuarioRol;
        Escrituras.Add($"replace:{userId}:{productCode}");
        return Task.CompletedTask;
    }

    public Task AddProductRoleAsync(Guid tenantId, Guid userId, Guid roleId, Guid actorId, CancellationToken ct)
    {
        _usuarios[userId].Roles[Producto] = roleId == AdminRol.Id ? AdminRol : UsuarioRol;
        Escrituras.Add($"add:{userId}");
        return Task.CompletedTask;
    }

    public Task SuspendAsync(Guid tenantId, Guid userId, string productCode, Guid roleId, Guid actorId, string? lastAdminGuardRoleCode, CancellationToken ct)
    {
        Guardas.Add(lastAdminGuardRoleCode);
        var u = _usuarios[userId];
        u.Suspendido = u.Roles[productCode];
        u.Roles.Remove(productCode);
        Escrituras.Add($"suspend:{userId}");
        return Task.CompletedTask;
    }

    public Task ReactivateAsync(Guid tenantId, Guid userId, string productCode, Guid roleId, Guid actorId, CancellationToken ct)
    {
        var u = _usuarios[userId];
        u.Roles[productCode] = roleId == AdminRol.Id ? AdminRol : UsuarioRol;
        u.Suspendido = null;
        Escrituras.Add($"reactivate:{userId}");
        return Task.CompletedTask;
    }

    public Task ClearSuspensionAsync(Guid tenantId, Guid userId, string productCode, CancellationToken ct)
    {
        _usuarios[userId].Suspendido = null;
        Escrituras.Add($"clear:{userId}");
        return Task.CompletedTask;
    }

    public Task<InviteTarget> ResolveInviteTargetAsync(Guid tenantId, string normalizedEmail, string productCode, CancellationToken ct)
    {
        var u = _usuarios.Values.FirstOrDefault(x => x.Email == normalizedEmail);
        if (u is null)
            return Task.FromResult(new InviteTarget(CorreosPendientes.Contains(normalizedEmail) ? InviteTargetKind.PendingInvitation : InviteTargetKind.NewEmail));
        if (u.Tenant != tenantId)
            return Task.FromResult(new InviteTarget(InviteTargetKind.OtherTenant));
        if (u.AdminCompany || u.SuperAdmin)
            return Task.FromResult(new InviteTarget(InviteTargetKind.ProtectedMember, u.Id));
        return Task.FromResult(u.Roles.ContainsKey(productCode) || u.Suspendido is not null
            ? new InviteTarget(InviteTargetKind.MemberWithRole, u.Id)
            : new InviteTarget(InviteTargetKind.MemberWithoutRole, u.Id));
    }

    public Task<IReadOnlyList<ProductTenantCount>> CountByTenantAsync(string productCode, IReadOnlyCollection<string> roleCodes, string adminRoleCode, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProductTenantCount>>(_usuarios.Values.GroupBy(u => u.Tenant)
            .Select(g => new ProductTenantCount(g.Key, g.Count(u => (u.Roles.TryGetValue(productCode, out var x) && roleCodes.Contains(x.Code))
                    || (u.Suspendido is { } s && roleCodes.Contains(s.Code))),
                g.Count(u => u.Roles.TryGetValue(productCode, out var r) && r.Code == adminRoleCode))).ToList());

    private static ProductMemberSnapshot Snapshot(Usuario u, string productCode) =>
        new(u.Id, u.Nombre, u.Email, u.CuentaActiva, u.AdminCompany, u.SuperAdmin,
            u.Roles.TryGetValue(productCode, out var r) ? r : null, u.Suspendido);
}
