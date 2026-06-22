namespace Flit.Admin.Application.Companies.SetCompanyStatus;

/// <summary>Cuerpo de <c>PUT /api/v1/admin/companies/{tenantId}/status</c>.</summary>
/// <param name="EstadoActivo"><c>true</c> activa la compañía, <c>false</c> la desactiva.</param>
public sealed record SetCompanyStatusRequest(bool EstadoActivo);
