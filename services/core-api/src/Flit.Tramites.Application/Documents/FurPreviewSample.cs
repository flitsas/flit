using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Datos sintéticos para el simulador FUR (Plataforma → FUR). Marcadores visibles, no personas reales.
/// HU #11701.
/// </summary>
public static class FurPreviewSample
{
    public const string PhVendedorPn = "[ACÁ VA EL NOMBRE DEL VENDEDOR]";
    public const string PhVendedorPnDoc = "[DOC VENDEDOR]";
    public const string PhVendedorPj = "[ACÁ VA LA RAZÓN SOCIAL DEL VENDEDOR]";
    public const string PhVendedorNit = "[NIT VENDEDOR]";
    public const string PhCompradorPn = "[ACÁ VA EL NOMBRE DEL COMPRADOR]";
    public const string PhCompradorPnDoc = "[DOC COMPRADOR]";
    public const string PhCompradorPj = "[ACÁ VA LA RAZÓN SOCIAL DEL COMPRADOR]";
    public const string PhCompradorNit = "[NIT COMPRADOR]";
    public const string PhRlNombre = "[ACÁ VA EL NOMBRE DEL REPRESENTANTE LEGAL]";
    public const string PhRlDocumento = "[ACÁ VA EL DOCUMENTO DEL REPRESENTANTE LEGAL]";
    public const string PhPlaca = "ABC123";
    public const string PhAcreedor = "FONDEICON";
    public const string PhAcreedorNit = "900000000";
    public const string PhLocatario = "[NOMBRE LOCATARIO]";
    public const string PhLocatarioDoc = "[NUMERO LOCATARIO]";
    public const string PhLocatarioTipoDoc = "CC";
    public const string PhColorNuevo = "MULTICOLOR CON AEROGRAFIAS";

    public const string PersonNatural = "natural";
    public const string PersonJuridica = "juridica";

    public const string VehicleCarro = "carro";
    public const string VehicleMoto = "moto";
    public const string VehicleCamioneta = "camioneta";
    public const string VehicleRemolque = "remolque";
    public const string VehicleMaquinaria = "maquinaria";

    public static bool TryParsePersonKind(string? raw, out string kind)
    {
        kind = PersonNatural;
        var n = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (n is PersonNatural or PersonJuridica)
        {
            kind = n;
            return true;
        }

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        return false;
    }

    public static bool TryParseVehicleKind(string? raw, out string kind)
    {
        kind = VehicleCarro;
        var n = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (n is VehicleCarro or VehicleMoto or VehicleCamioneta or VehicleRemolque or VehicleMaquinaria)
        {
            kind = n;
            return true;
        }

        return false;
    }

    public static bool IsTraspaso(string? family, string? code)
    {
        var f = (family ?? string.Empty).Trim().ToUpperInvariant();
        var c = (code ?? string.Empty).Trim().ToUpperInvariant();
        return f == ProcedureFamilyCodes.Traspaso || c.Contains("TRASPASO", StringComparison.Ordinal);
    }

