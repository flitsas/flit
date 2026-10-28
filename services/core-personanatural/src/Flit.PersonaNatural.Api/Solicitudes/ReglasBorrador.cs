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
public sealed record ActorValidado(int Indice, string Rol, short Ordinal, string TipoDocumento, string Documento, string Nombre, string? Correo, string? Celular, string? Direccion);

/// <summary>
/// Reglas del borrador (HU #14047). TODAS salen de la definición congelada de la solicitud: ningún rol, campo, documento ni
/// capacidad se nombra aquí. Son funciones puras, sin base ni red, para que se prueben sin host.
/// </summary>
public static partial class ReglasBorrador
{
    public const int MaxTexto = 500;
    public const int MaxTextoLargo = 2000;

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
    public static IReadOnlyList<ActorValidado> ValidarActores(DefinicionTipoDto def, IReadOnlyList<ActorRequest>? actores, ErroresDeCampo errores)
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
            if (!CotizacionesService.CorreoValido(correo))
                errores.Agregar($"{pre}.correo", "El correo no es válido.");

            var celular = a.Celular is null ? null : new string(a.Celular.Where(c => char.IsDigit(c) || c == '+').ToArray());
            if (celular is not null && celular.Length > 0 && !Telefono().IsMatch(celular))
                errores.Agregar($"{pre}.celular", "El teléfono no es válido.");

            var direccion = a.Direccion?.Trim();
            if (direccion is { Length: > 200 })
                errores.Agregar($"{pre}.direccion", "La dirección es demasiado larga.");

            if (!errores.Hay)
                salida.Add(new ActorValidado(i, rol, ordinal, tipo, documento, nombre, correo, string.IsNullOrEmpty(celular) ? null : celular, string.IsNullOrWhiteSpace(direccion) ? null : direccion));
        }

        return salida;
    }

    // ── Campos del tipo ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Valida <c>field_values</c> contra los campos de la definición. Devuelve lo que se guarda: clave → valor canónico, o
    /// <c>null</c> para borrar un campo opcional. El <c>validation_schema</c> viaja al front tal cual; aquí se valida tipo,
    /// opciones y obligatoriedad.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ValidarCampos(DefinicionTipoDto def, IReadOnlyDictionary<string, JsonElement>? valores, ErroresDeCampo errores)
    {
        var salida = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (valores is null || valores.Count == 0)
        {
            errores.Agregar("valores", "Envíe al menos un campo.");
            return salida;
        }

        var campos = def.Pasos.SelectMany(p => p.Campos).GroupBy(c => c.FieldKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var (clave, valor) in valores)
        {
            var pre = $"valores.{clave}";
            if (!campos.TryGetValue(clave, out var campo))
            {
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

    // ── Comercial y prenda ───────────────────────────────────────────────────────────────────────────────────────────

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

        var admitePrenda = def.Flags.RequierePrenda || def.Flags.PermitePrendaComplementaria;
        if (body.Prenda is { } p)
        {
            if (!admitePrenda)
                errores.Agregar("prenda", "Este tipo de trámite no admite prenda.");
            else
            {
                var tiene = def.Flags.RequierePrenda || p.TienePrenda;
                var acreedor = p.Acreedor?.Trim();
                var nit = new string((p.NitAcreedor ?? string.Empty).Where(char.IsDigit).ToArray());
                if (tiene && string.IsNullOrEmpty(acreedor))
                    errores.Agregar("prenda.acreedor", "Indique el acreedor de la prenda.");
                else if (acreedor is { Length: > 150 })
                    errores.Agregar("prenda.acreedor", "El nombre del acreedor es demasiado largo.");
                if (tiene && !Nit().IsMatch(nit))
                    errores.Agregar("prenda.nitAcreedor", "Indique el NIT del acreedor.");
                if (!errores.Hay)
                    prenda = tiene ? new PrendaDatos(true, acreedor, nit) : new PrendaDatos(false, null, null);
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
    /// Qué le falta al borrador y en qué paso se corrige. Un documento condicional (<c>condicion</c> con texto) no bloquea:
    /// la condición se evalúa en servidor al crear el trámite, no aquí. El comprobante de pago NO es requisito: el pago está en otra épica y el
    /// trámite se entrega a FLIT 2 como borrador (decisión del PO); el endpoint de comprobante sigue disponible como adjunto opcional.
    /// </summary>
    public static (IReadOnlyList<PasoEstadoDto> Pasos, IReadOnlyList<PendienteDto> Pendientes) Completitud(
        DefinicionTipoDto def,
        IReadOnlyDictionary<string, string> campos,
        DatosSolicitud datos,
        IReadOnlyList<(string Rol, short Ordinal)> personas,
        IReadOnlySet<string> documentosCargados)
    {
        string? PasoDe(string sectionType) => def.Pasos.FirstOrDefault(p => p.SectionTypes.Contains(sectionType, StringComparer.Ordinal))?.Clave;

        var pendientes = new List<PendienteDto>();
        foreach (var paso in def.Pasos)
        {
            foreach (var campo in paso.Campos.Where(c => c.Obligatorio && !(campos.TryGetValue(c.FieldKey, out var v) && !string.IsNullOrWhiteSpace(v))))
                pendientes.Add(new PendienteDto("campo_requerido", campo.FieldKey, paso.Clave));
        }

        foreach (var rol in def.Actores.Where(r => r.Min > 0 && personas.Count(p => p.Rol == r.Rol) < r.Min))
            pendientes.Add(new PendienteDto("actor_requerido", rol.Rol, PasoDe("actor_form")));

        if (def.Flags.RequiereComercial && datos.Comercial?.Valor is not > 0)
            pendientes.Add(new PendienteDto("comercial_requerido", "valorComercial", PasoDe("commercial")));

        var prendaExigida = def.Flags.RequierePrenda || datos.Prenda?.Tiene == true;
        if (prendaExigida && string.IsNullOrWhiteSpace(datos.Prenda?.Acreedor))
            pendientes.Add(new PendienteDto("prenda_acreedor_requerido", "prenda.acreedor", PasoDe("prenda_decision")));
        if (prendaExigida && string.IsNullOrWhiteSpace(datos.Prenda?.NitAcreedor))
            pendientes.Add(new PendienteDto("prenda_nit_requerido", "prenda.nitAcreedor", PasoDe("prenda_decision")));

        foreach (var doc in def.Documentos.Where(d => d.Obligatorio && string.IsNullOrWhiteSpace(d.Condicion) && !documentosCargados.Contains(d.DocCode)))
            pendientes.Add(new PendienteDto("documento_requerido", doc.DocCode, PasoDe("document_checklist")));

        var pasos = def.Pasos.Select(p =>
        {
            var propios = pendientes.Where(x => x.Paso == p.Clave).ToList();
            return new PasoEstadoDto(p.Clave, p.Titulo, propios.Count == 0, propios);
        }).ToList();
        return (pasos, pendientes);
    }
}
