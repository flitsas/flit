using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Sesiones;

namespace Flit.PersonaNatural.Api.Solicitudes;

/// <summary>Errores de validación por campo (clave → mensajes), listos para un 422.</summary>
public sealed class ErroresDeCampo
{
    private readonly Dictionary<string, List<string>> _errores = new(StringComparer.Ordinal);

    public bool Hay => _errores.Count > 0;

    public void Agregar(string campo, string mensaje)
    {
        if (!_errores.TryGetValue(campo, out var lista))
            _errores[campo] = lista = [];
        lista.Add(mensaje);
    }

    public IReadOnlyDictionary<string, string[]> Todos() => _errores.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
}

/// <summary>Un actor ya normalizado y validado, listo para guardarse cifrado.</summary>
public sealed record ActorValidado(int Indice, string Rol, short Ordinal, string TipoDocumento, string Documento, string Nombre, string? Correo, string? Celular, string? Direccion, string? Ciudad = null);

/// <summary>
/// Reglas del borrador (HU #14047). TODAS salen de la definición congelada de la solicitud: ningún rol, campo, documento ni
/// capacidad se nombra aquí. Son funciones puras, sin base ni red, para que se prueben sin host.
/// </summary>
public static partial class ReglasBorrador
{
    public const int MaxTexto = 500;
    public const int MaxTextoLargo = 2000;

    /// <summary>Tope de «Entidad ante la que se levantó»: el mismo de la columna de FLIT 2 (<c>levantamiento_entidad</c>).</summary>
    public const int MaxEntidadLevantamiento = 200;

    /// <summary>Documentos de una persona natural: la cédula de ciudadanía, la de extranjería, el pasaporte y la tarjeta de identidad.</summary>
    public static readonly IReadOnlySet<string> TiposDocumento = new HashSet<string>(StringComparer.Ordinal) { "CC", "CE", "PAS", "TI" };

    /// <summary>Transformaciones que se pueden declarar encima del tipo base (las mismas que cotiza la fachada).</summary>
    public static readonly IReadOnlySet<string> Transformaciones = new HashSet<string>(StringComparer.Ordinal) { "CAMBIO_COLOR", "CONVERSION_COMBUSTIBLE", "CAMBIO_CARROCERIA" };

    [GeneratedRegex(@"^\d{4,15}$")]
    private static partial Regex SoloDigitos();

    [GeneratedRegex(@"^[A-Z0-9]{4,20}$")]
    private static partial Regex Alfanumerico();

    [GeneratedRegex(@"^\+?\d{7,15}$")]
    private static partial Regex Telefono();

    [GeneratedRegex(@"^\d{8,10}$")]
    private static partial Regex Nit();

    // ── Vehículo (al abrir) ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Placa y/o VIN según el modo de entrada del tipo (<c>PLATE</c>, <c>VIN</c> o <c>BOTH</c>), normalizados.</summary>
    public static (string? Placa, string? Vin) ValidarVehiculo(DefinicionTipoDto def, string? placa, string? vin, ErroresDeCampo errores)
    {
        var p = Limpiar(placa);
        var v = Limpiar(vin);
        if (p is not null && !Regex.IsMatch(p, "^[A-Z0-9]{5,8}$", RegexOptions.None, TimeSpan.FromMilliseconds(50)))
            errores.Agregar("placa", "La placa no es válida.");
        if (v is not null && !Regex.IsMatch(v, "^[A-Z0-9]{11,30}$", RegexOptions.None, TimeSpan.FromMilliseconds(50)))
            errores.Agregar("vin", "El VIN no es válido.");

        switch (def.Flags.EntryMode.ToUpperInvariant())
        {
            case "PLATE":
                if (p is null) errores.Agregar("placa", "Indique la placa del vehículo.");
                return (p, null);
            case "VIN":
                if (v is null) errores.Agregar("vin", "Indique el VIN del vehículo.");
                return (null, v);
            default:
                if (p is null && v is null) errores.Agregar("placa", "Indique la placa o el VIN del vehículo.");
                return (p, v);
        }
    }