    public static bool TryParsePrenda(string? raw, out FurPrendaMarking? marking)
    {
        marking = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "ninguna":
            case "no_aplica":
                marking = FurPrendaMarking.Ninguna;
                return true;
            case "inscripcion":
            case "constitucion":
                marking = FurPrendaMarking.Constitucion;
                return true;
            case "levantamiento":
                marking = FurPrendaMarking.Levantamiento;
                return true;
            case "ambas":
            case "ambos":
                marking = FurPrendaMarking.Ambos;
                return true;
            default:
                return false;
        }
    }

    /// <param name="flags">
    /// Si un flag es null, se infiere del código de trámite (compatibilidad). Si viene true/false,
    /// esa es la fuente de verdad del simulador.
    /// </param>
    public static FurDocumentData Build(
        string procedureCode,
        string family,
        string sellerPersonKind,
        string buyerPersonKind,
        string vehicleKind,
        FurPreviewFlags? flags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(procedureCode);

        if (!TryParsePersonKind(sellerPersonKind, out var sellerKind)
            || !TryParsePersonKind(buyerPersonKind, out var buyerKind)
            || !TryParseVehicleKind(vehicleKind, out var vehicle))
        {
            throw new ArgumentOutOfRangeException(nameof(vehicleKind), "Parámetros de simulación inválidos.");
        }

        var (template, clase, placa, fieldToFill) = VehicleAssets(vehicle);
        return FinishBuild(procedureCode, family, sellerKind, buyerKind, template, clase, placa, flags, fieldToFill);
    }

    public static FurDocumentData BuildFromClassification(
        string procedureCode,
        string family,
        string sellerPersonKind,
        string buyerPersonKind,
        string vehicleClass,
        FurTemplateFormat format,
        string? fieldToFill,
        FurPreviewFlags? flags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(procedureCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleClass);

        if (!TryParsePersonKind(sellerPersonKind, out var sellerKind)
            || !TryParsePersonKind(buyerPersonKind, out var buyerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sellerPersonKind), "Parámetros de simulación inválidos.");
        }

        var clase = vehicleClass.Trim();
        var placa = IsMotoClase(clase) ? "ABC12A" : PhPlaca;
        return FinishBuild(procedureCode, family, sellerKind, buyerKind, format, clase, placa, flags, fieldToFill);
    }

    private static bool IsMotoClase(string clase)
    {
        var n = FurClassificationNormalizer.Normalize(clase);
        return n.Contains("MOTO", StringComparison.Ordinal);
    }

    private static FurDocumentData FinishBuild(
        string procedureCode,
        string family,
        string sellerKind,
        string buyerKind,
        FurTemplateFormat template,
        string clase,
        string placa,
        FurPreviewFlags? flags,
        string? fieldToFill)
    {
        var esTraspaso = IsTraspaso(family, procedureCode);
        var partes = new List<DocumentParte>();
        if (esTraspaso)
            partes.Add(BuildParte("vendedor", sellerKind, esVendedor: true));
        partes.Add(BuildParte("comprador", buyerKind, esVendedor: false));
        if (IsLeasing(procedureCode))
            partes.Add(BuildLocatario());

        var modalidad = esTraspaso ? "traspaso" : "matricula_inicial";
        var inferred = ResolveTransformaciones(procedureCode);
        var transformaciones = new FurTransformacionesDeclaradas(
            Color: flags?.CambioColor ?? inferred.Color,
            Carroceria: flags?.CambioCarroceria ?? inferred.Carroceria,
            Combustible: flags?.CambioCombustible ?? inferred.Combustible,
            Blindaje: flags?.Blindaje ?? inferred.Blindaje);
        var prendaMarking = flags?.Prenda ?? ResolvePrenda(procedureCode);
        var firmas = BuildFirmas(partes, omitirFirmaComprador: IsUnilateral(procedureCode));
        var vehiculo = new VehiculoDatos(
                Marca: "[MARCA]",
                Linea: "[LINEA]",
                Modelo: "2026",
                Color: transformaciones.Color ? PhColorNuevo : "ROJO",
                Clase: clase,
                Combustible: transformaciones.Combustible ? "DIESEL" : "GASOLINA",
                Cilindraje: "1600",
                Vin: "VINPREVFUROSAMPLE01",
                Placa: placa,
                NumeroMotor: "M-PREV",
                NumeroChasis: "C-PREV",
                NumeroSerie: "S-PREV",
                TipoCarroceria: transformaciones.Carroceria ? "PICKUP" : "SEDAN",
                TipoServicio: "PARTICULAR",
                Capacidad: "5",
                PesoBruto: "1200",
                NumeroEjes: "2");

        return new FurDocumentData(
            ProcedureInstanceId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ReferenceNumber: "PREV-FUR",
            Modalidad: modalidad,
            TipologiaCodigo: procedureCode.Trim(),
            Vehiculo: vehiculo,
            Organismo: new OrganismoTransito("05001000", "[NOMBRE DEL ORGANISMO DE TRÁNSITO]", "Medellín"),
            Partes: partes,
            ValorVenta: esTraspaso ? 1m : null,
            Causal: null,
            SellosFirma: [],
            FechaTramite: new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
            Observaciones: ComposeObservaciones(procedureCode, prendaMarking, transformaciones, partes), // casilla 23 — no usar placeholder
            FirmaImagenes: firmas.Imagenes,
            FirmaBaulMetadatos: firmas.Metadatos,
            IdentidadValidada: true,
            PrendaMarking: prendaMarking,
            AcreedorPrenda: prendaMarking is FurPrendaMarking.Ninguna ? null : PhAcreedor,
            TemplateFormat: template,
            FieldToFill: fieldToFill,
            Transformaciones: transformaciones);
    }

    public static bool IsUnilateral(string? code) =>
        string.Equals(code?.Trim(), "TRASPASO_UNILATERAL", StringComparison.OrdinalIgnoreCase);

    public static bool IsLeasing(string? code) =>
        string.Equals(code?.Trim(), "MATRICULA_LEASING", StringComparison.OrdinalIgnoreCase);

    private static DocumentParte BuildParte(string rol, string kind, bool esVendedor)
    {
        if (kind == PersonJuridica)
        {
            return new DocumentParte(
                rol,
                esVendedor ? PhVendedorPj : PhCompradorPj,
                esVendedor ? PhVendedorNit : PhCompradorNit,
                null,
                "NIT",
                null,
                "[DIRECCIÓN SINTÉTICA]",
                "Medellín",
                EsJuridica: true,
                RepresentanteLegalNombre: PhRlNombre,
                RepresentanteLegalTipoDoc: "CC",
                RepresentanteLegalDocumento: PhRlDocumento);
        }

        return new DocumentParte(
            rol,
            esVendedor ? PhVendedorPn : PhCompradorPn,
            esVendedor ? PhVendedorPnDoc : PhCompradorPnDoc,
            null,
            "CC",
            null,
            "[DIRECCIÓN SINTÉTICA]",
            "Medellín");
    }

    private static DocumentParte BuildLocatario() =>
        new("locatario", PhLocatario, PhLocatarioDoc, null, PhLocatarioTipoDoc, null, "[DIRECCIÓN SINTÉTICA]", "Medellín");

    public static FurTemplateFormat TemplateFormatFor(string vehicleKind) =>
        VehicleAssets(vehicleKind).Format;

    private static (FurTemplateFormat Format, string Clase, string Placa, string FieldToFill) VehicleAssets(string kind) =>
        kind switch
        {
            VehicleMoto => (FurTemplateFormat.Automotor, "MOTOCICLETA", "ABC12A", "MOTOCICLETA"),
            VehicleCamioneta => (FurTemplateFormat.Automotor, "CAMIONETA", PhPlaca, "CAMIONETA"),
            VehicleRemolque => (FurTemplateFormat.Remolques, "REMOLQUE", PhPlaca, "REMOLQUE"),
            VehicleMaquinaria => (FurTemplateFormat.Maquinaria, "EXCAVADORA", PhPlaca, "CONSTRUCCION"),
            _ => (FurTemplateFormat.Automotor, "AUTOMOVIL", PhPlaca, "AUTOMOVIL"),
        };

    private static FurPrendaMarking ResolvePrenda(string code)
    {
        var n = code.Trim().ToUpperInvariant();
        if (n is "LEVANTAR_INSCRIBIR_PRENDA")
            return FurPrendaMarking.Ambos;
        if (n.Contains("LEVANTAMIENTO_PRENDA", StringComparison.Ordinal) || n is "LEVANTAMIENTO_PRENDA")
            return FurPrendaMarking.Levantamiento;
        if (n.Contains("PRENDA_INSCRIPCION", StringComparison.Ordinal) || n.Contains("INSCRIBIR_PRENDA", StringComparison.Ordinal))
            return FurPrendaMarking.Constitucion;
        return FurPrendaMarking.Ninguna;
    }

    private static string? ComposeObservaciones(
        string procedureCode,
        FurPrendaMarking prenda,
        FurTransformacionesDeclaradas t,
        IReadOnlyList<DocumentParte> partes)
    {
        var extra = procedureCode.Trim().ToUpperInvariant() switch
        {
            "CAMBIO_LOCATARIO" => "CAMBIO DE LOCATARIO: [NOMBRE NUEVO LOCATARIO] - [DOC].",
            "CAMBIO_ACREEDOR" => "CAMBIO DE ACREEDOR PRENDARIO: [NOMBRE] - NIT [DOC].",
            "REGRABAR_MOTOR_CHASIS" => "Regrabación de motor: [MOTOR]. Regrabación de chasis: [CHASIS].",
            _ => null,
        };

        var transformaciones = new FurTransformacionesDeclaradas(
            Color: t.Color || string.Equals(procedureCode, "CAMBIO_COLOR", StringComparison.OrdinalIgnoreCase),
            Carroceria: t.Carroceria || string.Equals(procedureCode, "CAMBIO_CARROCERIA", StringComparison.OrdinalIgnoreCase),
            Combustible: t.Combustible || string.Equals(procedureCode, "CONVERSION_COMBUSTIBLE", StringComparison.OrdinalIgnoreCase),
            Blindaje: t.Blindaje);

        var automatico = FurPrendaObservation.Join(
            FurTramiteObservation.Compose(procedureCode, partes),
            FurPrendaObservation.Join(
                FurPrendaObservation.Compose(prenda, PhAcreedor, PhAcreedorNit),
                FurPrendaObservation.Join(
                    FurTransformationObservations.ComposeDeclaradas(
                        transformaciones,
                        transformaciones.Color ? PhColorNuevo : "ROJO",
                        transformaciones.Combustible ? "DIESEL" : "GASOLINA",
                        transformaciones.Carroceria ? "PICKUP" : "SEDAN"),
                    extra)));

        return FurObservacionesComposer.Componer(automatico, null);
    }

    private static (Dictionary<string, byte[]> Imagenes, Dictionary<string, FirmaBaulMetadata> Metadatos) BuildFirmas(
        IReadOnlyList<DocumentParte> partes,
        bool omitirFirmaComprador)
    {
        var imagenes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var metadatos = new Dictionary<string, FirmaBaulMetadata>(StringComparer.OrdinalIgnoreCase);
        var desde = new DateOnly(2026, 7, 1);
        var hasta = new DateOnly(2026, 8, 31);

        foreach (var parte in partes)
        {
            if (omitirFirmaComprador
                && string.Equals(parte.Rol, "comprador", StringComparison.OrdinalIgnoreCase))
                continue;

            var (doc, nombre, hash) = FirmaAuditoria(parte);
            imagenes[parte.Rol] = string.Equals(parte.Rol, "vendedor", StringComparison.OrdinalIgnoreCase)
                ? FurPreviewSignatures.Vendedor
                : FurPreviewSignatures.Comprador;
            metadatos[parte.Rol] = new FirmaBaulMetadata(
                doc,
                nombre,
                desde,
                hasta,
                Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
                hash);
        }

        return (imagenes, metadatos);
    }

    private static FurTransformacionesDeclaradas ResolveTransformaciones(string code)
    {
        var n = code.Trim().ToUpperInvariant();
        return new FurTransformacionesDeclaradas(
            Color: n is "CAMBIO_COLOR",
            Carroceria: n is "CAMBIO_CARROCERIA",
            Combustible: n is "CONVERSION_COMBUSTIBLE",
            Blindaje: n.Contains("BLINDAJE", StringComparison.Ordinal));
    }

    private static (string Doc, string Nombre, string Hash) FirmaAuditoria(DocumentParte parte)
    {
        if (parte.EsJuridica)
        {
            var doc = string.IsNullOrWhiteSpace(parte.RepresentanteLegalDocumento)
                ? parte.Documento
                : parte.RepresentanteLegalDocumento;
            var nombre = string.IsNullOrWhiteSpace(parte.RepresentanteLegalNombre)
                ? parte.Nombre
                : parte.RepresentanteLegalNombre;
            var hash = string.Equals(parte.Rol, "vendedor", StringComparison.OrdinalIgnoreCase)
                ? "434JH4JK3H4KJ32H4"
                : "9K2M7P1Q8R5T6W0X3";
            return (doc ?? string.Empty, (nombre ?? string.Empty).ToUpperInvariant(), hash);
        }

        var hashPn = string.Equals(parte.Rol, "vendedor", StringComparison.OrdinalIgnoreCase)
            ? "434JH4JK3H4KJ32H4"
            : "9K2M7P1Q8R5T6W0X3";
        return (
            parte.Documento ?? string.Empty,
            (parte.Nombre ?? string.Empty).ToUpperInvariant(),
            hashPn);
    }
}

/// <summary>Opciones del simulador FUR que el mapper productivo consume sin un segundo motor.</summary>
public sealed record FurPreviewFlags(
    bool? CambioColor = null,
    bool? CambioCombustible = null,
    bool? CambioCarroceria = null,
    bool? Blindaje = null,
    FurPrendaMarking? Prenda = null);
