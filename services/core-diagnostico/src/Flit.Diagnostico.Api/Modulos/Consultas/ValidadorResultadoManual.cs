using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>Una persona del cuerpo del llenado manual: su id, su resultado (aún sin validar) y su posición en el cuerpo.</summary>
internal sealed record PersonaManual(Guid PersonaId, JsonElement Resultado, int Posicion);

/// <summary>
/// El cuerpo del llenado manual ya leído (HU #13800): la versión de la celda que vio el backoffice y, según la fuente, el
/// <see cref="Resultado"/> (fuentes del vehículo) o las <see cref="Personas"/> (fuentes de persona).
/// </summary>
internal sealed record CuerpoManual(long Version, JsonElement? Resultado, IReadOnlyList<PersonaManual>? Personas);

/// <summary>
/// Valida el llenado manual del backoffice contra el tipo normalizado de cada fuente (HU #13800, F06-C8) y lo convierte en el
/// MISMO payload que deja la consulta por API: <see cref="ResultadoSimit"/> (SIMIT_VEHICULO y la consulta <c>SIMIT</c> de una
/// persona), <see cref="ResultadoAvaluo"/> (AVALUO), <see cref="ResultadoPersonaRunt"/> (<c>RUNT_PERSONA</c>) y
/// <see cref="ResultadoRues"/> (<c>RUES</c>), serializados con las opciones de los ejecutores. Así la compañía no puede distinguir
/// un dato manual de uno de API (R-20). Reglas:
/// <list type="bullet">
/// <item>Un campo que el tipo no tiene, un tipo de dato equivocado, un obligatorio que falta o un valor fuera de su catálogo
/// responde 422 <see cref="CodigoInvalido"/> con <c>campo</c> (ruta del cuerpo, p. ej. <c>resultado.fuentes[0].valor</c>).</item>
/// <item>La fecha del dato la fija el servidor: el avalúo no acepta <c>fecha</c> en sus fuentes (se pone el día de hoy en
/// Colombia, como en la consulta por API).</item>
/// <item>RUNT e IMPUESTO_DEPARTAMENTAL no admiten llenado manual (Q3, R-9): <see cref="AdmiteManual"/>.</item>
/// </list>
/// Servicio puro: sin base ni HTTP.
/// </summary>
/// <example>
/// <code>
/// var cuerpo = ValidadorResultadoManual.LeerCuerpo(Fuente.SimitVehiculo, json);
/// var payload = ValidadorResultadoManual.Vehiculo(Fuente.SimitVehiculo, cuerpo.Resultado!.Value, hoy); // {"semaforo":…}
/// </code>
/// </example>
internal static partial class ValidadorResultadoManual
{
    public const string CodigoInvalido = "RESULTADO_INVALIDO";
    public const int LargoTexto = 120;
    public const int MaxComparendos = 500;
    public const int MaxLicencias = 30;

    private static readonly string[] Semaforos = ["VERDE", "AMARILLO", "ROJO"];
    private static readonly string[] FuentesAvaluo = ["fasecolda", "mercado_libre", "base_gravable"];
    private static readonly string[] EstadosAvaluo = [ValorFuenteAvaluo.Ok, ValorFuenteAvaluo.SinDatos, ValorFuenteAvaluo.Error];

    /// <summary>Verdadero si la fuente se puede llenar a mano: todas salvo RUNT (Q3) e IMPUESTO_DEPARTAMENTAL (R-9).</summary>
    public static bool AdmiteManual(Fuente fuente) => fuente is not (Fuente.Runt or Fuente.ImpuestoDepartamental);