    private static string? Limpiar(string? valor)
    {
        var limpio = new string((valor ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return limpio.Length == 0 ? null : limpio;
    }

    // ── Actores ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Valida los actores contra los roles de la definición: rol conocido, posición dentro de <c>max</c>, documento y contacto
    /// bien formados, sin repetir persona. Con roles que el tipo no admite para persona natural, rechaza el rol.
    /// </summary>
    public static IReadOnlyList<ActorValidado> ValidarActores(DefinicionTipoDto def, IReadOnlyList<ActorRequest>? actores, ErroresDeCampo errores, bool correoOpcionalDelRadicante = false)
    {
        var salida = new List<ActorValidado>();
        if (actores is null || actores.Count == 0)
        {
            errores.Agregar("actores", "Envíe al menos un actor.");
            return salida;
        }

        var porRol = new Dictionary<string, List<short>>(StringComparer.Ordinal);
        var documentos = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < actores.Count; i++)
        {
            var a = actores[i];
            var pre = $"actores[{i}]";
            var rol = (a.Rol ?? string.Empty).Trim().ToLowerInvariant();
            var definicion = def.Actores.FirstOrDefault(r => string.Equals(r.Rol, rol, StringComparison.Ordinal));
            if (definicion is null)
            {
                errores.Agregar($"{pre}.rol", "Este tipo de trámite no tiene ese rol.");
                continue;
            }

            if (!definicion.PersonaNaturalPermitida)
                errores.Agregar($"{pre}.rol", "Este rol no puede ser una persona natural en el portal.");

            var lista = porRol.TryGetValue(rol, out var l) ? l : porRol[rol] = [];
            var ordinal = a.Ordinal ?? (short)(lista.Count + 1);
            if (ordinal < 1 || ordinal > definicion.Max)
                errores.Agregar($"{pre}.ordinal", $"Este rol admite hasta {definicion.Max} persona(s).");
            else if (lista.Contains(ordinal))
                errores.Agregar($"{pre}.ordinal", "Esa posición ya está ocupada.");
            lista.Add(ordinal);

            var tipo = (a.TipoDocumento ?? string.Empty).Trim().ToUpperInvariant();
            if (!TiposDocumento.Contains(tipo))
                errores.Agregar($"{pre}.tipoDocumento", "Use CC, CE, PAS o TI.");

            var documento = PiiHasher.NormalizarDocumento(a.NumeroDocumento ?? string.Empty);
            var documentoValido = tipo == "PAS" ? Alfanumerico().IsMatch(documento) : SoloDigitos().IsMatch(documento);
            if (!documentoValido)
                errores.Agregar($"{pre}.numeroDocumento", tipo == "PAS" ? "El pasaporte admite letras y números." : "El número de documento solo admite dígitos.");
            else if (!documentos.Add(documento))
                errores.Agregar($"{pre}.numeroDocumento", "Esa persona ya está en el trámite.");

            var nombre = (a.Nombre ?? string.Empty).Trim();
            if (nombre.Length == 0 || nombre.Length > 150)
                errores.Agregar($"{pre}.nombre", "Escriba el nombre completo.");

            var correo = a.Correo?.Trim();
            // Canal del punto: quien radica (primer rol, ordinal 1) valida in-situ y no necesita correo; vacío se acepta.
            var sinCorreoPermitido = correoOpcionalDelRadicante && string.IsNullOrWhiteSpace(correo) && ordinal == 1
                && def.Actores.Count > 0 && string.Equals(def.Actores[0].Rol, rol, StringComparison.Ordinal);
            if (sinCorreoPermitido)
                correo = null;
            else if (!CotizacionesService.CorreoValido(correo))
                errores.Agregar($"{pre}.correo", "El correo no es válido.");

            var celular = a.Celular is null ? null : new string(a.Celular.Where(c => char.IsDigit(c) || c == '+').ToArray());
            if (celular is not null && celular.Length > 0 && !Telefono().IsMatch(celular))
                errores.Agregar($"{pre}.celular", "El teléfono no es válido.");

            var direccion = a.Direccion?.Trim();
            if (direccion is { Length: > 200 })
                errores.Agregar($"{pre}.direccion", "La dirección es demasiado larga.");

            var ciudad = a.Ciudad?.Trim();
            if (ciudad is { Length: > 80 })
                errores.Agregar($"{pre}.ciudad", "La ciudad es demasiado larga.");

            if (!errores.Hay)
                salida.Add(new ActorValidado(i, rol, ordinal, tipo, documento, nombre, correo, string.IsNullOrEmpty(celular) ? null : celular, string.IsNullOrWhiteSpace(direccion) ? null : direccion, string.IsNullOrWhiteSpace(ciudad) ? null : ciudad));
        }

        return salida;
    }

    // ── Campos del tipo ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Valida <c>field_values</c> contra los campos de la definición. Devuelve lo que se guarda: clave → valor canónico, o
    /// <c>null</c> para borrar un campo opcional. El <c>validation_schema</c> viaja al front tal cual; aquí se valida tipo,
    /// opciones y obligatoriedad.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ValidarCampos(DefinicionTipoDto def, IReadOnlyDictionary<string, JsonElement>? valores, ErroresDeCampo errores, ValoresActualesVehiculo? runt = null)
    {
        var salida = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (valores is null || valores.Count == 0)
        {
            errores.Agregar("valores", "Envíe al menos un campo.");
            return salida;
        }

        var campos = def.Pasos.SelectMany(p => p.Campos).GroupBy(c => c.FieldKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var declaradas = ClavesDeDeclaraciones(def);
        foreach (var (clave, valor) in valores)
        {
            var pre = $"valores.{clave}";
            if (!campos.TryGetValue(clave, out var campo))
            {
                if (declaradas.TryGetValue(clave, out var declaracion))
                {
                    ValidarDeclaracion(declaracion.Declaracion, declaracion.EsExtra, clave, valor, pre, salida, errores, runt);
                    continue;
                }

                errores.Agregar(pre, "Este tipo de trámite no tiene ese campo.");
                continue;
            }

            var texto = Canonico(valor);
            if (string.IsNullOrEmpty(texto))
            {
                if (campo.Obligatorio)
                    errores.Agregar(pre, "Este campo es obligatorio.");
                else
                    salida[clave] = null;
                continue;
            }

            var error = ErrorDeTipo(campo, texto);
            if (error is not null)
                errores.Agregar(pre, error);
            else
                salida[clave] = texto;
        }

        return salida;
    }

    private static string Canonico(JsonElement valor) => valor.ValueKind switch
    {
        JsonValueKind.String => valor.GetString()?.Trim() ?? string.Empty,
        JsonValueKind.Number => valor.GetDecimal().ToString(CultureInfo.InvariantCulture),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => string.Empty,
    };

    private static string? ErrorDeTipo(CampoDto campo, string texto)
    {
        switch (campo.Tipo.ToLowerInvariant())
        {
            case "number":
                return decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out _) ? null : "Escriba un número.";
            case "date":
                return DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? null : "Use una fecha con formato AAAA-MM-DD.";
            case "checkbox":
                return texto is "true" or "false" ? null : "Use verdadero o falso.";
            case "select":
                return campo.Opciones.Count == 0 || campo.Opciones.Contains(texto, StringComparer.Ordinal) ? null : "Elija una de las opciones.";
            case "textarea":
                return texto.Length <= MaxTextoLargo ? null : $"Use hasta {MaxTextoLargo} caracteres.";
            default:
                return texto.Length <= MaxTexto ? null : $"Use hasta {MaxTexto} caracteres.";
        }
    }

    // ── Declaraciones del tipo (HU 5) ────────────────────────────────────────────────────────────────────────────────

    public const string ControlSeleccion = "seleccion";
    public const string ControlCatalogo = "seleccion_catalogo";
    public const string ControlDestino = "organismo_destino";
    public const string PendienteDeclaracionRequerida = "declaracion_requerida";
    public const string PendienteDocumentoRequerido = "documento_requerido";
    private const int MaxValorDeclaracion = 100;

    /// <summary>Claves de <c>field_values</c> que las declaraciones del tipo admiten: la principal y las derivadas por el servidor.</summary>
    public static IReadOnlyDictionary<string, (DeclaracionTipoDto Declaracion, bool EsExtra)> ClavesDeDeclaraciones(DefinicionTipoDto def)
    {
        var claves = new Dictionary<string, (DeclaracionTipoDto, bool)>(StringComparer.Ordinal);
        foreach (var d in def.Declaraciones ?? [])
        {
            claves.TryAdd(d.FieldKey, (d, false));
            foreach (var extra in d.FieldKeysExtra)
                claves.TryAdd(extra, (d, true));
        }

        return claves;
    }

    /// <summary>
    /// Valida un valor declarado. La clave principal se valida contra el control (opciones, catálogo o identificador del
    /// organismo); las claves derivadas (<c>cambio_*</c>, <c>blindaje</c>, <c>transit_office_destino_*</c>) las completa el
    /// servidor al crear el trámite, así que lo que el cliente mande en ellas se ignora. Al declarar una transformación se
    /// prende su bandera <c>cambio_*</c>; al vaciar el valor opcional se apaga.
    /// </summary>
    private static void ValidarDeclaracion(
        DeclaracionTipoDto d, bool esExtra, string clave, JsonElement valor, string pre, Dictionary<string, string?> salida, ErroresDeCampo errores, ValoresActualesVehiculo? runt)
    {
        if (esExtra)
            return;

        var texto = Canonico(valor);
        var banderas = d.FieldKeysExtra.Where(k => k.StartsWith("cambio_", StringComparison.Ordinal)).ToList();
        if (string.IsNullOrEmpty(texto))
        {
            if (d.Obligatoria)
            {
                errores.Agregar(pre, "Este campo es obligatorio.");
                return;
            }

            salida[clave] = null;
            foreach (var b in banderas)
                salida[b] = null;
            return;
        }

        var error = d.Control switch
        {
            ControlSeleccion => d.Opciones.Any(o => string.Equals(o.Codigo, texto, StringComparison.Ordinal)) ? null : "Elija una de las opciones.",
            ControlDestino => Guid.TryParse(texto, out var id) && id != Guid.Empty ? null : "Elija un organismo de destino.",
            _ when d.Opciones.Count > 0 => d.Opciones.Any(o => string.Equals(o.Codigo, texto, StringComparison.Ordinal)) ? null : "Elija una de las opciones.",
            _ => texto.Length <= MaxValorDeclaracion ? null : $"Use hasta {MaxValorDeclaracion} caracteres.",
        };
        // Paridad con FLIT 2: el valor nuevo de una transformación es obligatorio y distinto del que el RUNT tiene hoy.
        error ??= ErrorIgualAlRunt(d, texto, runt);
        if (error is not null)
        {
            errores.Agregar(pre, error);
            return;
        }

        salida[clave] = d.Control == ControlDestino ? Guid.Parse(texto).ToString() : texto;
        foreach (var b in banderas)
            salida[b] = "true";
    }

    /// <summary>
    /// Documentos que una declaración reclama por sí misma (certificado de blindaje, los de cada causal): la matriz del tipo no
    /// los repite como pendientes. Los soportes opcionales (cambio de color…) no entran: si la matriz los pide, la matriz manda.
    /// </summary>
    public static IReadOnlySet<string> DocumentosDeDeclaraciones(DefinicionTipoDto def)
    {
        var docs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in def.Declaraciones ?? [])
        {
            if (d.SoporteObligatorio && !string.IsNullOrWhiteSpace(d.DocCodeSoporte))
                docs.Add(d.DocCodeSoporte);
            foreach (var o in d.Opciones.Where(o => o.ExigeSoporte))
                foreach (var doc in o.DocCodes)
                    docs.Add(doc);
        }

        return docs;
    }

