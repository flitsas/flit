using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;

namespace Flit.Integration.Tests.Tenancy;

/// <summary>
/// HU #12322 (Feature #12254, Épica #12235) — escenario canónico de la jerarquía de clientes para
/// las suites de paridad y de fuga:
/// <list type="bullet">
///   <item><b>P</b> cabeza de grupo (<c>is_group_parent = true</c> y <c>tenant_type = CONCESION</c>:
///   desde HU #12406 la clase de la cabeza es su tipo y <c>ck_tenants_group_parent_by_type</c> los
///   acopla) con dos hijos <b>C1</b> y <b>C2</b> (<c>parent_tenant_id = P</c>).</item>
///   <item><b>X</b> cliente ajeno a la red (sin padre ni hijos).</item>
///   <item><b>S</b> cliente aislado, sin jerarquía: el «cliente de antes del Feature» (AC1).</item>
///   <item><b>O</b> tenant de un organismo de tránsito con perfil sobre la OT <c>Ot1</c>, que solo
///   tiene grant habilitado hacia C1 (AC6: grants de OT).</item>
/// </list>
/// Cada cliente recibe <b>los mismos datos propios</b> en cada tabla que alimenta las consultas del
/// inventario (<see cref="CoveredQueries"/>): un usuario, dos trámites (uno entregado a Ot1 con la
/// placa compartida <see cref="SharedPlate"/> y un borrador), su historial de estado, una bitácora de
/// notificación, un parámetro documental, un convenio con OT, una invitación pendiente y una entrada de
/// auditoría. Con datos simétricos, cualquier fila «de más» en una lectura es una fuga y no un artefacto
/// de la semilla.
/// <para>
/// UUIDs fijos y reconocibles (<c>a0000000-…</c>): bloque 0000 = tenants, 0001 = OT de catálogo,
/// 0002 = usuarios, 0003 = trámites, 0004..0009 = filas satélite. Nunca coinciden con los de
/// <c>SeedMockCompanies</c> (que además <see cref="PostgresDatabaseFixture.ResetAsync"/> trunca).
/// </para>
/// </summary>
internal static class HierarchyScenario
{
    public const string SharedPlate = "ABC123";

    public static readonly Guid P = TenantId(0x01);
    public static readonly Guid C1 = TenantId(0x11);
    public static readonly Guid C2 = TenantId(0x12);
    public static readonly Guid X = TenantId(0x99);
    public static readonly Guid S = TenantId(0x55);
    public static readonly Guid O = TenantId(0xEE);

    /// <summary>OT de catálogo con perfil de organismo (tenant <see cref="O"/>) y grant hacia C1.</summary>
    public static readonly Guid Ot1 = Id(0x0001, 0, 1);

    /// <summary>OT de catálogo sin organismo detrás: recibe los grants de los demás clientes.</summary>
    public static readonly Guid Ot2 = Id(0x0001, 0, 2);

    /// <summary>Los cinco clientes con datos sembrados, en orden estable.</summary>
    public static readonly IReadOnlyList<Guid> Clients = [P, C1, C2, X, S];

    /// <summary>Todo tenant del escenario menos <paramref name="own"/>.</summary>
    public static IReadOnlyList<Guid> Others(Guid own) => Clients.Where(t => t != own).ToList();

    public static string CodeOf(Guid tenantId) => tenantId switch
    {
        _ when tenantId == P => "P",
        _ when tenantId == C1 => "C1",
        _ when tenantId == C2 => "C2",
        _ when tenantId == X => "X",
        _ when tenantId == S => "S",
        _ when tenantId == O => "O",
        _ => tenantId.ToString(),
    };

    public static Guid UserOf(Guid tenantId) => Id(0x0002, Suffix(tenantId), 1);

    /// <summary>Trámite entregado a <see cref="Ot1"/> con la placa compartida.</summary>
    public static Guid DeliveredProcedureOf(Guid tenantId) => Id(0x0003, Suffix(tenantId), 1);

    /// <summary>Borrador propio del cliente (placa distinta por tenant).</summary>
    public static Guid DraftProcedureOf(Guid tenantId) => Id(0x0003, Suffix(tenantId), 2);

    public static IReadOnlyList<Guid> ProceduresOf(Guid tenantId) =>
        [DeliveredProcedureOf(tenantId), DraftProcedureOf(tenantId)];

    /// <summary>Radicado numérico único por trámite (índice único global, sin tenant).</summary>
    public static string ReferenceOf(Guid tenantId, int n) => $"{Suffix(tenantId) * 10 + n:D6}";

