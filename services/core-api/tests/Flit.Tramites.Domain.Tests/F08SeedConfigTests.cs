using System.Text.RegularExpressions;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// FEATURE-08 / HU-BE-07 (CFD-10) — valida que el <c>gate_profile</c> de los tipos canónicos
/// sembrados (38-F08-seeds-tipos-configurados.sql) es JSON válido y expone los flags esperados.
/// </summary>
public sealed class F08SeedConfigTests
{
    private static Dictionary<string, ProcedureTypeGateProfile> ParseSeedProfiles()
    {
        var path = LocateSeedFile();
        var sql = File.ReadAllText(path);
        var result = new Dictionary<string, ProcedureTypeGateProfile>(StringComparer.Ordinal);

        // UPDATE ... WHERE code = 'CODE' con gate_profile = '{...}'::jsonb
        var updateRx = new Regex(
            @"gate_profile\s*=\s*'(\{[^']*\})'::jsonb[\s\S]*?WHERE code = '(MATRICULA_NUEVA|TRASPASO_STANDARD)'",
            RegexOptions.Compiled);

        foreach (Match m in updateRx.Matches(sql))
            result[m.Groups[2].Value] = ProcedureTypeGateProfile.FromJson(m.Groups[1].Value);

        // INSERT VALUES (..., 'CODE', 'name', 'family', 1, '{...}'::jsonb, ...)
        var insertRx = new Regex(
            @"'(PRENDA_INSCRIPCION|CAMBIO_LOCATARIO)',\s*'[^']*',\s*'[^']*',\s*1,\s*'(\{[^']*\})'::jsonb",
            RegexOptions.Compiled);

        foreach (Match m in insertRx.Matches(sql))
            result[m.Groups[1].Value] = ProcedureTypeGateProfile.FromJson(m.Groups[2].Value);

        return result;
    }

    private static string LocateSeedFile()
    {
        const string rel = "src/Flit.Infrastructure/Persistence/Sql/Ddl/38-F08-seeds-tipos-configurados.sql";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"No se encontró el seed F08 subiendo desde {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Seed_ContainsTheFourReferenceTypes()
    {
        var profiles = ParseSeedProfiles();
        profiles.Keys.Should().BeEquivalentTo(
            "MATRICULA_NUEVA", "TRASPASO_STANDARD", "PRENDA_INSCRIPCION", "CAMBIO_LOCATARIO");
    }

    [Fact]
    public void Seed_MatriculaNueva_HasExpectedFlags()
    {
        var p = ParseSeedProfiles()["MATRICULA_NUEVA"];
        p.EntryMode.Should().Be("VIN");
        p.RequiresBiometrics.Should().BeTrue();
        p.BiometricActors.Should().Contain("BUYER");
        p.RequiresSignature.Should().BeTrue();
        p.RequiresPlateRequest.Should().BeTrue();
    }

    [Fact]
    public void Seed_TraspasoStandard_HasExpectedFlags()
    {
        var p = ParseSeedProfiles()["TRASPASO_STANDARD"];
        p.EntryMode.Should().Be("PLATE");
        p.RequiresCommercialValue.Should().BeTrue();
        p.CommercialValueSource.Should().Be("FASECOLDA");
        p.BiometricActors.Should().Contain(new[] { "OWNER", "BUYER" });
        p.SimitMode.Should().Be("INTERNAL");
    }

    [Fact]
    public void Seed_PrendaInscripcion_HasPrendaGate()
    {
        var p = ParseSeedProfiles()["PRENDA_INSCRIPCION"];
        p.EntryMode.Should().Be("PLATE");
        p.HasPrendaGate.Should().BeTrue();
    }

    [Fact]
    public void Seed_CambioLocatario_IsPlateWithSignature()
    {
        var p = ParseSeedProfiles()["CAMBIO_LOCATARIO"];
        p.EntryMode.Should().Be("PLATE");
        p.RequiresSignature.Should().BeTrue();
    }
}