    /// <summary>Un pendiente de declaración con el nombre legible que muestra el gate del punto.</summary>
    public readonly record struct PendienteDeDeclaracion(string Codigo, string Referencia, string Nombre, string? Paso, string? Motivo = null);

    /// <summary>
    /// Lo que falta de las declaraciones del borrador: el valor de cada declaración activa y obligatoria (<c>declaracion_requerida</c>)
    /// y los documentos que exige lo elegido (<c>documento_requerido</c>). Una declaración de trámite simultáneo solo cuenta si el
    /// ciudadano marcó su transformación.
    /// </summary>
    public static IReadOnlyList<PendienteDeDeclaracion> PendientesDeDeclaraciones(
        DefinicionTipoDto def, IReadOnlyDictionary<string, string> campos, IReadOnlyList<string> transformaciones, IReadOnlySet<string> documentosCargados, ValoresActualesVehiculo? runt = null)
    {
        var salida = new List<PendienteDeDeclaracion>();
        string? PasoDe(string sectionType) => def.Pasos.FirstOrDefault(p => p.SectionTypes.Contains(sectionType, StringComparer.Ordinal))?.Clave;
        string NombreDoc(string doc) => def.Documentos.FirstOrDefault(x => string.Equals(x.DocCode, doc, StringComparison.Ordinal))?.Nombre ?? doc;

        foreach (var d in def.Declaraciones ?? [])
        {
            var simultanea = !string.IsNullOrEmpty(d.ActivadaPor);
            if (simultanea && !transformaciones.Contains(d.ActivadaPor, StringComparer.OrdinalIgnoreCase))
                continue;

            var paso = d.Control == ControlDestino ? PasoDe("vehicle_query") ?? PasoDe("document_checklist") : PasoDe("document_checklist") ?? PasoDe("generic_form");
            campos.TryGetValue(d.FieldKey, out var valor);
            var opcion = string.IsNullOrWhiteSpace(valor) ? null : d.Opciones.FirstOrDefault(o => string.Equals(o.Codigo, valor, StringComparison.Ordinal));
            var tieneValor = !string.IsNullOrWhiteSpace(valor) && (d.Control != ControlSeleccion || opcion is not null);
            if (!tieneValor)
            {
                if (d.Obligatoria || simultanea)
                    salida.Add(new PendienteDeDeclaracion(PendienteDeclaracionRequerida, d.Clave, d.Etiqueta, paso));
                continue;
            }

            if (ErrorIgualAlRunt(d, valor, runt) is { } igual)
            {
                salida.Add(new PendienteDeDeclaracion(PendienteDeclaracionRequerida, d.Clave, d.Etiqueta, paso, igual));
                continue;
            }

            var docs = new List<string>();
            if (opcion is { ExigeSoporte: true })
                docs.AddRange(opcion.DocCodes);
            if (d.SoporteObligatorio && !string.IsNullOrWhiteSpace(d.DocCodeSoporte))
                docs.Add(d.DocCodeSoporte);
            foreach (var doc in docs.Distinct(StringComparer.Ordinal).Where(doc => !documentosCargados.Contains(doc)))
                salida.Add(new PendienteDeDeclaracion(PendienteDocumentoRequerido, doc, NombreDoc(doc), paso));
        }

        return salida;
    }

