namespace Flit.Admin.Domain.Companies.TransitOffices.Create;

/// <summary>
/// Datos normalizados para dar de alta un tenant Organismo de Tránsito (OT). Lo
/// construye la capa de aplicación tras validar; el repositorio lo persiste creando,
/// en una sola operación, el <c>Tenant</c>, el rol de sistema <c>ot_admin</c> (sin
/// <c>RoleGrant</c>s — el SuperAdmin los cura después vía RBAC Admin) y el
/// <c>TransitOfficeProfile</c> que vincula el tenant con la oficina del catálogo.
/// </summary>
/// <param name="TransitOfficeId">Oficina del catálogo <c>catalogs.transit_offices</c> a vincular.</param>
/// <param name="LegalName">Razón social del OT — <c>identity.tenants.legal_name</c>.</param>
/// <param name="TaxId">NIT del OT — <c>identity.tenants.tax_id</c>.</param>
/// <param name="Code">Código único del tenant — <c>identity.tenants.code</c>.</param>
/// <param name="OperationMode">Modo de operación del perfil OT (<c>dashboard</c> | <c>quipux</c>).</param>
/// <param name="CreatedBy">Operador SuperAdmin que da de alta el OT (claim sub), si se conoce.</param>
public sealed record NewTransitOffice(
    Guid TransitOfficeId,
    string LegalName,
    string TaxId,
    string Code,
    string OperationMode,
    Guid? CreatedBy);
