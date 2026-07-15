namespace Flit.Admin.Application.Companies.TransitOffices.SetOtConsultationRestriction;

/// <summary>
/// Cuerpo de <c>PUT /api/v1/admin/companies/{tenantId}/ot-consultation-restrictions/{transitOfficeId}/{consultationKind}</c>.
/// Transporta el ESTADO DESEADO (no un verbo): <c>true</c> permite la consulta (default,
/// equivale a no tener fila), <c>false</c> la inhabilita para ese tenant + OT.
/// </summary>
public sealed record SetOtConsultationRestrictionRequest(bool Enabled);
