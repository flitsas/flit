using System.Globalization;
using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>Mapea <see cref="FurDocumentData"/> al diccionario de tokens del manifest overlay.</summary>
public static class FurFieldMapper
{
    /// <summary>Sello impreso en el espacio de firma cuando no hay validación de identidad (HU #10463).</summary>
    private const string NoFirmadoSello = "NO FIRMADO";

    public static IReadOnlyDictionary<string, FurFieldValue> Map(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var esTraspaso = IsTraspaso(data);
        var propietario = ResolvePropietario(data, esTraspaso);
        var comprador = esTraspaso ? ResolveComprador(data) : null;
        var (placaLetras, placaNumeros) = SplitPlaca(data.Placa);
        var (propAp1, propAp2, propNom) = NameParts(propietario);
        var (compAp1, compAp2, compNom) = NameParts(comprador);
        var fecha = data.FechaTramite ?? DateTime.UtcNow;

        var dict = new Dictionary<string, FurFieldValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["traffic_secretary_name"] = Text(Upper(data.Organismo.Nombre)),
            ["traffic_secretary_city"] = Text(data.Organismo.Ciudad),
            ["traffic_secretary_code"] = Text(data.Organismo.Codigo),
            ["processing_day"] = Text(fecha.Day.ToString(CultureInfo.InvariantCulture)),
            ["processing_month"] = Text(fecha.Month.ToString(CultureInfo.InvariantCulture)),
            ["processing_year"] = Text(fecha.Year.ToString(CultureInfo.InvariantCulture)),
            ["plate_letter"] = Text(placaLetras),
            ["plate_number"] = Text(placaNumeros),
            ["vehicle_brand"] = Text(Upper(data.Vehiculo.Marca)),
            ["vehicle_line"] = Text(Upper(data.Vehiculo.Linea)),
            ["vehicle_colors"] = Text(Upper(data.Vehiculo.Color)),
            ["vehicle_model"] = Text(data.Vehiculo.Modelo),
            ["vehicle_displacement"] = Text(data.Vehiculo.Cilindraje),
            ["vehicle_capacity"] = Text(data.Vehiculo.Capacidad),
            ["vehicle_bodywork_type"] = Text(Upper(data.Vehiculo.TipoCarroceria)),
            ["vehicle_engine_number"] = Text(Upper(data.Vehiculo.NumeroMotor)),
            ["vehicle_chassis_number"] = Text(Upper(data.Vehiculo.NumeroChasis)),
            ["vehicle_serial_number"] = Text(Upper(data.Vehiculo.NumeroSerie)),
            ["vehicle_vin_number"] = Text(Upper(data.Vehiculo.Vin)),
            ["vehicle_owner_first_last_name"] = Text(Upper(propAp1)),
            ["vehicle_owner_second_last_name"] = Text(Upper(propAp2)),
            ["vehicle_owner_name"] = Text(Upper(propNom)),
            ["vehicle_owner_document_number"] = Text(propietario?.Documento),
            ["vehicle_owner_address"] = Text(DisplayOrDash(propietario?.Address)),
            ["vehicle_owner_city"] = Text(DisplayOrDash(propietario?.City)),
            ["vehicle_owner_phone"] = Text(DisplayOrDash(propietario?.Phone)),
            ["observations"] = Text(BuildObservations(data)),
            // Casilla 19 "EMPRESA VINCULADORA". Solo se llena cuando el trámite trae el dato (servicio
            // público con empresa vinculadora); si no, queda en blanco como el resto del recuadro
            // (Text() ya devuelve "" ante null/whitespace — misma convención que el resto del mapper).
            ["linked_company_name"] = Text(Upper(data.EmpresaVinculadoraRazonSocial)),
            ["linked_company_nit"] = Text(data.EmpresaVinculadoraNit),
        };

        SetSignature(
            dict,
            "vehicle_owner_signature",
            data,
            propietario?.Rol,
            IdentidadOrSello(
                data,
                esTraspaso ? "vendedor" : "comprador",
                esTraspaso ? ["vendedor", "propietario"] : ["comprador", "propietario"]));

