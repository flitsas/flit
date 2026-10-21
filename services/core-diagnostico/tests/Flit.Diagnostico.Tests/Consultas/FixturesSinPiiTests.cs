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
/// otra cadena de seis o más dígitos en un fixture también debe caer en ese rango.</item>
/// <item>Nombres: solo los de <see cref="NombresFicticios"/>.</item>
/// <item>Sin direcciones, teléfonos ni correos.</item>
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

    [GeneratedRegex("[0-9]{6,}")]
    private static partial Regex SecuenciaLarga();

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
        {
            using var doc = JsonDocument.Parse(json);
            Revisar(archivo, "$", null, doc.RootElement, hallazgos, ref documentos);
        }

        hallazgos.Should().BeEmpty();
        documentos.Should().BeGreaterThanOrEqualTo(8, "los fixtures traen documentos de propietarios y locatarios");
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

    private static bool EsFecha(string texto) => DateTimeOffset.TryParse(texto, System.Globalization.CultureInfo.InvariantCulture, out _);
}