    /// <summary>
    /// Lee el sobre del cuerpo: <c>version</c> (entero ≥ 0) y <c>resultado</c> (objeto) en las fuentes del vehículo, o
    /// <c>personas</c> (arreglo no vacío de <c>{persona_id, resultado}</c>, sin repetir persona) en las de persona.
    /// </summary>
    public static CuerpoManual LeerCuerpo(Fuente fuente, JsonElement cuerpo)
    {
        var raiz = Objeto.De(cuerpo, string.Empty);
        var porPersona = EjecutorPorPersonas.EsPorPersona(fuente);
        raiz.Solo("version", porPersona ? "personas" : "resultado");
        var version = raiz.Entero("version", requerido: true, minimo: 0)!.Value;
        if (!porPersona)
        {
            var resultado = raiz.Requerido("resultado", JsonValueKind.Object);
            raiz.Fin();
            return new CuerpoManual(version, resultado, null);
        }

        var personas = new List<PersonaManual>();
        var vistas = new HashSet<Guid>();
        var lista = raiz.Arreglo("personas", requerido: true, maximo: 50)!;
        if (lista.Count == 0)
            throw Invalido("personas", "Indica al menos una persona en error.");
        for (var i = 0; i < lista.Count; i++)
        {
            var campo = $"personas[{i}]";
            var persona = Objeto.De(lista[i], campo);
            var texto = persona.Texto("persona_id", requerido: true, largo: 36);
            if (!Guid.TryParse(texto, out var id) || id == Guid.Empty || !vistas.Add(id))
                throw Invalido($"{campo}.persona_id", "persona_id es el id de una persona en error de la celda, sin repetir.");
            var resultado = persona.Requerido("resultado", JsonValueKind.Object);
            persona.Fin();
            personas.Add(new PersonaManual(id, resultado, i));
        }

        raiz.Fin();
        return new CuerpoManual(version, null, personas);
    }

    /// <summary>Payload (JSON) de una celda del vehículo llenada a mano: SIMIT_VEHICULO o AVALUO.</summary>
    public static string Vehiculo(Fuente fuente, JsonElement resultado, DateOnly hoy) => fuente switch
    {
        Fuente.SimitVehiculo => JsonSerializer.Serialize(Simit(resultado, "resultado"), EjecutorPorPersonas.Json),
        Fuente.Avaluo => JsonSerializer.Serialize(Avaluo(resultado, "resultado", hoy), EjecutorPorPersonas.Json),
        _ => throw new ArgumentOutOfRangeException(nameof(fuente), fuente, "No es una fuente del vehículo que admita llenado manual."),
    };

    /// <summary>Resultado de UNA persona según su consulta (<c>RUNT_PERSONA</c>, <c>RUES</c> o <c>SIMIT</c>), como lo guarda la API.</summary>
    public static JsonElement Persona(string consulta, JsonElement resultado, string campo) => consulta switch
    {
        EjecutorCeldaRuntPersona.ConsultaRuntPersona => JsonSerializer.SerializeToElement(RuntPersona(resultado, campo), EjecutorPorPersonas.Json),
        EjecutorCeldaRuntPersona.ConsultaRues => JsonSerializer.SerializeToElement(Rues(resultado, campo), EjecutorPorPersonas.Json),
        EjecutorCeldaSimitPersona.ConsultaSimit => JsonSerializer.SerializeToElement(Simit(resultado, campo), EjecutorPorPersonas.Json),
        _ => throw Invalido(campo, "La consulta de esta persona no admite llenado manual."),
    };

    public static ReglaException Invalido(string campo, string mensaje) => new(CodigoInvalido, mensaje) { Campo = campo };