    // ── Comercial y prenda ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Decisiones de prenda que el portal acepta (HU 8): constituir una que ya existe, levantarla o declarar que no hay.</summary>
    public const string PrendaRegistrar = "registrar";
    public const string PrendaLevantar = "levantar";
    public const string PrendaSinPrenda = "sin_prenda";

    public static readonly IReadOnlySet<string> DecisionesPrenda = new HashSet<string>(StringComparer.Ordinal) { PrendaRegistrar, PrendaLevantar, PrendaSinPrenda };

    /// <summary>Documento de soporte de la decisión (mismos DocTipo de FLIT 2: <c>PrendaDocTipos</c>); <c>sin_prenda</c> no pide documento.</summary>
    public static string? DocumentoDePrenda(string? decision) => decision?.Trim().ToLowerInvariant() switch
    {
        PrendaRegistrar => "prenda_registro",
        PrendaLevantar => "prenda_levantamiento",
        _ => null,
    };

    /// <summary>
    /// El documento de la decisión de prenda solo se reclama si la matriz del tipo lo trae; los tipos de acción única
    /// (levantamiento, inscripción) ya traen el suyo con otro código y ese es el que se exige.
    /// </summary>
    public static string? DocumentoDePrendaDelTipo(DefinicionTipoDto def, string? decision) =>
        DocumentoDePrenda(decision) is { } doc && def.Documentos.Any(d => string.Equals(d.DocCode, doc, StringComparison.OrdinalIgnoreCase)) ? doc : null;

