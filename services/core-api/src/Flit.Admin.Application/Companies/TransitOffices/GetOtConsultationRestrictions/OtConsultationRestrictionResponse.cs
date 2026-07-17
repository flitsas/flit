namespace Flit.Admin.Application.Companies.TransitOffices.GetOtConsultationRestrictions;

/// <summary>Fila del listado de restricciones de consulta por OT de un tenant (AC1/AC5).</summary>
public sealed record OtConsultationRestrictionResponse(Guid TransitOfficeId, string ConsultationKind, bool Enabled);
