namespace Flit.Api.Authorization;

/// <summary>
/// Constantes de autorización del módulo Admin (HU #10189, RF01).
/// </summary>
public static class AdminAuthorization
{
    /// <summary>Nombre de la policy que exige rol SuperAdmin.</summary>
    public const string SuperAdminPolicy = "SuperAdmin";

    /// <summary>Valor del claim de rol que concede acceso multi-tenant.</summary>
    public const string SuperAdminRole = "SuperAdmin";

    /// <summary>Nombre de la policy que permite acceso a AdminCompany o SuperAdmin.</summary>
    public const string AdminCompanyPolicy = "AdminCompany";

    /// <summary>Valor del claim de rol para administradores de compañía.</summary>
    public const string AdminCompanyRole = "AdminCompany";

    /// <summary>
    /// Tipo de claim donde viaja el rol en el JWT FLIT. Se configura como
    /// <c>RoleClaimType</c> para que <c>RequireRole</c> / <c>IsInRole</c> lo evalúen.
    /// </summary>
    public const string RoleClaimType = "role";

    /// <summary>Mensaje de error 403 cuando falta el rol SuperAdmin (AC3).</summary>
    public const string ForbiddenMessage = "Acceso restringido: se requiere rol SuperAdmin";

    /// <summary>Nombre de la policy que exige rol ot_admin (HU #10215).</summary>
    public const string OtAdminPolicy = "OtAdmin";

    /// <summary>
    /// Policy del módulo OT: catálogo <c>/admin/transit-offices</c> y API
    /// <c>/api/v1/admin/ot/*</c>. SuperAdmin y ot_admin (HU #10218 / #10236).
    /// </summary>
    public const string OtModulePolicy = "OtModule";

    /// <summary>Valor del claim de rol para administrador OT.</summary>
    public const string OtAdminRole = "ot_admin";

    /// <summary>Mensaje de error 403 cuando falta el rol ot_admin.</summary>
    public const string OtAdminForbiddenMessage = "Acceso restringido: se requiere rol ot_admin";

    /// <summary>Mensaje de error 403 para el módulo OT (SuperAdmin u ot_admin).</summary>
    public const string OtModuleForbiddenMessage =
        "Acceso restringido: se requiere rol SuperAdmin u ot_admin";

    /// <summary>Claim JWT del tenant autenticado.</summary>
    public const string TenantIdClaimType = "tenant_id";
}
