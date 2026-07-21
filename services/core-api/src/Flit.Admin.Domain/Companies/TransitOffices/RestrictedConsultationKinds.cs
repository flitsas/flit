namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Vocabulario cerrado de tipos de consulta que una compañía puede inhabilitar para un
/// Organismo de Tránsito puntual (HU #10759). Refleja el CHECK de
/// <c>admin.tenant_transit_office_consultation_restrictions.consultation_kind</c>
/// (33-F05-ot-consultation-restrictions.sql).
///
/// ADVERTENCIA — colisión de vocabulario: NO es lo mismo que
/// <c>Flit.Tramites.Application.UseCases.Consultations.ConsultationKind</c>
/// (<c>vehicle_vin|vehicle_plate|conductor</c>), que enumera el TIPO de dato consultado en
/// el motor de consultas del trámite. Este vocabulario enumera la POLÍTICA COMERCIAL que
/// una compañía puede inhabilitar por OT. Prohibido importar <c>ConsultationKindKeys</c>
/// de Tramites.Application en Admin — son conceptos distintos que coinciden en nombre.
///
/// "vehicle" está deliberadamente fuera de este vocabulario: restringir la consulta de
/// vehículo rompería la hidratación de field_values y la generación del FUR.
/// </summary>
public static class RestrictedConsultationKinds
{
    public const string Rnmc = "rnmc";

    public const string Fines = "fines";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Rnmc,
        Fines,
    };

    public static bool IsValid(string? kind) => !string.IsNullOrWhiteSpace(kind) && All.Contains(kind);
}