    /// <summary>Todos los DocTipo de prenda que un borrador con prenda puede recibir como adjunto.</summary>
    public static readonly IReadOnlySet<string> DocumentosDePrenda = new HashSet<string>(StringComparer.Ordinal) { "prenda_solicitud", "prenda_registro", "prenda_levantamiento" };

    /// <summary>Levantar en un tipo con prenda complementaria (sin acción única ni prenda obligatoria): el acreedor y el NIT son opcionales.</summary>
    public static bool LevantarSinAcreedor(DefinicionTipoDto def, string? decision) =>
        string.Equals(decision, PrendaLevantar, StringComparison.Ordinal)
        && def.Flags.PermitePrendaComplementaria && !def.Flags.RequierePrenda && string.IsNullOrEmpty(def.Flags.AccionPrenda);

    /// <summary>
    /// Valor comercial solo si el tipo lo pide; prenda con acreedor y NIT cuando el tipo la exige o la admite y el ciudadano
    /// declara que hay. Un bloque que el tipo no pide se rechaza: no se guarda nada «por si acaso».
    /// </summary>
    public static (ComercialDatos? Comercial, PrendaDatos? Prenda) ValidarComercial(DefinicionTipoDto def, ComercialRequest? body, ErroresDeCampo errores)
    {
        ComercialDatos? comercial = null;
        PrendaDatos? prenda = null;
        if (body is null)
        {
            errores.Agregar("comercial", "Envíe los datos.");
            return (null, null);
        }

        if (body.ValorComercial is { } valor)
        {
            if (!def.Flags.RequiereComercial)
                errores.Agregar("valorComercial", "Este tipo de trámite no pide valor comercial.");
            else if (valor <= 0)
                errores.Agregar("valorComercial", "El valor comercial debe ser mayor que cero.");
            else
                comercial = new ComercialDatos(valor);
        }
        else if (def.Flags.RequiereComercial)
        {
            errores.Agregar("valorComercial", "Indique el valor comercial.");
        }

        var admitePrenda = def.Flags.RequierePrenda || def.Flags.PermitePrendaComplementaria || !string.IsNullOrEmpty(def.Flags.AccionPrenda);
        if (body.Prenda is { } p)
        {
            if (!admitePrenda)
                errores.Agregar("prenda", "Este tipo de trámite no admite prenda.");
            else
            {
                var decision = p.Decision?.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(decision) && !DecisionesPrenda.Contains(decision))
                    errores.Agregar("prenda.decision", "Elija registrar, levantar o sin prenda.");
                else if (decision == PrendaSinPrenda && def.Flags.RequierePrenda)
                    errores.Agregar("prenda.decision", "Este tipo de trámite exige indicar la prenda.");
                if (string.IsNullOrEmpty(decision))
                    decision = null;

                // Con decisión explícita manda ella; sin decisión se conserva el comportamiento anterior (TienePrenda).
                var tiene = decision switch
                {
                    PrendaRegistrar or PrendaLevantar => true,
                    PrendaSinPrenda => false,
                    _ => def.Flags.RequierePrenda || p.TienePrenda,
                };
                var acreedor = p.Acreedor?.Trim();
                var nit = new string((p.NitAcreedor ?? string.Empty).Where(char.IsDigit).ToArray());
                // Regla de FLIT 2 (PrendaGate): levantar en un tipo de prenda complementaria no exige acreedor ni NIT; si vienen se validan.
                var exigeAcreedor = tiene && !LevantarSinAcreedor(def, decision);
                if (exigeAcreedor && string.IsNullOrEmpty(acreedor))
                    errores.Agregar("prenda.acreedor", "Indique el acreedor de la prenda.");
                else if (acreedor is { Length: > 150 })
                    errores.Agregar("prenda.acreedor", "El nombre del acreedor es demasiado largo.");
                if (tiene && (exigeAcreedor || nit.Length > 0) && !Nit().IsMatch(nit))
                    errores.Agregar("prenda.nitAcreedor", "Indique el NIT del acreedor.");
                // Solo el levantamiento de acción única la exige (PrendaGate); en los demás tipos no se guarda.
                var entidad = def.Flags.AccionPrenda == PrendaLevantar && tiene ? p.EntidadLevantamiento?.Trim() : null;
                if (entidad is { Length: > MaxEntidadLevantamiento })
                    errores.Agregar("prenda.entidadLevantamiento", $"Use hasta {MaxEntidadLevantamiento} caracteres.");
                if (string.IsNullOrEmpty(entidad))
                    entidad = null;
                if (!errores.Hay)
                    prenda = tiene ? new PrendaDatos(true, acreedor, nit, decision, entidad) : new PrendaDatos(false, null, null, decision);
            }
        }
        else if (def.Flags.RequierePrenda)
        {
            errores.Agregar("prenda", "Este tipo de trámite exige indicar la prenda: acreedor y NIT.");
        }

