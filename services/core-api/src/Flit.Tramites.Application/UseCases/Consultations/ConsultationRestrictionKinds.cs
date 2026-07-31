namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// FEATURE 05 — tipos de consulta que una compañía puede inhabilitar para un Organismo de Tránsito
/// puntual (<c>admin.tenant_transit_office_consultation_restrictions.consultation_kind</c>, creada en
/// HU #10759), vistos desde Trámites.
///
/// Espejo deliberado de <c>RestrictedConsultationKinds</c> (Flit.Admin.Domain): Application de
/// Trámites no referencia Admin.Domain — el cruce ocurre en Infraestructura, vía
/// <c>ConsultationRestrictionPolicy</c>, que sí ve ambos lados. Si cambian los valores del CHECK de
/// la columna, hay que tocar los dos.
///
/// ADVERTENCIA — colisión de vocabulario: NO es lo mismo que <see cref="ConsultationKind"/>
/// (<c>vehicle_vin|vehicle_plate|conductor</c>), que enumera el TIPO de dato consultado. Este
/// vocabulario enumera la POLÍTICA COMERCIAL que una compañía puede inhabilitar por OT.
///
/// "vehicle" está deliberadamente fuera: restringir la consulta de vehículo rompería la hidratación
/// de field_values y la generación del FUR.
/// </summary>
public static class ConsultationRestrictionKinds
{
    /// <summary>Consulta RNMC (medidas correctivas de la Policía).</summary>
    public const string Rnmc = "rnmc";

    /// <summary>Consulta de comparendos.</summary>
    public const string Fines = "fines";
}