    /// <summary>
    /// Siembra el escenario completo. Devuelve el tipo de trámite usado (uno del catálogo preservado).
    /// Cada bloque va en su propio <c>SaveChanges</c> porque los triggers <c>BEFORE</c> de
    /// <c>identity.tenants</c> exigen que el padre exista antes que sus hijos.
    /// </summary>
    public static async Task<ProcedureType> SeedAsync(PostgresDatabaseFixture fixture)
    {
        await using var ctx = fixture.CreateDbContext();

        var type = await ctx.ProcedureTypes.AsNoTracking()
            .Where(t => t.Code == "MATRICULA_NUEVA")
            .FirstOrDefaultAsync()
            ?? await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).FirstAsync();

        ctx.TransitOffices.AddRange(
            NewOffice(Ot1, "11001000", "BOGOTA", "11", "11001"),
            NewOffice(Ot2, "5001000", "MEDELLIN", "05", "05001"));

        ctx.Tenants.Add(NewTenant(P, "IT-P", isGroupParent: true, parentId: null));
        ctx.Tenants.Add(NewTenant(X, "IT-X", isGroupParent: false, parentId: null));
        ctx.Tenants.Add(NewTenant(S, "IT-S", isGroupParent: false, parentId: null));
        var ot = NewTenant(O, "IT-OT", isGroupParent: false, parentId: null);
        ot.TenantType = "FLIT"; // ck_tenants_tenant_type (RENTING, CONCESIONARIO, FLIT); la condición de OT es el perfil, no el tipo
        ctx.Tenants.Add(ot);
        await ctx.SaveChangesAsync();

        ctx.Tenants.Add(NewTenant(C1, "IT-C1", isGroupParent: false, parentId: P));
        ctx.Tenants.Add(NewTenant(C2, "IT-C2", isGroupParent: false, parentId: P));
        await ctx.SaveChangesAsync();

        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Id(0x0004, Suffix(O), 1),
            TenantId = O,
            TransitOfficeId = Ot1,
            OperationMode = "dashboard",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // Por etapas: EF solo ordena los INSERT entre entidades con navegación configurada, y
        // procedure_instances.created_by_user_id → identity.users no la tiene (igual que las filas
        // satélite → procedure_instances). Cada etapa referencia solo filas ya confirmadas.
        foreach (var tenant in Clients)
            AddUser(ctx, tenant);
        await ctx.SaveChangesAsync();

        foreach (var tenant in Clients)
            AddProcedures(ctx, tenant, type.Id);
        await ctx.SaveChangesAsync();

        foreach (var tenant in Clients)
            AddSatelliteRows(ctx, tenant);
        await ctx.SaveChangesAsync();

