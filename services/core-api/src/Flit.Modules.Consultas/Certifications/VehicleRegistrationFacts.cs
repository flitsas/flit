namespace Flit.Tramites.Domain.Certifications;

/// <summary>
/// Datos de matrícula del vehículo que no pertenecen ni al SOAT ni a la RTM pero condicionan la tabla.
/// </summary>
/// <remarks>
/// <see cref="FechaMatricula"/> es lo que decide si la revisión técnico-mecánica ya es exigible
/// (<c>RtmCertificado.Aplica</c>: un vehículo nuevo no la debe todavía). Hoy esa llave no se escribe
/// nunca —el proveedor primario la manda en <c>vehiculo.fechaRegistro</c> y el DTO no la modela— y por
/// eso el bloque queda permanentemente en "no aplica".
/// </remarks>
public sealed record VehicleRegistrationFacts(CertifiedDate FechaMatricula)
{
    public static readonly VehicleRegistrationFacts Empty = new(CertifiedDate.Empty);

    public bool HasAnyValue => FechaMatricula.HasValue;

    public IReadOnlyList<string> NormalizationIssues() =>
        CertificationKeys.Unresolved((nameof(FechaMatricula), FechaMatricula));
}
