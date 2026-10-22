using System.Globalization;
using Flit.Queries.Domain.Time;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.ExternalSync;

/// <summary>
/// HU #13079 — convierte la fila cruda de la base en el ítem del contrato v3.1 §4. Función pura.
/// <list type="bullet">
///   <item>Texto: se recorta y el vacío pasa a <c>null</c> (nunca <c>""</c> ni <c>" "</c>).</item>
///   <item>Números del vehículo con <see cref="int.TryParse(string?, NumberStyles, IFormatProvider?, out int)"/>:
///   si no es un entero, <c>null</c>. Solo el cilindraje conserva el texto crudo en
///   <c>cilindrajeTexto</c> (decisión del PO: <c>modeloAno</c> y <c>capacidad</c> no llevan texto).</item>
///   <item><c>tipoServicio</c>: <c>null</c> sin dato (decisión del PO: no se asume PARTICULAR, que es el
///   valor por defecto del formulario); con dato, <see cref="VehicleServiceTypeCode.Resolve"/>.</item>
///   <item><c>compradores</c>: por ordinal; <c>porcentajeParticipacion</c> en <c>null</c> con un solo actor.</item>
///   <item>Tombstone (<c>eliminado</c>): <c>vehiculo</c>, <c>organismo</c> y <c>factura</c> en <c>null</c> y
///   <c>compradores</c> vacío; el resto se mantiene.</item>
///   <item>Fechas con el offset de Colombia (-05:00).</item>
/// </list>
/// </summary>
public static class ProcedureSyncItemMapper
{
    public static ProcedureSyncItem Map(ProcedureSyncRow row, IReadOnlyDictionary<string, string> serviceTypeNames)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(serviceTypeNames);

        var eliminado = row.IsDeleted;

        return new ProcedureSyncItem(
            row.Id,
            row.ReferenceNumber,
            row.Consecutivo,
            row.Position.Version,
            ColombiaTime.From(row.ChangedAt),
            eliminado,
            row.Status.Trim().ToLowerInvariant(),
            new ProcedureSyncTramite(row.ProcedureTypeCode, row.ProcedureTypeName, Texto(row.ProcedureTypeFamily)),
            ColombiaTime.From(row.CreatedAt),
            Fecha(row.SubmittedAt),
            Fecha(row.ApprovedAt),
            eliminado ? null : Vehiculo(row, serviceTypeNames),
            eliminado ? null : Organismo(row),
            eliminado ? [] : Compradores(row.Actors),
            eliminado || row.InvoiceAttachmentId is null || row.InvoiceUploadedAt is null
                ? null
                : new ProcedureSyncFactura(row.InvoiceAttachmentId.Value, Texto(row.InvoiceFilename), ColombiaTime.From(row.InvoiceUploadedAt.Value)),
            new ProcedureSyncCompania(row.TenantId, Texto(row.TenantTaxId), Texto(row.TenantLegalName)));
    }

    private static ProcedureSyncVehiculo Vehiculo(ProcedureSyncRow row, IReadOnlyDictionary<string, string> serviceTypeNames)
    {
        var cilindrajeCrudo = Texto(row.VehicleEngineDisplacement);
        var cilindraje = Entero(cilindrajeCrudo);

        return new ProcedureSyncVehiculo(
            Texto(row.Vin),
            Texto(row.Plate),
            Texto(row.VehicleClass),
            Texto(row.VehicleBrand),
            Texto(row.VehicleLine),
            Entero(Texto(row.VehicleYear)),
            Texto(row.VehicleBodyType),
            cilindraje,
            cilindraje is null ? cilindrajeCrudo : null,
            Entero(Texto(row.VehiclePassengers)),
            Texto(row.VehicleEngineNumber),
            Texto(row.VehicleSeries),
            TipoServicio(Texto(row.VehicleService), serviceTypeNames));
    }

    private static ProcedureSyncTipoServicio? TipoServicio(string? crudo, IReadOnlyDictionary<string, string> serviceTypeNames)
    {
        if (crudo is null)
        {
            return null;
        }

        var codigo = VehicleServiceTypeCode.Resolve(crudo);
        return codigo is null
            ? null
            : new ProcedureSyncTipoServicio(codigo, serviceTypeNames.TryGetValue(codigo, out var nombre) ? nombre : null);
    }

    private static ProcedureSyncOrganismo? Organismo(ProcedureSyncRow row)
    {
        var organismo = new ProcedureSyncOrganismo(
            Texto(row.TransitOfficeCode),
            Texto(row.TransitOfficeName),
            Texto(row.TransitOfficeCityCode),
            Texto(row.TransitOfficeCityName),
            Texto(row.TransitOfficeDepartmentName));

        return organismo is { CodigoTransito: null, Nombre: null, CodigoSecretaria: null, Ciudad: null, Departamento: null }
            ? null
            : organismo;
    }

    private static List<ProcedureSyncComprador> Compradores(IReadOnlyList<ProcedureSyncActorRow> actores)
    {
        var unico = actores.Count == 1;
        return actores
            .OrderBy(a => a.Ordinal)
            .Select(a => new ProcedureSyncComprador(
                a.Ordinal,
                unico ? null : a.OwnershipPercentage,
                a.ActorType.Trim().ToLowerInvariant(),
                Texto(a.PersonType),
                Texto(a.DocumentType),
                Texto(a.DocumentNumber),
                Texto(a.FullName),
                Texto(a.Address),
                Texto(a.City),
                Texto(a.Phone),
                Texto(a.Email)))
            .ToList();
    }

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static int? Entero(string? valor) =>
        valor is not null && int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static DateTimeOffset? Fecha(DateTimeOffset? valor) => valor is { } v ? ColombiaTime.From(v) : null;
}
