using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Flit.Modules.Quipux.Domain.Mapeo;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// HU #12667 (y RN-09 de la Épica #12552) — la frontera del formato estándar.
///
/// <para>
/// Al revisar ICT y Quipux no quedó nada de presentación por cambiar: sus pantallas y sus reportes
/// ya pasaron por las HU #12664 y #12665. Lo que quedó fueron dos fechas que <b>parecen</b> estar
/// fuera del estándar y deben seguir estándolo, porque no son presentación:
/// </para>
///
/// <list type="bullet">
///   <item>El nombre del documento de Quipux (<c>yyyyMMdd_HHmm</c>) se persiste y es la clave con la
///   que se correlaciona un trámite. Su propia documentación lo advierte: en 1.0 se regeneraba en
///   cada intento y un reintento producía un nombre distinto, con lo que el trámite quedaba
///   imposible de consultar y podía duplicarse en Quipux.</item>
///   <item>La fecha del proceso de estado de ICT (<c>yyyy-MM-ddTHH:mm:ss.fffZ</c>) es
///   serialización ISO 8601 de una carga útil, no texto que alguien lea.</item>
/// </list>
///
/// <para>
/// Este test existe porque «no hay nada que cambiar» no se puede verificar leyendo el código: el
/// riesgo real es un barrido futuro que unifique todas las fechas y se lleve estas dos por delante.
/// </para>
/// </summary>
public class FormatoFechaAlcanceTests
{
    [Fact]
    public void ElNombreDeDocumentoDeQuipuxNoAdoptaElFormatoEstandar()
    {
        // 14:05 de Colombia, expresado ya en -05:00: el nombre lo usa tal cual, sin convertir.
        var momento = new DateTimeOffset(2026, 9, 17, 14, 5, 0, TimeSpan.FromHours(-5));

        var nombre = QuipuxDocumentNameBuilder.Build(
            razonSocial: "Transportes Andinos SAS",
            prefijo: "TR",
            momento: momento,
            sufijo: "ABC123",
            maxLongitudEmpresa: 35);

        nombre.Should().Contain("20260917_1405",
            "es la clave de correlación con Quipux; con DD/MM/YYYY HH:mm el nombre dejaría de " +
            "ser un identificador válido y el trámite no se podría consultar");
        nombre.Should().NotContain("17/09/2026");
    }

    [Fact]
    public void LaCargaUtilDeIctSigueSerializandoEnIso8601()
    {
        var fuente = Path.Combine(
            RaizDeServicios(), "core-ict", "src", "Flit.Ict.Infrastructure",
            "Persistence", "Repositories", "StatusProcessV1Query.cs");

        File.Exists(fuente).Should().BeTrue($"no se encontró {fuente}");

        File.ReadAllText(fuente).Should().Contain("yyyy-MM-ddTHH:mm:ss.fffZ",
            "es el formato de cable de la respuesta, no presentación: cambiarlo rompe al consumidor");
    }

    [Fact]
    public void LaClaveSemanalDeLasSeriesDeOtNoAdoptaElFormatoEstandar()
    {
        var fuente = Path.Combine(
            RaizDeServicios(), "core-api", "src", "Flit.Infrastructure",
            "Persistence", "Repositories", "OtMetricsReadRepository.cs");

        File.Exists(fuente).Should().BeTrue($"no se encontró {fuente}");

        File.ReadAllText(fuente).Should().Contain("ToString(\"yyyy-MM-dd\")",
            "es la clave por la que agrupan las gráficas, no una fecha que alguien lea");
    }

    [Fact]
    public void LaExcepcionDeSegundosSoloLaUsaElSelloDeLaImpronta()
    {
        // RN-11: FormatoFecha.SelloDeTiempo es el único formato con segundos que queda en la
        // plataforma. Vive en la clase canónica para poder comprobar justo esto: que nadie más
        // la llame. Sin el test, la excepción se convierte en una puerta abierta.
        var src = Path.Combine(RaizDeServicios(), "core-api", "src");
        var sep = Path.DirectorySeparatorChar;

        var llamantes = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"FormatoFecha\.SelloDeTiempo\s*\("))
            .Select(f => Path.GetFileName(f))
            .ToArray();

        llamantes.Should().BeEquivalentTo(["ImprontaManualStamper.cs"]);
    }

    /// <summary>
    /// Se ancla en la ruta de ESTE archivo en tiempo de compilación y no en
    /// <c>AppContext.BaseDirectory</c>, para seguir encontrando el fuente aunque la compilación
    /// mande la salida fuera del repositorio.
    /// </summary>
    private static string RaizDeServicios([CallerFilePath] string esteArchivo = "")
    {
        // .../services/core-api/tests/Flit.Admin.Tests/Architecture/<este archivo>
        var dir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(esteArchivo)!, "..", "..", "..", ".."));

        Directory.Exists(dir).Should().BeTrue($"no se encontró el directorio de servicios en {dir}");
        return dir;
    }
}