        return (comercial, prenda);
    }

    // ── Requisitos ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Transformaciones solo si el tipo las permite, y únicamente las que el portal cotiza; observaciones acotadas.</summary>
    public static RequisitosDatos? ValidarRequisitos(DefinicionTipoDto def, RequisitosRequest? body, ErroresDeCampo errores)
    {
        if (body is null)
        {
            errores.Agregar("requisitos", "Envíe los datos.");
            return null;
        }

        var transformaciones = (body.Transformaciones ?? []).Select(t => (t ?? string.Empty).Trim().ToUpperInvariant()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (transformaciones.Count > 0 && !def.Flags.PermiteTransformaciones)
            errores.Agregar("transformaciones", "Este tipo de trámite no permite transformaciones.");
        foreach (var t in transformaciones.Where(t => !Transformaciones.Contains(t)))
            errores.Agregar("transformaciones", $"La transformación {t} no es válida.");

        var observaciones = body.Observaciones?.Trim();
        if (observaciones is { Length: > MaxTextoLargo })
            errores.Agregar("observaciones", $"Use hasta {MaxTextoLargo} caracteres.");

        return errores.Hay ? null : new RequisitosDatos(transformaciones, string.IsNullOrEmpty(observaciones) ? null : observaciones);
    }

    // ── Completitud ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Placa o VIN de la solicitud y documento del titular, ya capturados en el diagnóstico o en las personas.
    /// El formulario del tipo los pide otra vez como campos; no hace falta volver a escribirlos.
    /// </summary>
    public readonly record struct CapturaConsulta(string? Placa, string? Vin, string? TipoDocumentoTitular, bool TitularTieneDocumento);

    /// <summary>
    /// Qué le falta al borrador y en qué paso se corrige. Un documento condicional (<c>condicion</c> con texto) no bloquea:
    /// la condición se evalúa en servidor al crear el trámite, no aquí. El comprobante de pago NO es requisito: el pago está en otra épica y el
    /// trámite se entrega a FLIT 2 como borrador (decisión del PO); el endpoint de comprobante sigue disponible como adjunto opcional.
    /// La placa, el VIN y el documento del titular cuentan como llenos si ya se capturaron al consultar o al registrar a la persona.
    /// </summary>
    public static (IReadOnlyList<PasoEstadoDto> Pasos, IReadOnlyList<PendienteDto> Pendientes) Completitud(
        DefinicionTipoDto def,
        IReadOnlyDictionary<string, string> campos,
        DatosSolicitud datos,
        IReadOnlyList<(string Rol, short Ordinal)> personas,
        IReadOnlySet<string> documentosCargados,
        CapturaConsulta captura = default,
        ValoresActualesVehiculo? runt = null)
    {
        string? PasoDe(string sectionType) => def.Pasos.FirstOrDefault(p => p.SectionTypes.Contains(sectionType, StringComparer.Ordinal))?.Clave;

        var pendientes = new List<PendienteDto>();
        foreach (var paso in def.Pasos)
        {
            foreach (var campo in paso.Campos.Where(c => c.Obligatorio && !CampoCubierto(c.FieldKey, campos, captura)))
                pendientes.Add(new PendienteDto("campo_requerido", campo.FieldKey, paso.Clave));
        }

        foreach (var rol in def.Actores.Where(r => r.Min > 0 && personas.Count(p => p.Rol == r.Rol) < r.Min))
            pendientes.Add(new PendienteDto("actor_requerido", rol.Rol, PasoDe("actor_form")));

        if (def.Flags.RequiereComercial && datos.Comercial?.Valor is not > 0)
            pendientes.Add(new PendienteDto("comercial_requerido", "valorComercial", PasoDe("commercial")));

        var prendaExigida = (def.Flags.RequierePrenda || datos.Prenda?.Tiene == true || !string.IsNullOrEmpty(def.Flags.AccionPrenda))
            && !LevantarSinAcreedor(def, datos.Prenda?.Decision?.Trim().ToLowerInvariant());
        if (prendaExigida && string.IsNullOrWhiteSpace(datos.Prenda?.Acreedor))
            pendientes.Add(new PendienteDto("prenda_acreedor_requerido", "prenda.acreedor", PasoDe("prenda_decision")));
        if (prendaExigida && string.IsNullOrWhiteSpace(datos.Prenda?.NitAcreedor))
            pendientes.Add(new PendienteDto("prenda_nit_requerido", "prenda.nitAcreedor", PasoDe("prenda_decision")));
        if (def.Flags.AccionPrenda == PrendaLevantar && string.IsNullOrWhiteSpace(datos.Prenda?.EntidadLevantamiento))
            pendientes.Add(new PendienteDto("prenda_entidad_requerida", "prenda.entidadLevantamiento", PasoDe("prenda_decision")));

        // Lo que reclama una declaración (certificado de blindaje, documentos de la causal) lo decide ella, no la matriz.
        var deLasDeclaraciones = DocumentosDeDeclaraciones(def);
        foreach (var doc in def.Documentos.Where(d => d.Obligatorio && string.IsNullOrWhiteSpace(d.Condicion) && !deLasDeclaraciones.Contains(d.DocCode) && !documentosCargados.Contains(d.DocCode)))
            pendientes.Add(new PendienteDto(PendienteDocumentoRequerido, doc.DocCode, PasoDe("document_checklist")));

        foreach (var x in PendientesDeDeclaraciones(def, campos, datos.Requisitos?.Transformaciones ?? [], documentosCargados, runt))
            pendientes.Add(new PendienteDto(x.Codigo, x.Referencia, x.Paso, x.Motivo));

        if (DocumentoDePrendaDelTipo(def, datos.Prenda?.Decision) is { } docPrenda && !documentosCargados.Contains(docPrenda)
            && !pendientes.Any(x => x.Codigo == PendienteDocumentoRequerido && string.Equals(x.Referencia, docPrenda, StringComparison.OrdinalIgnoreCase)))
            pendientes.Add(new PendienteDto(PendienteDocumentoRequerido, docPrenda, PasoDe("prenda_decision") ?? PasoDe("document_checklist")));

        var pasos = def.Pasos.Select(p =>
        {
            var propios = pendientes.Where(x => x.Paso == p.Clave).ToList();
            return new PasoEstadoDto(p.Clave, p.Titulo, propios.Count == 0, propios);
        }).ToList();
        return (pasos, pendientes);
    }

    /// <summary>
    /// Un campo obligatorio ya está si el ciudadano lo escribió, o si es la placa, el VIN o el documento del titular
    /// y ese dato ya quedó en la solicitud o en la persona.
    /// </summary>
    private static bool CampoCubierto(string fieldKey, IReadOnlyDictionary<string, string> campos, CapturaConsulta captura)
    {
        if (campos.TryGetValue(fieldKey, out var valor) && !string.IsNullOrWhiteSpace(valor))
            return true;

        return fieldKey switch
        {
            "plate" => !string.IsNullOrWhiteSpace(captura.Placa),
            "vin" => !string.IsNullOrWhiteSpace(captura.Vin),
            "owner_document_type" => !string.IsNullOrWhiteSpace(captura.TipoDocumentoTitular),
            "owner_document_number" => captura.TitularTieneDocumento,
            _ => false,
        };
    }
}
