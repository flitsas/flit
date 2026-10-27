using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Security;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #13643 AC5 — con TODO el catálogo de permisos otorgado, el JWT que viaja en la cookie <c>flit_token</c> no supera
/// 4 KB (límite de la cookie). El catálogo se lee de los DDL que clasifican <c>security.permissions.enforced</c>
/// (139 y siguientes): un slug nuevo entra aquí en cuanto su DDL lo declara, y si hace superar el tope la prueba falla.
/// El peor caso lleva los dos roles de un administrador y un nombre de compañía largo.
/// </summary>
public sealed class EffectivePermissionsTokenSizeTests(ITestOutputHelper output)
{
    private const int CookieLimitBytes = 4096;
    private static readonly Regex Row = new(@"\('(?<slug>[^']+)',\s*'(?:true|false|excluded)'\)", RegexOptions.Compiled);

    [Fact]
    public void AC5_ElTokenConTodoElCatalogoNoSuperaLosCuatroKB()
    {
        var slugs = CatalogSlugs();
        slugs.Should().HaveCountGreaterThan(40);

        var rsa = RSA.Create(2048);
        var issuer = new RsaJwtTokenIssuer(
            new JwtKeyMaterial { SigningKey = new RsaSecurityKey(rsa), Issuer = "https://api.flit.co", Audience = "flit-api" },
            Options.Create(new JwtSettings()));

        var issued = issuer.IssueToken(
            Guid.NewGuid(),
            "administrador.con.correo.largo@concesionario-de-ejemplo.com.co",
            Guid.NewGuid(),
            new string('N', 80),
            "9001234567",
            "COMPANY",
            "CONCESIONARIO",
            false,
            [new UserRoleSnapshot(Guid.NewGuid(), "AdminCompany"), new UserRoleSnapshot(Guid.NewGuid(), "admin_tramites")],
            slugs,
            "flit");

        var cookie = $"flit_token={issued.Token}";
        var bytes = Encoding.UTF8.GetByteCount(cookie);
        output.WriteLine($"Catalogo: {slugs.Count} slugs; JWT = {issued.Token.Length} bytes; cookie flit_token = {bytes} bytes (limite {CookieLimitBytes}).");
        new JwtSecurityTokenHandler().ReadJwtToken(issued.Token).Claims.Count(c => c.Type == "permissions").Should().BeGreaterThan(0);

        bytes.Should().BeLessThanOrEqualTo(
            CookieLimitBytes,
            $"con {slugs.Count} slugs la cookie mide {bytes} bytes; un slug nuevo que la lleve sobre 4 KB debe resolverse (token reducido) antes de agregarlo");
    }

    private static List<string> CatalogSlugs()
    {
        var dir = Path.Combine(CoreApiSrc(), "Flit.Infrastructure", "Persistence", "Sql", "Ddl");
        return Directory.EnumerateFiles(dir, "*enforced*.sql")
            .SelectMany(f => Row.Matches(File.ReadAllText(f)).Select(m => m.Groups["slug"].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string CoreApiSrc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (File.Exists(Path.Combine(candidate, "Flit.Infrastructure", "Flit.Infrastructure.csproj")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró services/core-api/src");
    }
}
