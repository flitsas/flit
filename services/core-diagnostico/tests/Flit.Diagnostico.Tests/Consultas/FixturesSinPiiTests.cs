using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13725 AC1 (F03-C16, Ley 1581) — los fixtures de Intempo versionados son inventados. Rango ficticio declarado (para la
/// revisión del security-agent):
/// <list type="bullet">
/// <item>Placas: <c>TST</c> + tres dígitos (<see cref="RangoPlaca"/>).</item>
/// <item>VIN: <c>9ZZPRUEBA</c> + ocho dígitos (<see cref="RangoVin"/>).</item>
/// <item>Documentos (cédula y NIT): prefijo <c>99999</c> + cuatro o cinco dígitos (<see cref="RangoDocumento"/>); cualquier
/// otra cadena de cinco o más dígitos en un fixture también debe caer en ese rango (salvo GUID y fechas).</item>
/// <item>Nombres: solo los de <see cref="NombresFicticios"/>; todo texto sin dígitos bajo una clave que contenga
/// <c>nombre</c>, <c>razon</c> o <c>name</c> (sin distinguir mayúsculas) se trata como nombre.</item>
/// <item>Sin direcciones, teléfonos ni correos: los correos se buscan en todo texto, no solo por clave.</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// dotnet test tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~FixturesSinPiiTests"
/// </code>
/// </example>
public sealed partial class FixturesSinPiiTests
{
    public static readonly IReadOnlyList<string> NombresFicticios =
    [
        "PERSONA PRUEBA UNO",
        "EMPRESA FICTICIA SAS",
        "LEASING FICTICIO SA",
        "LOCATARIO PRUEBA DOS",
        "EMPRESA LOCATARIA FICTICIA SAS",
        "TRANSPORTES FICTICIOS SAS",
        "PERSONA PRUEBA TRES",
    ];

    public static readonly IReadOnlyList<string> Variantes =
    [
        "intempo-persona-natural.json",
        "intempo-nit.json",
        "intempo-leasing-locatario-c.json",
        "intempo-leasing-locatario-n.json",
        "intempo-servicio-publico.json",
        "intempo-departamento-no-soportado.json",
        "intempo-error-timeout.json",
        "intempo-error-respuesta-invalida.json",
    ];

    private static readonly HashSet<string> ClavesDocumento = new(StringComparer.OrdinalIgnoreCase)
    {
        "noDocumento", "ndoc", "documentNumber",
    };

    private static readonly HashSet<string> ClavesNombre = new(StringComparer.OrdinalIgnoreCase)
    {
        "nombreCompleto", "nombreRazonSocial", "names", "nombreAcreedor",
    };

    private static readonly string[] ClavesProhibidas = ["direccion", "telefono", "celular", "correo", "email", "address", "phone"];

    [GeneratedRegex("^TST[0-9]{3}$")]
    public static partial Regex RangoPlaca();

    [GeneratedRegex("^9ZZPRUEBA[0-9]{8}$")]
    public static partial Regex RangoVin();

    [GeneratedRegex("^99999[0-9]{4,5}$")]
    public static partial Regex RangoDocumento();

    [GeneratedRegex("[0-9]{5,}")]
    private static partial Regex SecuenciaLarga();

    [GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+")]
    private static partial Regex Correo();

    private static readonly string[] FragmentosClaveNombre = ["nombre", "razon", "name"];

    [Fact]
    public void Fixtures_cubren_persona_natural_NIT_y_leasing_con_locatario_C_y_N()
    {
        FixturesIntempo.Todos().Keys.Should().BeEquivalentTo(Variantes);
    }

    // AC1 — solo placas, VIN, documentos y nombres del rango ficticio declarado.
    [Fact]
    public void Fixtures_solo_usan_placas_y_documentos_del_rango_ficticio()
    {
        var fixtures = FixturesIntempo.Todos();
        fixtures.Should().NotBeEmpty();
        var hallazgos = new List<string>();
        var documentos = 0;
        foreach (var (archivo, json) in fixtures)
            documentos += Analizar(archivo, json, hallazgos);

        hallazgos.Should().BeEmpty();
        documentos.Should().BeGreaterThanOrEqualTo(8, "los fixtures traen documentos de propietarios y locatarios");
    }

    /// <summary>
    /// JSON inventado en memoria (nunca un fixture ni una muestra) con un caso por regla endurecida tras la revisión del
    /// security-agent: nombres bajo claves no listadas, correo en un campo libre y un número de cinco dígitos fuera de rango.
    /// </summary>
    private const string JsonConFugas = """
        {
          "informacionGeneral": { "noPlaca": "TST001", "modelo": "2020" },
          "propietariosActuales": [
            {
              "noDocumento": "999990001",
              "nombrePropietario": "NOMBRE INVENTADO FUERA DE LISTA",
              "RazonSocialTitular": "OTRA RAZON INVENTADA",
              "nombreRazonSocial": "PERSONA PRUEBA UNO",
              "observacion": "contacto persona@ejemplo.test",
              "referencia": "12345",
              "codigo": 54321
            }
          ]
        }
        """;

    // (a) Texto sin dígitos bajo cualquier clave con nombre/razon/name, salvo los de NombresFicticios.
    [Fact]
    public void Regla_nombre_detecta_texto_bajo_clave_con_nombre_razon_o_name_fuera_de_la_lista()
    {
        var hallazgos = new List<string>();
        Analizar("en-memoria", JsonConFugas, hallazgos);

        hallazgos.Should().Contain(h => h.Contains(".nombrePropietario") && h.Contains("nombre fuera de la lista ficticia"));
        hallazgos.Should().Contain(h => h.Contains(".RazonSocialTitular") && h.Contains("nombre fuera de la lista ficticia"));
        hallazgos.Should().NotContain(h => h.Contains(".nombreRazonSocial"), "PERSONA PRUEBA UNO está en NombresFicticios");
    }

    // (b) Correo (@ + dominio) en cualquier texto, no solo bajo claves de contacto.
    [Fact]
    public void Regla_correo_detecta_correo_en_cualquier_texto()
    {
        var hallazgos = new List<string>();
        Analizar("en-memoria", JsonConFugas, hallazgos);

        hallazgos.Should().Contain(h => h.Contains(".observacion") && h.Contains("correo"));
    }

    // (c) Secuencias de cinco o más dígitos fuera del rango ficticio, en texto y en número.
    [Fact]
    public void Regla_secuencia_detecta_cinco_digitos_fuera_del_rango_ficticio()
    {
        var hallazgos = new List<string>();
        Analizar("en-memoria", JsonConFugas, hallazgos);

        hallazgos.Should().Contain(h => h.Contains(".referencia") && h.Contains("fuera del rango ficticio"));
        hallazgos.Should().Contain(h => h.Contains(".codigo") && h.Contains("fuera del rango ficticio"));
        hallazgos.Should().NotContain(h => h.Contains(".modelo") || h.Contains(".noPlaca") || h.Contains(".noDocumento"));
    }

    // HU #13862 — la simulación del impuesto (FuenteImpuestoSimulada) solo inventa montos, años y el departamento pedido: ningún
    // nombre, documento, correo, placa ni proveedor, para todos los últimos dígitos. Los montos son números largos por naturaleza,
    // así que de las reglas de arriba solo se tolera «número largo» bajo monto_total y valor.
    [Fact]
    public async Task Simulacion_del_impuesto_solo_trae_montos_vigencias_y_departamento()
    {
        var ct = TestContext.Current.CancellationToken;
        var simulada = new FuenteImpuestoSimulada();
        var permitidas = new HashSet<string>(StringComparer.Ordinal) { "monto_total", "vigencias", "departamento_codigo", "anio", "valor", "pagada" };
        var conDato = 0;
        for (var digito = 0; digito <= 9; digito++)
        {
            var placa = $"TST30{digito}";
            var r = await simulada.ConsultarAsync(Guid.NewGuid(), new SolicitudImpuesto(placa, "05"), ct);
            if (r.Resultado is null)
                continue;
            conDato++;
            var json = JsonSerializer.Serialize(r.Resultado, Flit.Diagnostico.Api.Modulos.Consultas.EjecutorPorPersonas.Json);
            var hallazgos = new List<string>();
            Analizar($"impuesto-{placa}", json, hallazgos);
            hallazgos.Should().OnlyContain(h => h.Contains("número largo") && (h.Contains(".monto_total") || h.Contains(".valor")), placa);
            using var doc = JsonDocument.Parse(json);
            Claves(doc.RootElement).Should().OnlyContain(c => permitidas.Contains(c), placa);
            json.Should().NotContainAny([placa, "verifik", "proveedor", FuenteImpuestoSimulada.Transaccion]);
        }

        conDato.Should().Be(9, "solo el dígito 7 responde TIMEOUT");
    }

    private static IEnumerable<string> Claves(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().SelectMany(p => Claves(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => e.EnumerateArray().SelectMany(Claves),
        _ => [],
    };

    /// <summary>Revisa un JSON y agrega sus hallazgos; devuelve cuántos documentos encontró.</summary>
    private static int Analizar(string archivo, string json, List<string> hallazgos)
    {
        var documentos = 0;
        using var doc = JsonDocument.Parse(json);
        Revisar(archivo, "$", null, doc.RootElement, hallazgos, ref documentos);
        return documentos;
    }

    private static void Revisar(string archivo, string ruta, string? clave, JsonElement valor, List<string> hallazgos, ref int documentos)
    {
        if (clave is not null && ClavesProhibidas.Any(p => clave.Contains(p, StringComparison.OrdinalIgnoreCase)))
            hallazgos.Add($"{archivo} {ruta}: campo de contacto no permitido");

        switch (valor.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in valor.EnumerateObject())
                    Revisar(archivo, $"{ruta}.{p.Name}", p.Name, p.Value, hallazgos, ref documentos);
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var e in valor.EnumerateArray())
                    Revisar(archivo, $"{ruta}[{i++}]", clave, e, hallazgos, ref documentos);
                break;
            case JsonValueKind.String:
                RevisarTexto(archivo, ruta, clave, valor.GetString()!, hallazgos, ref documentos);
                break;
            case JsonValueKind.Number:
                if (SecuenciaLarga().IsMatch(valor.GetRawText()) && !RangoDocumento().IsMatch(valor.GetRawText()))
                    hallazgos.Add($"{archivo} {ruta}: número largo fuera del rango ficticio");
                break;
        }
    }

    private static void RevisarTexto(string archivo, string ruta, string? clave, string texto, List<string> hallazgos, ref int documentos)
    {
        if (Correo().IsMatch(texto))
            hallazgos.Add($"{archivo} {ruta}: correo no permitido");
        if (clave is null || texto.Length == 0)
            return;
        if (ClavesDocumento.Contains(clave))
        {
            documentos++;
            if (!RangoDocumento().IsMatch(texto))
                hallazgos.Add($"{archivo} {ruta}: documento fuera del rango ficticio");
        }
        else if (ClavesNombre.Contains(clave))
        {
            if (!NombresFicticios.Contains(texto))
                hallazgos.Add($"{archivo} {ruta}: nombre fuera de la lista ficticia");
        }
        else if (EsClaveNombre(clave) && !texto.Any(char.IsAsciiDigit))
        {
            if (!NombresFicticios.Contains(texto))
                hallazgos.Add($"{archivo} {ruta}: nombre fuera de la lista ficticia");
        }
        else if (string.Equals(clave, "noPlaca", StringComparison.OrdinalIgnoreCase))
        {
            if (!RangoPlaca().IsMatch(texto))
                hallazgos.Add($"{archivo} {ruta}: placa fuera del rango ficticio");
        }
        else if (string.Equals(clave, "noVin", StringComparison.OrdinalIgnoreCase))
        {
            if (!RangoVin().IsMatch(texto))
                hallazgos.Add($"{archivo} {ruta}: VIN fuera del rango ficticio");
        }
        else if (!Guid.TryParse(texto, out _) && SecuenciaLarga().Matches(texto).Any(m => !RangoDocumento().IsMatch(m.Value) && !EsFecha(texto)))
        {
            hallazgos.Add($"{archivo} {ruta}: secuencia de dígitos fuera del rango ficticio");
        }
    }

    private static bool EsClaveNombre(string clave) =>
        FragmentosClaveNombre.Any(f => clave.Contains(f, StringComparison.OrdinalIgnoreCase));

    private static bool EsFecha(string texto) => DateTimeOffset.TryParse(texto, System.Globalization.CultureInfo.InvariantCulture, out _);
}
