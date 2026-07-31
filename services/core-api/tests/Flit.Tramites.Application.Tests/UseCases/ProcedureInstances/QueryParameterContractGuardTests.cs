using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #11139 — <b>guardia de parámetros opcionales de query</b>.
///
/// <para><b>Por qué existe.</b> En Minimal APIs, un parámetro <c>[FromQuery]</c> de tipo valor sin
/// <c>?</c> y sin valor por defecto es <b>obligatorio</b>: si el cliente lo omite, el framework
/// responde <c>400 BadHttpRequestException</c> <i>antes</i> de entrar al handler. Eso convirtió un flag
/// pensado como opcional (<c>force</c> del expediente consolidado) en un requisito, y el asistente
/// —que solo lo envía cuando vale <c>true</c>— quedó sin poder generar el expediente en su último paso.</para>
///
/// <para><b>Por qué no lo detectó nada.</b> No es un error de compilación ni de ejecución del código
/// propio: el binder falla en el borde HTTP, así que ninguna prueba de handler puede verlo. Solo se
/// manifiesta contra la API real, y únicamente por el camino que omite el parámetro.</para>
///
/// <para><b>Límite honesto.</b> Esto revisa el código fuente de los endpoints, no el comportamiento en
/// tiempo de ejecución. No demuestra que el binding funcione; demuestra que ningún endpoint declara un
/// flag de query como obligatorio por descuido, que es la forma concreta en que apareció el fallo.</para>
/// </summary>
public sealed class QueryParameterContractGuardTests
{
    private const string Endpoints = "services/core-api/src/Flit.Api/Endpoints";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "services", "core-api", "src")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("No se encontró la raíz del repositorio desde el directorio de test.");
    }

    /// <summary>
    /// Declaraciones <c>[FromQuery] bool &lt;nombre&gt;</c> sin <c>?</c> y sin valor por defecto.
    ///
    /// <para><b>Acotado a <c>bool</c> a propósito.</b> Un identificador obligatorio en la query es un
    /// diseño legítimo — pedir las placas disponibles sin decir de qué organismo no significa nada, y
    /// esos endpoints declaran <c>Guid transitOfficeId</c> con toda la intención. Un <b>flag</b>
    /// obligatorio, en cambio, casi siempre es un descuido: la gracia de un flag es que omitirlo
    /// equivalga a <c>false</c>, y si el binder lo exige, el camino "no lo activo" deja de existir.</para>
    /// </summary>
    private static readonly Regex Obligatorio = new(
        @"\[FromQuery(?:\([^)]*\))?\]\s+bool\s+(?<nombre>\w+)\s*(?<cola>[,)])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void NingunEndpointDeclaraUnFlagDeQueryComoObligatorio()
    {
        var raiz = Path.Combine(RepoRoot(), Endpoints.Replace('/', Path.DirectorySeparatorChar));
        Directory.Exists(raiz).Should().BeTrue("la carpeta de endpoints debe existir");

        var infractores = new List<string>();

        foreach (var fichero in Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories))
        {
            var fuente = File.ReadAllText(fichero);
            foreach (Match m in Obligatorio.Matches(fuente))
            {
                // `= valor` tras el nombre lo vuelve opcional; el regex ya excluye ese caso al exigir
                // que lo siguiente sea una coma o el cierre del paréntesis.
                infractores.Add($"{Path.GetFileName(fichero)}: bool {m.Groups["nombre"].Value}");
            }
        }

        infractores.Should().BeEmpty(
            "estos flags de query no son nullable ni tienen valor por defecto, así que Minimal APIs los "
            + "EXIGE y un cliente que los omita recibe 400 antes de llegar al handler. Declararlos "
            + "`bool?` o darles un valor por defecto: {0}",
            string.Join(" | ", infractores));
    }

    [Fact]
    public void LaGuardiaDetectaLaFormaExactaDelDefecto()
    {
        // Prueba de la propia guardia sobre el codigo que causo el Bug #11139, sin tocar produccion.
        var comoEstaba = "[FromQuery] bool force,\n            HttpContext http,";
        var corregido = "[FromQuery] bool? force,\n            HttpContext http,";
        var conDefecto = "[FromQuery] bool force = false)";

        Obligatorio.IsMatch(comoEstaba).Should().BeTrue("así estaba declarado cuando se rompió");
        Obligatorio.IsMatch(corregido).Should().BeFalse("nullable lo vuelve opcional");
        Obligatorio.IsMatch(conDefecto).Should().BeFalse("un valor por defecto también lo vuelve opcional");
    }
}
