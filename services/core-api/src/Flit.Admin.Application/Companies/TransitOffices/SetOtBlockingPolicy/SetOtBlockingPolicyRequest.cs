namespace Flit.Admin.Application.Companies.TransitOffices.SetOtBlockingPolicy;

/// <summary>
/// Cuerpo de <c>PUT /api/v1/admin/companies/{tenantId}/ot-blocking-policies/{transitOfficeId}/{criterion}</c>.
/// Transporta el ESTADO DESEADO (no un verbo): <c>true</c> el criterio BLOQUEA (fail→rojo, subsanable),
/// <c>false</c> solo ADVIERTE (warn→amarillo, el usuario decide continuar) para ese tenant + OT.
/// </summary>
public sealed record SetOtBlockingPolicyRequest(bool Blocks);
