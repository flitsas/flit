using System.Globalization;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Enums;

namespace Flit.Tramites.Application.BulkTramites.Processing;

/// <summary>
/// Datos de una fila del Excel ya traducidos al vocabulario del wizard (HU #12523): lo que el paso 1
/// necesita para consultar el vehículo y lo que el paso de actores necesita para guardarlos.
/// </summary>
public sealed record BulkTramitesRowContext(
    Guid TenantId,
    Guid CreatedByUserId,
    string FamilyCode,
    string? ProcedureTypeCode,
    string? Vin,
    string? Plate,
    string? OwnerDocumentType,
    string? OwnerDocumentNumber,
    IReadOnlyList<ActorInput> Actors,
    /// <summary>
    /// NOMBRE del organismo de tránsito escrito en el Excel (solo matrícula). Se resuelve a id
    /// contra los habilitados de la empresa al procesar la fila; null en el resto de plantillas,
    /// donde el organismo lo impone el RUNT.
    /// </summary>
    string? TransitOfficeName = null);

/// <summary>
/// Traduce una fila del Excel a <see cref="BulkTramitesRowContext"/>. Es lógica pura y sin
/// dependencias a propósito: el mapeo columna→campo del wizard es justo donde un error pasa
/// desapercibido (un trámite creado con el documento del vendedor en la casilla del comprador no
/// falla, sale mal), así que se prueba aparte del procesamiento.
///
/// <para>En matrícula inicial el propietario se persiste con el rol <c>comprador</c>, igual que hace
/// el wizard (ver <c>PutActorsHandler</c>): no existe un rol «propietario» en el dominio.</para>
/// </summary>
public static class BulkTramitesRowMapper
{
    public const string MatriculaProcedureTypeCode = "MATRICULA_NUEVA";
    public const string TraspasoProcedureTypeCode = "TRASPASO_STANDARD";

    public static BulkTramitesRowContext Map(
        BulkTramitesTemplateType tipo,
        Guid tenantId,
        Guid createdByUserId,
        IReadOnlyDictionary<string, string?> values) => tipo switch
    {
        BulkTramitesTemplateType.Matricula => MapMatricula(tenantId, createdByUserId, values),
        BulkTramitesTemplateType.Traspaso => MapTraspaso(tenantId, createdByUserId, values),
        BulkTramitesTemplateType.Otros => MapOtros(tenantId, createdByUserId, values),
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de plantilla no soportado."),
    };

    private static BulkTramitesRowContext MapMatricula(
        Guid tenantId, Guid createdByUserId, IReadOnlyDictionary<string, string?> values)
    {
        // Matrícula inicial entra por VIN: el vehículo aún no tiene placa asignada. Hasta 4
        // propietarios (copropiedad, ADR-0053), todos con el rol con el que el dominio persiste al
        // titular en matrícula.
        var propietarios = new List<ActorInput>();
        for (var i = 1; i <= 4; i++)
        {
            var propietario = Actor(values, $"propietario_{i}", "comprador", i, Porcentaje(values, $"propietario_{i}"));
            if (propietario is not null)
            {
                propietarios.Add(propietario);
            }
        }

        return new BulkTramitesRowContext(
            tenantId,
            createdByUserId,
            ProcedureFamilyCodes.Matriculas,
            MatriculaProcedureTypeCode,
            Value(values, "vin"),
            Value(values, "placa"),
            OwnerDocumentType: null,
            OwnerDocumentNumber: null,
            propietarios,
            Value(values, BulkTramitesTemplateCatalog.OrganismoTransitoHeader));
    }

