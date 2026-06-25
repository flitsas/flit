namespace Flit.Admin.Application.Companies.UpdateCompany;

/// <summary>
/// Payload de la edición de compañía (PUT /api/v1/admin/companies/{tenantId}). Campos
/// en camelCase vía las System.Text.Json Web defaults. Todos nullable para validar
/// explícitamente (un campo ausente devuelve 422, no un 400 de binding). El
/// <c>code</c> es inmutable: no se acepta en la edición.
/// </summary>
/// <param name="RazonSocial">Razón social (legal_name). Requerido, ≤255.</param>
/// <param name="Nit">NIT (tax_id). Requerido, ≤20.</param>
/// <param name="TenantType">Tipo de compañía: RENTING | CONCESIONARIO | FLIT.</param>
/// <param name="EstadoActivo">Estado activo. Opcional (default true).</param>
/// <param name="RowVersion">
/// Versión de fila que el cliente leyó al abrir la edición. Opcional: si se envía y no
/// coincide con la versión actual del tenant, la edición se rechaza con 409 (otra persona
/// la modificó). Si es <c>null</c> no se verifica concurrencia.
/// </param>
public sealed record UpdateCompanyRequest(
    string? RazonSocial,
    string? Nit,
    string? TenantType,
    bool? EstadoActivo,
    long? RowVersion = null);