        MarkTramite(dict, data);
        MarkAlertas(dict, data);
        MarkClase(dict, data.Vehiculo.Clase);
        MarkCombustible(dict, data.Vehiculo.Combustible);
        MarkServicio(dict, data.Vehiculo.TipoServicio);
        MarkCheckbox(dict, "is_armored_vehicle_yes", data.Transformaciones.Blindaje);
        MarkCheckbox(dict, "is_armored_vehicle_no", !data.Transformaciones.Blindaje);
        MarkCheckbox(dict, "is_dismantling_armor_no", true);

        if (esTraspaso && comprador is not null)
        {
            dict["vehicle_buyer_first_last_name"] = Text(Upper(compAp1));
            dict["vehicle_buyer_second_last_name"] = Text(Upper(compAp2));
            dict["vehicle_buyer_name"] = Text(Upper(compNom));
            dict["vehicle_buyer_document_number"] = Text(comprador.Documento);
            dict["vehicle_buyer_address"] = Text(DisplayOrDash(comprador.Address));
            dict["vehicle_buyer_city"] = Text(DisplayOrDash(comprador.City));
            dict["vehicle_buyer_phone"] = Text(DisplayOrDash(comprador.Phone));
            // ADR-0051 — el comprador solo lleva sello/imagen de firma si el tipo lo declara firmante
            // (data.SignatureActors). TRASPASO_UNILATERAL declara ["vendedor"]: el comprador (locatario)
            // NO firma, aunque sí aparece con sus datos en esta sección. `null` (llave ausente) = sin
            // restricción, comportamiento previo a esta llave.
            if (data.SignatureActors is null || data.SignatureActors.Contains("comprador", StringComparer.OrdinalIgnoreCase))
            {
                // HU #11035 — el sello del comprador baja 4pt (el campo declara 8pt, frente a 6,5pt del
                // propietario): con la reducción uniforme de 2pt seguía saliéndose del recuadro.
                SetSignature(
                    dict,
                    "vehicle_buyer_signature",
                    data,
                    comprador.Rol,
                    IdentidadOrSello(data, "comprador", ["comprador"]),
                    selloFontSizeDelta: -4);
            }
            else
            {
                dict["vehicle_buyer_signature"] = Text("");
            }
            MarkDocType(dict, comprador.Documento, comprador.DocumentType, "vehicle_buyer");
        }
        else
        {
            // AC2 (#10457): traspaso sin comprador resuelto (o matrícula) → la sección comprador
            // queda EN BLANCO, sin '-' ni basura. Los checkboxes de tipo de documento del comprador
            // se marcan explícitamente como no seleccionados (simetría con vehicle_owner, que siempre
            // se marca en L90) para no dejar casillas en estado indefinido en el overlay.
            dict["vehicle_buyer_first_last_name"] = Text("");
            dict["vehicle_buyer_second_last_name"] = Text("");
            dict["vehicle_buyer_name"] = Text("");
            dict["vehicle_buyer_document_number"] = Text("");
            dict["vehicle_buyer_address"] = Text("");
            dict["vehicle_buyer_city"] = Text("");
            dict["vehicle_buyer_phone"] = Text("");
            dict["vehicle_buyer_signature"] = Text("");
            MarkDocType(dict, null, null, "vehicle_buyer");
        }

        MarkDocType(dict, propietario?.Documento, propietario?.DocumentType, "vehicle_owner");

        // HU #10463 — sin validación de identidad aprobada, el espacio de firma del FUR muestra
        // "NO FIRMADO" (matrícula: propietario; traspaso: vendedor + comprador).
        // ADR-0051 — el comprador solo recibe el sello "NO FIRMADO" si el tipo lo declara firmante:
        // sin esta guarda, TRASPASO_UNILATERAL estampaba "NO FIRMADO" en un espacio de firma que el
        // tipo ni siquiera exige (el comprador/locatario no firma este documento).
        if (!data.IdentidadValidada)
        {
            dict["vehicle_owner_signature"] = Text(NoFirmadoSello);
            if (esTraspaso
                && (data.SignatureActors is null || data.SignatureActors.Contains("comprador", StringComparer.OrdinalIgnoreCase)))
                dict["vehicle_buyer_signature"] = Text(NoFirmadoSello);
        }