    private static ResultadoSimit Simit(JsonElement elemento, string campo)
    {
        var o = Objeto.De(elemento, campo);
        var semaforo = o.Catalogo("semaforo", Semaforos, requerido: false);
        var cantidad = o.Entero("cantidad_comparendos", requerido: false, minimo: 0);
        var lista = o.Arreglo("comparendos", requerido: true, maximo: MaxComparendos)!;
        o.Fin();

        var comparendos = new List<ComparendoSimit>(lista.Count);
        var numeros = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lista.Count; i++)
        {
            var c = Objeto.De(lista[i], $"{campo}.comparendos[{i}]");
            var numero = c.Texto("numero", requerido: true, largo: 60)!;
            if (!numeros.Add(numero))
                throw Invalido(c.Ruta("numero"), "El número de comparendo está repetido.");
            var fecha = c.Fecha("fecha", soloAnio: false);
            var valor = c.Texto("valor", requerido: false, largo: 20);
            if (valor is not null && !ValorEnPesos().IsMatch(valor))
                throw Invalido(c.Ruta("valor"), "valor es un número decimal en pesos (p. ej. 468500 o 468500.50).");
            var estado = c.Texto("estado", requerido: false, largo: 60);
            var organismo = c.Texto("organismo", requerido: false, largo: LargoTexto);
            c.Fin();
            comparendos.Add(new ComparendoSimit(numero, fecha, valor, estado, organismo));
        }

