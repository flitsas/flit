using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Queries.Domain.Tenancy;

namespace Flit.Integration.Tests.Postgres;

/// <summary>
/// HU #12319 — fábrica de <see cref="Tenant"/> para las pruebas de jerarquía. Ids y códigos
/// deterministas (no datos reales) para que dos pruebas puedan sembrar EXACTAMENTE las mismas
/// llaves y demostrar el aislamiento (AC2).
/// </summary>
internal static class TenantSeed
{
    public static readonly Guid ParentId = new("11111111-1111-4111-8111-111111111111");
    public static readonly Guid ChildId = new("22222222-2222-4222-8222-222222222222");
    public static readonly Guid GrandchildId = new("33333333-3333-4333-8333-333333333333");
    public static readonly Guid LoneId = new("44444444-4444-4444-8444-444444444444");

    public static Tenant GroupParent(Guid? id = null, string code = "IT-PARENT") =>
        New(id ?? ParentId, code, isGroupParent: true, parentId: null);

    public static Tenant ChildOf(Guid parentId, Guid? id = null, string code = "IT-CHILD") =>
        New(id ?? ChildId, code, isGroupParent: false, parentId: parentId);

    public static Tenant Lone(Guid? id = null, string code = "IT-LONE") =>
        New(id ?? LoneId, code, isGroupParent: false, parentId: null);

    /// <summary>
    /// HU #12406: la clase de la cabeza es su <c>tenant_type</c> y <c>is_group_parent</c> va acoplado a
    /// él (<c>ck_tenants_group_parent_by_type</c>). Por defecto una cabeza nace <c>CONCESION</c> y quien
    /// no lo es, <c>CONCESIONARIO</c>; pasar <paramref name="tenantType"/> explícito para probar el
    /// CHECK (cabeza con tipo que no es de cabeza, tipo de cabeza sin marcar, MARCA_BLANCA…).
    /// </summary>
    public static Tenant New(Guid id, string code, bool isGroupParent, Guid? parentId, string? tenantType = null) => new()
    {
        Id = id,
        Code = code,
        LegalName = $"Cliente de integración {code}",
        TaxId = ("9" + id.ToString("N"))[..15], // uq_tenants_tax_id (DDL 53): único por id, determinista
        TenantType = tenantType ?? (isGroupParent ? GroupKindCodes.Concesion : "CONCESIONARIO"),
        IsActive = true,
        IsGroupParent = isGroupParent,
        ParentTenantId = parentId,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