        return type;
    }

    private static void AddUser(FlitDbContext ctx, Guid tenant)
    {
        var code = CodeOf(tenant);
        ctx.Users.Add(new User
        {
            Id = UserOf(tenant),
            Email = $"it-{code.ToLowerInvariant()}@flit.test",
            DisplayName = $"Gestor {code}",
            Status = "active",
            HomeTenantId = tenant,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });
    }

    private static void AddProcedures(FlitDbContext ctx, Guid tenant, Guid procedureTypeId)
    {
        var code = CodeOf(tenant);
        var suffix = Suffix(tenant);
        var user = UserOf(tenant);
        var now = DateTimeOffset.UtcNow;

        var delivered = new ProcedureInstance
        {
            Id = DeliveredProcedureOf(tenant),
            TenantId = tenant,
            ProcedureTypeId = procedureTypeId,
            ReferenceNumber = ReferenceOf(tenant, 1),
            Status = TramiteEstado.Entregado,
            TransitOfficeId = Ot1,
            Plate = SharedPlate,
            Vin = $"VIN{suffix:X2}{1:D12}",
            VendedorNombre = $"Vendedor {code}",
            CompradorNombre = $"Comprador {code}",
            CreatedByUserId = user,
            CreatedAt = now.AddDays(-2),
            SubmittedAt = now.AddDays(-1),
        };
        var draft = new ProcedureInstance
        {
            Id = DraftProcedureOf(tenant),
            TenantId = tenant,
            ProcedureTypeId = procedureTypeId,
            ReferenceNumber = ReferenceOf(tenant, 2),
            Status = TramiteEstado.Borrador,
            Plate = $"{code}999",
            Vin = $"VIN{suffix:X2}{2:D12}",
            CreatedByUserId = user,
            CreatedAt = now.AddDays(-1),
        };
        ctx.ProcedureInstances.AddRange(delivered, draft);
    }

    private static void AddSatelliteRows(FlitDbContext ctx, Guid tenant)
    {
        var code = CodeOf(tenant);
        var suffix = Suffix(tenant);
        var user = UserOf(tenant);
        var now = DateTimeOffset.UtcNow;
        var delivered = new { Id = DeliveredProcedureOf(tenant) };

        ctx.ProcedureInstanceStatusHistories.AddRange(
            new ProcedureInstanceStatusHistory
            {
                Id = Id(0x0005, suffix, 1),
                TenantId = tenant,
                ProcedureInstanceId = delivered.Id,
                FromStatus = TramiteEstado.Borrador,
                ToStatus = TramiteEstado.Preparado,
                ChangedAt = now.AddDays(-2).AddHours(1),
                ChangedBy = user,
            },
            new ProcedureInstanceStatusHistory
            {
                Id = Id(0x0005, suffix, 2),
                TenantId = tenant,
                ProcedureInstanceId = delivered.Id,
                FromStatus = TramiteEstado.Preparado,
                ToStatus = TramiteEstado.Entregado,
                ChangedAt = now.AddDays(-1),
                ChangedBy = user,
            });

        ctx.NotificationDeliveryLogs.Add(new NotificationDeliveryLogEntity
        {
            Id = Id(0x0006, suffix, 1),
            TenantId = tenant,
            TemplateKey = "tramite.entregado",
            Channel = "email",
            Recipient = $"it-{code.ToLowerInvariant()}@flit.test",
            Result = "enviado",
            DurationMs = 10,
            OccurredAt = now.AddDays(-1),
            CreatedAt = now.AddDays(-1),
        });

        ctx.CompanyDocumentParams.Add(new CompanyDocumentParamEntity
        {
            Id = Id(0x0007, suffix, 1),
            TenantId = tenant,
            DocumentTypeCode = "SOAT",
            State = "OBLIGATORIO",
            CreatedAt = now,
        });

        ctx.CompanyTransitOfficeAgreements.Add(new CompanyTransitOfficeAgreement
        {
            Id = Id(0x0008, suffix, 1),
            CompanyTenantId = tenant,
            TransitOfficeId = Ot1,
            IsActive = true,
            CreatedAt = now,
        });

        // Grants de OT: solo C1 está habilitado en Ot1 (la OT con organismo); el resto en Ot2.
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Id(0x0009, suffix, 1),
            TenantId = tenant,
            TransitOfficeId = tenant == C1 ? Ot1 : Ot2,
            IsEnabled = true,
            CreatedAt = now,
        });

        ctx.UserInvitations.Add(new UserInvitation
        {
            Id = Id(0x000A, suffix, 1),
            TenantId = tenant,
            Email = $"invitado-{code.ToLowerInvariant()}@flit.test",
            FullName = $"Invitado {code}",
            TokenHash = $"hash-{code}",
            Status = "pending",
            InvitedBy = user,
            ExpiresAt = now.AddDays(7),
            CreatedAt = now,
        });

        ctx.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Id(0x000B, suffix, 1),
            TenantId = tenant,
            EntityName = "tenant",
            FieldName = "legal_name",
            OldValue = null,
            NewValue = $"\"Cliente {code}\"", // columna jsonb
            ChangedAt = now,
            ChangedBy = user,
            Operation = "update",
            Result = "success",
            Module = "companies",
        });
    }

    /// <summary><see cref="TenantSeed.New"/> con NIT único por sufijo (los ids del escenario comparten prefijo y <c>uq_tenants_tax_id</c> lo rechazaría).</summary>
    private static Tenant NewTenant(Guid id, string code, bool isGroupParent, Guid? parentId)
    {
        var tenant = TenantSeed.New(id, code, isGroupParent, parentId);
        tenant.TaxId = $"9{Suffix(id):D14}";
        return tenant;
    }

    private static TransitOffice NewOffice(Guid id, string code, string name, string department, string city) => new()
    {
        Id = id,
        Code = code,
        Name = $"SECRETARIA DE MOVILIDAD DE {name}",
        DepartmentCode = department,
        CityCode = city,
        IsActive = true,
    };

    /// <summary>
    /// Tenant dueño de cualquier id sembrado por este escenario (todos codifican el sufijo del tenant
    /// en los bytes finales). Para un id ajeno al escenario devuelve <see cref="Guid.Empty"/>, que
    /// ningún alcance permite: una fila desconocida también cuenta como fuga.
    /// </summary>
    public static Guid OwnerOf(Guid id)
    {
        var text = id.ToString("N");
        if (!text.StartsWith("a0000000", StringComparison.Ordinal))
            return Guid.Empty;

        var suffix = int.Parse(text[^4..^2], System.Globalization.NumberStyles.HexNumber);
        return Clients.Concat([O]).FirstOrDefault(t => Suffix(t) == suffix);
    }

    private static Guid TenantId(int suffix) => Id(0x0000, suffix, 0);

    private static int Suffix(Guid tenantId) => int.Parse(tenantId.ToString("N")[^4..^2], System.Globalization.NumberStyles.HexNumber);

    /// <summary><c>a0000000-{block:x4}-4000-8000-0000000{suffix:x2}{n:x2}</c>.</summary>
    private static Guid Id(int block, int suffix, int n) =>
        new($"a0000000-{block:x4}-4000-8000-00000000{suffix:x2}{n:x2}");
}
