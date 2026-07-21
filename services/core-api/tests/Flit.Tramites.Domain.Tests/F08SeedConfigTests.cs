using System.Text.RegularExpressions;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// FEATURE-08 / HU-BE-07 (CFD-10) — valida que el <c>gate_profile</c> de los 4 tipos de referencia
/// sembrados (38-F08-seeds-tipos-configurados.sql) es JSON válido y expone los flags esperados. La
/// aplicación del seed y el E2E se validan en DEV; este test cubre la corrección de la configuración.
/// </summary>
public sealed class F08SeedConfigTests
{
    private static Dictionary<string, ProcedureTypeGateProfile> ParseSeedProfiles()
    {
        var path = LocateSeedFile();
        var sql = File.ReadAllText(path);
        // VALUES (uuidv7(), 'CODE', 'name', 'family', 1, '{...}'::jsonb, ...
        var rx = new Regex(
            @"'(MATRICULA_INICIAL|TRASPASO_SIMPLE|PRENDA_INSCRIPCION|CAMBIO_LOCATARIO)',\s*'[^']*',\s*'[^']*',\s*1,\s*'(\{[^']*\})'::jsonb",
            RegexOptions.Compiled);

        var result = new Dictionary<string, ProcedureTypeGateProfile>(StringComparer.Ordinal);
        foreach (Match m in rx.Matches(sql))
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
            "MATRICULA_INICIAL", "TRASPASO_SIMPLE", "PRENDA_INSCRIPCION", "CAMBIO_LOCATARIO");
    }

    [Fact]
    public void Seed_MatriculaInicial_HasExpectedFlags()
    {
        var p = ParseSeedProfiles()["MATRICULA_INICIAL"];
        p.EntryMode.Should().Be("VIN");
        p.RequiresBiometrics.Should().BeTrue();
        p.BiometricActors.Should().Contain("BUYER");
        p.RequiresSignature.Should().BeTrue();
        p.RequiresPlateRequest.Should().BeTrue();
    }

    [Fact]
    public void Seed_TraspasoSimple_HasExpectedFlags()
    {
        var p = ParseSeedProfiles()["TRASPASO_SIMPLE"];
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
