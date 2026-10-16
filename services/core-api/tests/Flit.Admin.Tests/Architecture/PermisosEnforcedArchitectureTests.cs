using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// HU #13641 (Feature #13100 FA-6, Épica #12753) — AC2: el marcador <c>security.permissions.enforced</c> (DDL 139) no miente.
/// Un slug <c>true</c> debe aparecer como literal en el código de enforcement de algún servicio
/// (<c>.RequirePermission</c>/<c>PermissionRequirement</c>, <c>IdentityModuleAccessFilter</c>, <c>OtPermissionMap</c>, core-ict
/// <c>PlatformAccess</c>, core-identity <c>AdminResetPasswordHandler</c>, constantes de endpoints); un slug <c>false</c> no puede
/// aparecer. El barrido es léxico: lee <c>services/*/src/**/*.cs</c> sin migraciones, sin el seeder de desarrollo y sin líneas de
/// comentario, y busca el slug entre comillas. Límite conocido: una constante declarada pero sin uso cuenta como "exigido".
/// <para>Uso de ejemplo: <c>Verificar(catalogo, slug =&gt; literales.Contains(slug))</c> devuelve un mensaje por slug incoherente.</para>
/// </summary>
public sealed class PermisosEnforcedArchitectureTests
{
    private static readonly Regex Row = new(@"\('(?<slug>[^']+)',\s*'(?<v>true|false|excluded)'\)", RegexOptions.Compiled);

    [Fact]
    public void AC2_CadaSlugMarcadoTrueSeExigeYCadaFalseNoSeExige()
    {
        var catalogo = LeerCatalogoDelDdl();
        var literales = LiteralesDeEnforcement();

        Verificar(catalogo, literales.Contains).Should().BeEmpty();
    }

    [Fact]
    public void AC3_AC4_ElCatalogoDelDdlClasificaLosExcluidosYLosOt()
    {
        var catalogo = LeerCatalogoDelDdl();

        foreach (var slug in new[] { "usuarios.manage", "rbac.manage", "auth.me.read", "banners.manage" })
            catalogo[slug].Should().Be("excluded", slug);
        foreach (var slug in new[] { "tramites", "improntas", "reportes", "mandatos", "mandatarios", "documentos", "usuarios", "integracion" })
            catalogo[$"ot.{slug}.manage"].Should().Be("true", slug);
    }

    [Fact]
    public void Verificador_FallaNombrandoElSlug()
    {
        var catalogo = new Dictionary<string, string>
        {
            ["a.exigido"] = "true", ["b.huerfano"] = "true", ["c.noExigido"] = "false", ["d.colado"] = "false", ["e.fuera"] = "excluded",
        };

        var errores = Verificar(catalogo, slug => slug is "a.exigido" or "d.colado" or "e.fuera");

        errores.Should().HaveCount(2);
        errores.Should().ContainSingle(e => e.Contains("b.huerfano") && e.Contains("true"));
        errores.Should().ContainSingle(e => e.Contains("d.colado") && e.Contains("false"));
    }

    internal static List<string> Verificar(IReadOnlyDictionary<string, string> catalogo, Func<string, bool> seExige)
    {
        var errores = new List<string>();
        foreach (var (slug, valor) in catalogo)
        {
            if (valor == "true" && !seExige(slug))
                errores.Add($"'{slug}' está enforced=true pero ningún punto de enforcement lo exige");
            else if (valor == "false" && seExige(slug))
                errores.Add($"'{slug}' está enforced=false pero el código sí lo exige");
        }

        return errores;
    }

    private static Dictionary<string, string> LeerCatalogoDelDdl()
    {
        var ddl = Path.Combine(CoreApiSrc(), "Flit.Infrastructure", "Persistence", "Sql", "Ddl", "139-HU13641-permisos-enforced.sql");
        var catalogo = Row.Matches(File.ReadAllText(ddl)).ToDictionary(m => m.Groups["slug"].Value, m => m.Groups["v"].Value);
        catalogo.Should().HaveCountGreaterThan(40, "el DDL 139 es la fuente única del marcador");
        return catalogo;
    }

    private static HashSet<string> LiteralesDeEnforcement()
    {
        var services = Directory.GetParent(CoreApiSrc())!.Parent!.FullName;
        var quoted = new Regex("\"([^\"\\s]+)\"", RegexOptions.Compiled);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateDirectories(services).Select(d => Path.Combine(d, "src")).Where(Directory.Exists)
                     .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)))
        {
            var rel = file.Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains("/Migrations/") || rel.EndsWith("/DevelopmentAuthSeeder.cs", StringComparison.Ordinal))
                continue;
            foreach (var line in File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)))
                foreach (Match m in quoted.Matches(line))
                    found.Add(m.Groups[1].Value);
        }

        found.Should().Contain(["ot.tramites.manage", "ict.pii.reveal", "security.users.reset_password"], "el barrido debe leer core-api, core-ict y core-identity");
        return found;
    }

    private static string CoreApiSrc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (File.Exists(Path.Combine(candidate, "Flit.Infrastructure", "Flit.Infrastructure.csproj")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró services/core-api/src");
    }
}