    private static BulkTramitesRowContext MapTraspaso(
        Guid tenantId, Guid createdByUserId, IReadOnlyDictionary<string, string?> values)
    {
        var actores = new List<ActorInput>();

        for (var i = 1; i <= 4; i++)
        {
            var comprador = Actor(values, $"comprador_{i}", "comprador", i, Porcentaje(values, $"comprador_{i}"));
            if (comprador is not null)
            {
                actores.Add(comprador);
            }
        }

        for (var i = 1; i <= 4; i++)
        {
            var vendedor = Actor(values, $"vendedor_{i}", "vendedor", i, Porcentaje(values, $"vendedor_{i}"));
            if (vendedor is not null)
            {
                actores.Add(vendedor);
            }
        }

        // El traspaso consulta por placa + documento del PROPIETARIO ACTUAL, que es el vendedor
        // principal (ordinal 1) — no el comprador, que todavía no figura en el RUNT.
        return new BulkTramitesRowContext(
            tenantId,
            createdByUserId,
            ProcedureFamilyCodes.Traspaso,
            TraspasoProcedureTypeCode,
            Value(values, "vin"),
            Value(values, "placa"),
            Value(values, "vendedor_1_tipo_documento"),
            Value(values, "vendedor_1_numero_documento"),
            actores);
    }

    private static BulkTramitesRowContext MapOtros(
        Guid tenantId, Guid createdByUserId, IReadOnlyDictionary<string, string?> values)
    {
        var actores = new List<ActorInput>();

        for (var i = 1; i <= 2; i++)
        {
            // El rol lo declara el usuario porque varía con el tipo de trámite; si no lo escribe, se
            // asume el del titular (comprador), que es el rol con el que el dominio persiste al
            // propietario.
            var rol = Value(values, $"actor_{i}_rol") ?? "comprador";
            var actor = Actor(values, $"actor_{i}", rol, ordinal: i, porcentaje: null);
            if (actor is not null)
            {
                actores.Add(actor);
            }
        }

        return new BulkTramitesRowContext(
            tenantId,
            createdByUserId,
            ProcedureFamilyCodes.Otros,
            Value(values, BulkTramitesTemplateCatalog.TipoTramiteHeader),
            Value(values, "vin"),
            Value(values, "placa"),
            Value(values, "actor_1_tipo_documento"),
            Value(values, "actor_1_numero_documento"),
            actores);
    }

    private static ActorInput? Actor(
        IReadOnlyDictionary<string, string?> values,
        string prefijo,
        string rol,
        int ordinal,
        decimal? porcentaje)
    {
        var numeroDocumento = Value(values, $"{prefijo}_numero_documento");
        if (numeroDocumento is null)
        {
            return null;
        }

        // El nombre sale VACÍO a propósito: la plantilla no lo pide y lo rellena el procesador con
        // el resultado de la consulta de persona al RUNT (ver BulkTramitesBatchProcessor). Un actor
        // que llegue al guardado con el nombre vacío es un error de flujo, no un dato faltante.
        return new ActorInput(
            rol.Trim().ToLowerInvariant(),
            Value(values, $"{prefijo}_tipo_documento") ?? "CC",
            numeroDocumento,
            NombreCompleto: string.Empty,
            Value(values, $"{prefijo}_email") ?? string.Empty,
            Value(values, $"{prefijo}_celular"),
            Ciudad: Value(values, $"{prefijo}_ciudad"),
            Direccion: Value(values, $"{prefijo}_direccion"),
            Ordinal: ordinal,
            Porcentaje: porcentaje);
    }

    /// <summary>
    /// Porcentaje del actor, o null si no aplica. Con un solo actor por lado el wizard exige null
    /// (<c>PutActorsHandler</c> lo ignora si viene), y el parser de HU #12522 ya rechazó las filas
    /// con reparto inválido: aquí solo se transcribe lo que el usuario escribió.
    /// </summary>
    private static decimal? Porcentaje(IReadOnlyDictionary<string, string?> values, string prefijo)
    {
        var crudo = Value(values, $"{prefijo}_porcentaje");
        return crudo is not null
            && decimal.TryParse(crudo, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor)
            ? valor
            : null;
    }

    private static string? Value(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
}