        if (cantidad is { } total && total != comparendos.Count)
            throw Invalido($"{campo}.cantidad_comparendos", "cantidad_comparendos debe coincidir con los comparendos enviados.");
        return new ResultadoSimit(semaforo, comparendos.Count, comparendos);
    }

    private static ResultadoAvaluo Avaluo(JsonElement elemento, string campo, DateOnly hoy)
    {
        var o = Objeto.De(elemento, campo);
        var sinValor = o.Booleano("sin_valor_comercial", requerido: true)!.Value;
        var sugerido = o.Entero("valor_sugerido", requerido: false, minimo: 1);
        var principal = o.Catalogo("fuente_principal", FuentesAvaluo, requerido: false);
        var lista = o.Arreglo("fuentes", requerido: true, maximo: FuentesAvaluo.Length)!;
        o.Fin();

        var fecha = hoy.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var fuentes = new List<ValorFuenteAvaluo>(lista.Count);
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < lista.Count; i++)
        {
            var f = Objeto.De(lista[i], $"{campo}.fuentes[{i}]");
            var fuente = f.Catalogo("fuente", FuentesAvaluo, requerido: true)!;
            if (!vistas.Add(fuente))
                throw Invalido(f.Ruta("fuente"), "La fuente del avalúo está repetida.");
            var estado = f.Catalogo("estado", EstadosAvaluo, requerido: true)!;
            var valor = f.Entero("valor", requerido: estado == ValorFuenteAvaluo.Ok, minimo: 1);
            if (estado != ValorFuenteAvaluo.Ok && valor is not null)
                throw Invalido(f.Ruta("valor"), "valor solo va en una fuente con estado OK.");
            var muestras = f.Entero("muestras", requerido: false, minimo: 1);
            f.Fin();
            fuentes.Add(new ValorFuenteAvaluo(fuente, estado, valor, muestras is { } m ? (int)m : null, fecha));
        }

        if (sinValor)
        {
            if (sugerido is not null)
                throw Invalido($"{campo}.valor_sugerido", "Sin valor comercial no lleva valor_sugerido.");
            if (principal is not null)
                throw Invalido($"{campo}.fuente_principal", "Sin valor comercial no lleva fuente_principal.");
            if (fuentes.Any(f => f.Estado == ValorFuenteAvaluo.Ok))
                throw Invalido($"{campo}.sin_valor_comercial", "Una fuente con valor (OK) contradice «sin valor comercial».");
            return new ResultadoAvaluo(true, null, null, fuentes);
        }

        if (sugerido is null)
            throw Invalido($"{campo}.valor_sugerido", "valor_sugerido es obligatorio (entero en pesos mayor que 0) si hay valor comercial.");
        if (principal is null || !fuentes.Any(f => f.Fuente == principal && f.Estado == ValorFuenteAvaluo.Ok))
            throw Invalido($"{campo}.fuente_principal", "fuente_principal es una de las fuentes enviadas con estado OK.");
        return new ResultadoAvaluo(false, sugerido, principal, fuentes);
    }

    private static ResultadoPersonaRunt RuntPersona(JsonElement elemento, string campo)
    {
        var o = Objeto.De(elemento, campo);
        var inscrito = o.Booleano("inscrito", requerido: true)!.Value;
        var estado = o.Texto("estado_conductor", requerido: false, largo: 60);
        var lista = o.Arreglo("licencias", requerido: true, maximo: MaxLicencias)!;
        o.Fin();

        var licencias = new List<LicenciaConduccion>(lista.Count);
        for (var i = 0; i < lista.Count; i++)
        {
            var l = Objeto.De(lista[i], $"{campo}.licencias[{i}]");
            var categoria = l.Texto("categoria", requerido: true, largo: 10)!;
            var estadoLicencia = l.Texto("estado", requerido: false, largo: 60);
            var vigencia = l.Fecha("vigente_hasta", soloAnio: false);
            l.Fin();
            licencias.Add(new LicenciaConduccion(categoria, estadoLicencia, vigencia));
        }

        return new ResultadoPersonaRunt(inscrito, estado, licencias);
    }

    private static ResultadoRues Rues(JsonElement elemento, string campo)
    {
        var o = Objeto.De(elemento, campo);
        var encontrada = o.Booleano("encontrada", requerido: true)!.Value;
        var estado = o.Texto("estado_matricula", requerido: false, largo: 60);
        var renovacion = o.Fecha("ultima_renovacion", soloAnio: true);
        o.Fin();
        return new ResultadoRues(encontrada, estado, renovacion);
    }

    [GeneratedRegex(@"^\d{1,15}(\.\d{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ValorEnPesos();

    /// <summary>
    /// Un objeto JSON del cuerpo con su ruta: lee cada clave una vez y <see cref="Fin"/> rechaza las que el tipo no tiene (y las
    /// repetidas), así que el esquema es cerrado.
    /// </summary>
    private sealed class Objeto
    {
        private readonly Dictionary<string, JsonElement> _claves;
        private readonly HashSet<string> _leidas = new(StringComparer.Ordinal);
        private readonly string _ruta;

        private Objeto(Dictionary<string, JsonElement> claves, string ruta)
        {
            _claves = claves;
            _ruta = ruta;
        }

        public static Objeto De(JsonElement elemento, string ruta)
        {
            if (elemento.ValueKind != JsonValueKind.Object)
                throw new ReglaException(CodigoInvalido, "Se esperaba un objeto JSON.") { Campo = string.IsNullOrEmpty(ruta) ? null : ruta };
            var claves = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var propiedad in elemento.EnumerateObject())
            {
                if (!claves.TryAdd(propiedad.Name, propiedad.Value))
                    throw Invalido(Unir(ruta, propiedad.Name), "La clave está repetida.");
            }

            return new Objeto(claves, ruta);
        }

        public string Ruta(string clave) => Unir(_ruta, clave);

        /// <summary>Rechaza de entrada la primera clave que no está en <paramref name="permitidas"/>.</summary>
        public void Solo(params string[] permitidas)
        {
            var sobrante = _claves.Keys.FirstOrDefault(k => !permitidas.Contains(k, StringComparer.Ordinal));
            if (sobrante is not null)
                throw Invalido(Ruta(sobrante), $"El campo «{sobrante}» no existe en el cuerpo del llenado de esta fuente.");
        }

        /// <summary>Rechaza la primera clave que el tipo no tiene.</summary>
        public void Fin()
        {
            var sobrante = _claves.Keys.FirstOrDefault(k => !_leidas.Contains(k));
            if (sobrante is not null)
                throw Invalido(Ruta(sobrante), $"El campo «{sobrante}» no existe en el esquema de esta fuente.");
        }

        public JsonElement Requerido(string clave, JsonValueKind tipo)
        {
            if (Valor(clave) is not { } valor)
                throw Invalido(Ruta(clave), $"{clave} es obligatorio.");
            if (valor.ValueKind != tipo)
                throw Invalido(Ruta(clave), $"{clave} debe ser {Nombre(tipo)}.");
            return valor;
        }

        public bool? Booleano(string clave, bool requerido)
        {
            if (Valor(clave) is not { } valor)
                return requerido ? throw Invalido(Ruta(clave), $"{clave} es obligatorio.") : null;
            return valor.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw Invalido(Ruta(clave), $"{clave} debe ser true o false."),
            };
        }

        public long? Entero(string clave, bool requerido, long minimo)
        {
            if (Valor(clave) is not { } valor)
                return requerido ? throw Invalido(Ruta(clave), $"{clave} es obligatorio.") : null;
            if (valor.ValueKind != JsonValueKind.Number || !valor.TryGetInt64(out var numero) || numero < minimo)
                throw Invalido(Ruta(clave), $"{clave} debe ser un entero mayor o igual que {minimo}.");
            return numero;
        }

        public string? Texto(string clave, bool requerido, int largo)
        {
            if (Valor(clave) is not { } valor)
                return requerido ? throw Invalido(Ruta(clave), $"{clave} es obligatorio.") : null;
            if (valor.ValueKind != JsonValueKind.String)
                throw Invalido(Ruta(clave), $"{clave} debe ser texto.");
            var texto = valor.GetString()!.Trim();
            if (texto.Length == 0)
                return requerido ? throw Invalido(Ruta(clave), $"{clave} no puede ir vacío.") : null;
            if (texto.Length > largo)
                throw Invalido(Ruta(clave), $"{clave} admite como mucho {largo} caracteres.");
            return texto;
        }

        public string? Catalogo(string clave, string[] valores, bool requerido)
        {
            var texto = Texto(clave, requerido, largo: 40);
            if (texto is not null && !valores.Contains(texto, StringComparer.Ordinal))
                throw Invalido(Ruta(clave), $"{clave} es uno de: {string.Join(", ", valores)}.");
            return texto;
        }

        /// <summary>Fecha AAAA-MM-DD (o solo el año AAAA si <paramref name="soloAnio"/>); null si no viene.</summary>
        public string? Fecha(string clave, bool soloAnio)
        {
            var texto = Texto(clave, requerido: false, largo: 10);
            if (texto is null)
                return null;
            var valida = DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || (soloAnio && texto.Length == 4 && int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var anio) && anio is >= 1900 and <= 2100);
            if (!valida)
                throw Invalido(Ruta(clave), soloAnio ? $"{clave} es una fecha AAAA-MM-DD o un año AAAA." : $"{clave} es una fecha AAAA-MM-DD.");
            return texto;
        }

        public List<JsonElement>? Arreglo(string clave, bool requerido, int maximo)
        {
            if (Valor(clave) is not { } valor)
                return requerido ? throw Invalido(Ruta(clave), $"{clave} es obligatorio.") : null;
            if (valor.ValueKind != JsonValueKind.Array)
                throw Invalido(Ruta(clave), $"{clave} debe ser un arreglo.");
            var lista = valor.EnumerateArray().ToList();
            if (lista.Count > maximo)
                throw Invalido(Ruta(clave), $"{clave} admite como mucho {maximo} elementos.");
            return lista;
        }

        /// <summary>El valor de la clave (y la marca como leída); null si no viene o viene en null.</summary>
        private JsonElement? Valor(string clave)
        {
            _leidas.Add(clave);
            return _claves.TryGetValue(clave, out var valor) && valor.ValueKind != JsonValueKind.Null ? valor : null;
        }

        private static string Unir(string ruta, string clave) => string.IsNullOrEmpty(ruta) ? clave : $"{ruta}.{clave}";

        private static string Nombre(JsonValueKind tipo) => tipo switch
        {
            JsonValueKind.Object => "un objeto",
            JsonValueKind.Array => "un arreglo",
            _ => tipo.ToString().ToLowerInvariant(),
        };
    }
}