        return dict;
    }

    /// <param name="selloFontSizeDelta">
    /// Ajuste de cuerpo del SELLO de identidad respecto al manifiesto (HU #11031/#11035). El campo del
    /// comprador declara 8pt frente a los 6,5pt del propietario, así que necesita bajar más para que el
    /// bloque de cuatro líneas quepa en su recuadro.
    /// </param>
    private static void SetSignature(
        Dictionary<string, FurFieldValue> dict,
        string fieldId,
        FurDocumentData data,
        string? rol,
        string fallbackText,
        double selloFontSizeDelta = -2)
    {
        if (!string.IsNullOrWhiteSpace(rol)
            && data.FirmaImagenes is not null
            && TryGetFirmaImagen(data.FirmaImagenes, rol, out var image))
        {
            var sidecar = TryBuildFirmaBaulSidecar(data.FirmaBaulMetadatos, rol);
            dict[fieldId] = new FurFieldValue(null, image, sidecar);
            return;
        }

        // HU #11031 — el sello de la validación de identidad se imprime 2pt más pequeño que el resto
        // del campo: son cuatro líneas dentro del espacio de firma y con el cuerpo del manifiesto se
        // salían del recuadro. El sello previo de firma electrónica conserva su tamaño.
        var esSelloIdentidad = !string.IsNullOrWhiteSpace(rol)
            && data.SellosIdentidad is not null
            && data.SellosIdentidad.TryGetValue(rol, out var selloIdentidad)
            && !string.IsNullOrWhiteSpace(selloIdentidad)
            && string.Equals(selloIdentidad, fallbackText, StringComparison.Ordinal);

        dict[fieldId] = new FurFieldValue(Val(fallbackText), FontSizeDelta: esSelloIdentidad ? selloFontSizeDelta : 0);
    }

    /// <summary>
    /// Sello de trazabilidad junto a la imagen de la firma del baúl. El texto lo arma
    /// <see cref="FlitFirmaBaulSello"/>, compartido con la compraventa, el mandato y la solicitud de
    /// trámite virtual (HU #11170): antes vivía aquí y por eso era el único documento que lo llevaba.
    /// El FUR es el que SÍ incluye la identificación del firmante, porque su espacio de firma no la
    /// imprime en ninguna otra parte.
    /// </summary>
    private static string? TryBuildFirmaBaulSidecar(
        IReadOnlyDictionary<string, FirmaBaulMetadata>? metadata,
        string rol) =>
        FlitFirmaBaulSello.Resolve(metadata, rol, incluirIdentificacion: true);

    private static bool TryGetFirmaImagen(IReadOnlyDictionary<string, byte[]> images, string rol, out byte[] bytes)
    {
        foreach (var key in FirmaRolKeys(rol))
        {
            if (images.TryGetValue(key, out var img) && img.Length > 0)
            {
                bytes = img;
                return true;
            }
        }

        bytes = [];
        return false;
    }

    /// <summary>
    /// Alias de rol para resolver la firma. Delega en <see cref="FlitFirmaBaulSello.RolKeys"/> para que
    /// la imagen y su sello de trazabilidad se busquen SIEMPRE con las mismas llaves y en el mismo
    /// orden: si divergieran, una parte podría quedar con firma estampada y sin vigencia ni hash.
    /// </summary>
    private static IEnumerable<string> FirmaRolKeys(string rol) => FlitFirmaBaulSello.RolKeys(rol);

    /// <summary>
    /// Numeral 3 del FUR. Las casillas objetivo (tipo ∪ prenda ∪ transformaciones) están en
    /// <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c>. Este método es el emisor actual; no
    /// contradigas el artefacto en un cambio nuevo sin actualizarlo en el mismo PR.
    /// </summary>
    private static void MarkTramite(Dictionary<string, FurFieldValue> dict, FurDocumentData data)
    {
        var marks = FurNumeral3Marks.Resolve(data);
        foreach (var n in FurNumeral3Marks.Emittable)
            MarkCheckbox(dict, FurNumeral3Marks.FieldId(n), marks.Contains(n));
        // Casillas 6 y 14 no se declaran: no hay tipo en el catálogo (REGLAS-NUMERAL-3).
    }

    /// <summary>
    /// Numeral 20 DATOS DE ALERTA. Inscripción/registro de prenda → LIM. PROPIEDAD (2) + A FAVOR DE.
    /// Levantamiento → OTRO (4) + A FAVOR DE. Hurto (1) y embargo (3) no se marcan desde el gravamen.
    /// <para>Duplicado de placa y duplicado de tarjeta marcan también OTRO (4), pero por TIPO de
    /// trámite y no por gravamen: ahí no hay acreedor, así que A FAVOR DE queda vacía — la misma
    /// convención que un gravamen sin nombre (sí X en la columna, campo vacío).</para>
    /// </summary>
    private static void MarkAlertas(Dictionary<string, FurFieldValue> dict, FurDocumentData data)
    {
        var marking = data.PrendaMarking;
        var inscribe = marking is FurPrendaMarking.Constitucion or FurPrendaMarking.Ambos;
        var levanta = marking is FurPrendaMarking.Levantamiento or FurPrendaMarking.Ambos;
        MarkCheckbox(dict, "alert_data_code_1", false);
        MarkCheckbox(dict, "alert_data_code_2", inscribe);
        MarkCheckbox(dict, "alert_data_code_3", false);
        MarkCheckbox(dict, "alert_data_code_4", levanta || MarcaOtroPorTipo(Norm(data.TipologiaCodigo)));
        dict["alert_data_code_5"] = Text(inscribe || levanta ? Upper(data.AcreedorPrenda) : "");
    }

    /// <summary>
    /// Tipos cuyo numeral 20 se marca en OTRO por sí mismos, sin gravamen de por medio
    /// (<c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c>, numeral 20).
    ///
    /// <para><c>RADICADO_CUENTA</c> entra por la misma vía que los duplicados: marca OTRO por el tipo
    /// de trámite, no por un gravamen, así que «A FAVOR DE» queda vacía — no hay acreedor que
    /// escribir. El organismo de destino se declara en el párrafo 23, no aquí.</para>
    /// </summary>
    private static bool MarcaOtroPorTipo(string code) =>
        code is "DUPLICADO_PLACA" or "DUPLICADO_TARJETA" or "RADICADO_CUENTA" or "TRASLADO_CUENTA";

    private static void MarkClase(Dictionary<string, FurFieldValue> dict, string? clase)
    {
        var n = Norm(clase);
        MarkCheckbox(dict, "vehicle_class_1", n.Contains("AUTOMOVIL"));
        MarkCheckbox(dict, "vehicle_class_5", n.Contains("CAMIONETA"));
        MarkCheckbox(dict, "vehicle_class_9", n.Contains("MOTOCICLETA") || n == "MOTO");
    }

    private static void MarkCombustible(Dictionary<string, FurFieldValue> dict, string? combustible)
    {
        var n = Norm(combustible);
        MarkCheckbox(dict, "vehicle_fuel_type_1", n.Contains("GASOLINA") || n.Contains("GASOL"));
        // HU #11641 — BIODIESEL contiene "DIESEL": el Contains suelto marcaba las casillas 2 y 8 a la
        // vez y el formulario dejaba de decir con qué se mueve el vehículo. Mismo defecto que ya se
        // corrigió en MarkServicio con "SERVICIO PUBLICO ESPECIAL".
        MarkCheckbox(dict, "vehicle_fuel_type_2", n.Contains("DIESEL") && !n.Contains("BIODIESEL"));
        MarkCheckbox(dict, "vehicle_fuel_type_3", IsGasFuel(n));
        // HU #11641 — HIBRIDO comparte casilla con MIXTO. El formulario oficial no tiene casilla de
        // híbrido, y «MIXTO» es literalmente su caso: el vehículo se mueve con más de una fuente de
        // energía. El catálogo del wizard ofrecía HIBRIDO desde su creación sin que ninguna casilla
        // lo recogiera, así que estos vehículos salían con la sección 7 en blanco.
        MarkCheckbox(dict, "vehicle_fuel_type_4", n.Contains("MIXTO") || n.Contains("HIBRID"));
        MarkCheckbox(dict, "vehicle_fuel_type_5", n.Contains("ELECTRIC"));
        MarkCheckbox(dict, "vehicle_fuel_type_6", n.Contains("HIDROGEN"));
        MarkCheckbox(dict, "vehicle_fuel_type_7", n.Contains("ETANOL"));
        MarkCheckbox(dict, "vehicle_fuel_type_8", n.Contains("BIODIESEL"));
    }

    private static bool IsGasFuel(string n) =>
        n is "GAS"
        || n.Contains("GAS NATURAL")
        || (n.Contains("GAS") && !n.Contains("GASOL") && !n.Contains("GASOLINA"));

    /// <summary>
    /// Casilla 18 del FUR. Delega en <see cref="VehicleServiceTypeCode.Resolve"/> para reducir el
    /// valor de <c>vehicle_service</c> (texto libre del RUNT o código de matrícula inicial) a UN
    /// solo código canónico y marcar exactamente una casilla — antes cada casilla se evaluaba con
    /// un <c>Contains</c> independiente y un valor compuesto del RUNT como "SERVICIO PUBLICO
    /// ESPECIAL" marcaba PÚBLICO y ESPECIAL a la vez.
    /// </summary>
    private static void MarkServicio(Dictionary<string, FurFieldValue> dict, string? servicio)
    {
        var codigo = VehicleServiceTypeCode.Resolve(servicio);
        MarkCheckbox(dict, "vehicle_service_type_1", codigo == VehicleServiceTypeCode.Particular);
        MarkCheckbox(dict, "vehicle_service_type_2", codigo == VehicleServiceTypeCode.Publico);
        MarkCheckbox(dict, "vehicle_service_type_3", codigo == VehicleServiceTypeCode.Diplomatico);
        MarkCheckbox(dict, "vehicle_service_type_4", codigo == VehicleServiceTypeCode.Oficial);
        MarkCheckbox(dict, "vehicle_service_type_5", codigo == VehicleServiceTypeCode.Especial);
        MarkCheckbox(dict, "vehicle_service_type_6", codigo == VehicleServiceTypeCode.Otros);
    }

    private static void MarkDocType(
        Dictionary<string, FurFieldValue> dict,
        string? documento,
        string? documentType,
        string prefix)
    {
        foreach (var id in DocTypeCheckboxIds)
            MarkCheckbox(dict, $"{prefix}_document_type_{id}", false);

        var doc = documento?.Trim() ?? "";
        if (doc.Length == 0)
            return;

        var selected = ResolveDocTypeCheckbox(Norm(documentType), doc);
        if (selected is not null)
            MarkCheckbox(dict, $"{prefix}_document_type_{selected}", true);
    }

    private static readonly string[] DocTypeCheckboxIds =
    [
        "c", "nit", "nn", "p", "ce", "ti", "nuip", "cd",
    ];

    private static string? ResolveDocTypeCheckbox(string tipo, string doc)
    {
        if (tipo is "CC" or "CEDULA" or "C" || tipo.Contains("CIUDADANIA", StringComparison.Ordinal))
            return "c";
        if (tipo.Contains("NIT", StringComparison.Ordinal))
            return "nit";
        if (tipo is "NN")
            return "nn";
        if (tipo is "PAS" or "PASSPORT" or "PASAPORTE" || tipo.Contains("PASAPORTE", StringComparison.Ordinal))
            return "p";
        if (tipo is "CE" or "CEX" || tipo.Contains("EXTRANJ", StringComparison.Ordinal))
            return "ce";
        if (tipo is "TI" or "TARJETA" || tipo.Contains("IDENTIDAD", StringComparison.Ordinal))
            return "ti";
        if (tipo.Contains("NUIP", StringComparison.Ordinal))
            return "nuip";
        if (tipo.Contains("DIPLOMAT", StringComparison.Ordinal))
            return "cd";

        return doc.All(char.IsDigit) ? "c" : "p";
    }

    private static string BuildObservations(FurDocumentData data)
    {
        if (!string.IsNullOrWhiteSpace(data.Observaciones))
            return data.Observaciones.Trim();

        if (data.SellosFirma.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var s in data.SellosFirma)
        {
            if (string.IsNullOrWhiteSpace(s))
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(s);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Texto del espacio de firma (HU #10488): prioriza el sello de la validación biométrica de la parte
    /// (<see cref="FurDocumentData.SellosIdentidad"/>, por rol) y, si no hay, cae al sello previo de firma
    /// electrónica (<see cref="FurDocumentData.SellosFirma"/>). El override "NO FIRMADO" (sin identidad
    /// validada) se aplica después en <see cref="Map"/> y tiene la última palabra.
    /// </summary>
    private static string IdentidadOrSello(FurDocumentData data, string role, string[] fallbackPartes)
    {
        if (data.SellosIdentidad is not null
            && data.SellosIdentidad.TryGetValue(role, out var sello)
            && !string.IsNullOrWhiteSpace(sello))
            return sello;

        return SellosTexto(data.SellosFirma, fallbackPartes);
    }

    private static string SellosTexto(IReadOnlyList<string> sellos, params string[] partes)
    {
        foreach (var s in sellos)
        {
            foreach (var p in partes)
            {
                if (s.Contains(p, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
        }

        return "";
    }

    private static DocumentParte? ResolvePropietario(FurDocumentData data, bool esTraspaso)
    {
        if (esTraspaso)
        {
            foreach (var p in data.Partes)
            {
                var rol = Norm(p.Rol);
                if (rol.Contains("VENDEDOR") || rol.Contains("PROPIETARIO"))
                    return p;
            }
            return null;
        }

        foreach (var p in data.Partes)
        {
            var rol = Norm(p.Rol);
            if (rol.Contains("COMPRADOR") || rol.Contains("PROPIETARIO"))
                return p;
        }

        return data.Partes.Count > 0 ? data.Partes[0] : null;
    }

    private static DocumentParte? ResolveComprador(FurDocumentData data)
    {
        foreach (var p in data.Partes)
        {
            if (Norm(p.Rol).Contains("COMPRADOR"))
                return p;
        }
        return null;
    }

    /// <summary>
    /// ¿El FUR lleva sección de parte vendedora? Lo declara el tipo (ADR-0050).
    /// <para>Antes se decidía buscando la palabra "TRASPASO" dentro de la tipología o de la
    /// modalidad. Además de dar por traspaso cualquier código que la contuviera, dejaba fuera los
    /// tipos que sí tienen parte saliente sin llamarse así. Se conserva la heurística como respaldo
    /// para los documentos que aún no traen la capacidad.</para>
    /// </summary>
    private static bool IsTraspaso(FurDocumentData data) =>
        data.RequiereVendedor
        || Norm(data.TipologiaCodigo).Contains("TRASPASO")
        || Norm(data.Modalidad).Contains("TRASPASO");

    private static FurFieldValue Text(string? value) => new(Val(value));

    private static void MarkCheckbox(Dictionary<string, FurFieldValue> dict, string id, bool on) =>
        dict[id] = new FurFieldValue(on ? "X" : "");

    private static string Val(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    private static string DisplayOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string Upper(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.ToUpperInvariant();

    private static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().ToUpperInvariant().Trim();
    }

    private static (string Letras, string Numeros) SplitPlaca(string? placa)
    {
        if (string.IsNullOrWhiteSpace(placa)) return ("", "");
        var clean = placa.Trim().ToUpperInvariant().Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        var letras = new string(clean.TakeWhile(char.IsLetter).ToArray());
        var numeros = new string(clean.SkipWhile(char.IsLetter).ToArray());
        return (letras, numeros);
    }

    /// <summary>
    /// HU #10688 — reparte el nombre de la parte en las casillas del FUR. Persona jurídica: la razón social
    /// va COMPLETA en la casilla de nombre (sin trocear), apellidos vacíos. Persona natural: se trocea con
    /// <see cref="SplitName"/> como antes.
    /// </summary>
    private static (string Ap1, string Ap2, string Nom) NameParts(DocumentParte? parte)
    {
        if (parte is null)
            return ("", "", "");
        return parte.EsJuridica ? ("", "", parte.Nombre?.Trim() ?? "") : SplitName(parte.Nombre);
    }

    private static (string Ap1, string Ap2, string Nom) SplitName(string? full)
    {
        if (string.IsNullOrWhiteSpace(full)) return ("", "", "");
        var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => ("", "", parts[0]),
            2 => (parts[0], "", parts[1]),
            3 => (parts[1], parts[2], parts[0]),
            _ => (parts[^2], parts[^1], string.Join(' ', parts[..^2])),
        };
    }
}
