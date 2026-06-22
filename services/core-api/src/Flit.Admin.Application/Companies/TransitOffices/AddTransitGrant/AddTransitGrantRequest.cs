namespace Flit.Admin.Application.Companies.TransitOffices.AddTransitGrant;

/// <summary>Cuerpo del POST de alta de grant: <c>{ transitOfficeId }</c> (AC2).</summary>
public sealed record AddTransitGrantRequest(Guid? TransitOfficeId);
