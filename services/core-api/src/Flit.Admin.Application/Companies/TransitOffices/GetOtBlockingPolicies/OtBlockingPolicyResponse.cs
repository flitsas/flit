namespace Flit.Admin.Application.Companies.TransitOffices.GetOtBlockingPolicies;

/// <summary>Fila del listado de políticas de bloqueo por OT de un tenant (FEATURE 05).</summary>
public sealed record OtBlockingPolicyResponse(Guid TransitOfficeId, string Criterion, bool Blocks);
