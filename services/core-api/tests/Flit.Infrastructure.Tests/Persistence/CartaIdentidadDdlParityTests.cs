using System.Reflection;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #14301 — el DDL 169 y el modelo no se desalinean: cada columna <c>letter_*</c> de la carta de identidad existe en el script y
/// está mapeada (NULL) en EF; el script es aditivo (no toca <c>certificate_hash</c> de Kyverum ni los CHECK) y la migración lleva
/// los atributos inline <c>[DbContext]</c>/<c>[Migration]</c> (sin Designer; sin ellos no se descubre y no corre al arrancar).
/// </summary>
public sealed class CartaIdentidadDdlParityTests
{
    private const string Script = "169-HU14301-carta-identidad-manual.sql";
    private const string MigrationId = "20261105120000_HU14301_CartaIdentidadManual";

    private static readonly Dictionary<string, string> Columnas = new()
    {
        [nameof(ProcedureInstanceBiometricValidation.CartaSerie)] = "letter_series",
        [nameof(ProcedureInstanceBiometricValidation.CartaSha256)] = "letter_sha256",
        [nameof(ProcedureInstanceBiometricValidation.CartaPdfSha256)] = "letter_pdf_sha256",
        [nameof(ProcedureInstanceBiometricValidation.CartaPath)] = "letter_path",
        [nameof(ProcedureInstanceBiometricValidation.CartaGeneratedAt)] = "letter_generated_at",
    };

    private static string LoadDdl()
    {
        using var stream = typeof(FlitDbContext).Assembly
            .GetManifestResourceStream($"Flit.Infrastructure.Persistence.Sql.Ddl.{Script}");
        stream.Should().NotBeNull($"el DDL embebido {Script} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return string.Join('\n', reader.ReadToEnd().Split('\n').Select(l =>
        {
            var corte = l.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? l[..corte] : l;
        }));
    }

    [Fact]
    public void ElDdlAgregaLasCincoColumnasDeFormaIdempotenteYAditiva()
    {
        var ddl = LoadDdl();

        foreach (var columna in Columnas.Values)
            ddl.Should().Contain($"ADD COLUMN IF NOT EXISTS {columna} ");

        ddl.Should().NotMatchRegex(@"(?s)(ADD|ALTER|DROP) COLUMN[^;]*certificate_hash", "la serie de Kyverum que lee el sello del FUR no se toca");
        ddl.Should().NotContain("DROP ").And.NotContain("UPDATE ").And.NotContain("CREATE TABLE").And.NotContain("CONSTRAINT");
    }

    [Fact]
    public void ElModeloEfMapeaLasColumnasComoNullables()
    {
        using var db = new FlitDbContext(
            new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(nameof(CartaIdentidadDdlParityTests)).Options);
        var entity = db.Model.FindEntityType(typeof(ProcedureInstanceBiometricValidation))!;

        foreach (var (propiedad, columna) in Columnas)
        {
            var p = entity.FindProperty(propiedad);
            p.Should().NotBeNull(propiedad);
            p!.GetColumnName().Should().Be(columna);
            p!.IsNullable.Should().BeTrue($"{columna} es NULL");
        }

        entity.FindProperty(nameof(ProcedureInstanceBiometricValidation.CertificateHash))!.GetColumnName()
            .Should().Be("certificate_hash", "la serie de Kyverum sigue en su columna");
    }

    [Fact]
    public void LaMigracionSeDescubreSinDesignerPorSusAtributosInline()
    {
        var tipo = typeof(FlitDbContext).Assembly.GetTypes()
            .Single(t => t.GetCustomAttribute<MigrationAttribute>()?.Id == MigrationId);

        tipo.GetCustomAttribute<DbContextAttribute>()!.ContextType.Should().Be<FlitDbContext>();
        tipo.IsSubclassOf(typeof(Migration)).Should().BeTrue();
    }

    [Fact]
    public void ElNumeroDelDdlNoChocaConOtroScript()
    {
        var recursos = typeof(FlitDbContext).Assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Sql.Ddl.169-", StringComparison.Ordinal))
            .ToList();

        recursos.Should().ContainSingle().Which.Should().EndWith(Script);
    }
}
