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
        // Paridad flujo estático pre-F08: la placa NO es un paso del wizard de matrícula (se resuelve en
        // la entrega, Feature #10587), así que el tipo de referencia no la exige.
        p.RequiresPlateRequest.Should().BeFalse();
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

    /// <summary>
    /// Extrae, por tipo, la secuencia ordenada de <c>section_type</c> configurada en el seed. Cada
    /// paso tiene una sección, así que el orden de los INSERT de <c>procedure_sections</c> dentro del
    /// bloque del tipo = el orden de los pasos del wizard.
    /// </summary>
    private static Dictionary<string, List<string>> ParseSeedSectionFlows()
    {
        var sql = File.ReadAllText(LocateSeedFile());
        var types = new[] { "MATRICULA_INICIAL", "TRASPASO_SIMPLE", "PRENDA_INSCRIPCION", "CAMBIO_LOCATARIO" };
        var starts = types
            .Select(t => new { Type = t, Idx = sql.IndexOf("'" + t + "'", StringComparison.Ordinal) })
            .Where(x => x.Idx >= 0)
            .OrderBy(x => x.Idx)
            .ToList();

        var sectionRx = new Regex(@"'single',\s*'([a-z_]+)',\s*now\(\)", RegexOptions.Compiled);
        var flows = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < starts.Count; i++)
        {
            var from = starts[i].Idx;
            var to = i + 1 < starts.Count ? starts[i + 1].Idx : sql.Length;
            var slice = sql.Substring(from, to - from);
            flows[starts[i].Type] = sectionRx.Matches(slice).Select(m => m.Groups[1].Value).ToList();
        }
        return flows;
    }

    /// <summary>
    /// HU-BE-07 (paridad de pasos, feedback PO 2026-07-21): al activar el motor dinámico los tipos de
    /// referencia deben reproducir EXACTAMENTE el flujo de pasos que el trámite tenía antes de FEATURE-08
    /// (camino estático de <c>WizardStateQuery.BuildMatricula</c>/<c>BuildTraspaso</c>). Este test es la
    /// garantía mecánica: si alguien reconfigura un seed y desajusta el flujo, falla.
    /// </summary>
    [Fact]
    public void Seed_ReferenceFlows_MatchCanonicalStaticSteps()
    {
        var flows = ParseSeedSectionFlows();

        // Matrícula estática pre-F08 (5 pasos): consulta VIN · documentos · comprador · identidad · FUR.
        flows["MATRICULA_INICIAL"].Should().Equal(
            "vehicle_query", "document_checklist", "actor_form", "biometric", "signature_fur");

        // Traspaso estático pre-F08 (6 pasos): consulta · documentos · vendedor · comprador · comercial · FUR.
        flows["TRASPASO_SIMPLE"].Should().Equal(
            "vehicle_query", "document_checklist", "actor_form", "actor_form", "commercial", "signature_fur");

        // Prenda inscripción (trámite aparte): consulta · documentos · propietario+acreedor · prenda · FUR.
        flows["PRENDA_INSCRIPCION"].Should().Equal(
            "vehicle_query", "document_checklist", "actor_form", "prenda_decision", "signature_fur");

        // Cambio de locatario (trámite aparte): consulta · documentos · locatario · identidad · FUR.
        flows["CAMBIO_LOCATARIO"].Should().Equal(
            "vehicle_query", "document_checklist", "actor_form", "biometric", "signature_fur");
    }

    /// <summary>
    /// Invariante de UX (feedback PO): ningún tipo de referencia debe exceder los 6 pasos (traspaso es el
    /// máximo histórico); el resto se mantiene en 5 o menos. Cota dura para que el configurador no infle
    /// el proceso.
    /// </summary>
    [Fact]
    public void Seed_NoReferenceTypeExceedsSixSteps()
    {
        foreach (var (type, flow) in ParseSeedSectionFlows())
            flow.Count.Should().BeInRange(1, 6, "el tipo {0} no debe exceder 6 pasos", type);
    }
}
