using System.Text.RegularExpressions;
using Flit.Tramites.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// ADR-0050 / CFD-09 — el seed <c>81-parametrizacion-tipos-operativos.sql</c> debe describir los
/// mismos pasos que emite el camino estático (<c>WizardStateQuery.StepKey</c>). Si los códigos o el
/// orden divergen, el motor dinámico cambiaría las claves de paso bajo los pies del frontend, que
/// hace <c>switch</c> sobre ellas.
/// <para>Se valida parseando el SQL, igual que <see cref="F08SeedConfigTests"/>: el DDL vive como
/// recurso embebido y no hay base de datos en la suite unitaria.</para>
/// </summary>
public sealed class ParametrizacionTiposOperativosSeedTests
{
    private sealed record SeedRow(string TypeCode, string StepCode, int StepOrder, string SectionType, int SectionOrder);

    private static List<SeedRow> ParseSeed()
    {
        var sql = File.ReadAllText(LocateSeedFile());

        // ('TYPE', 'step', 'Título', N, 'SECCION', 'Título', 'section_type', N)
        var rx = new Regex(
            @"\('(?<type>[A-Z_]+)',\s*'(?<step>[a-z_]+)',\s*'[^']*',\s*(?<sorder>\d+),\s*'[A-Z_]+',\s*'[^']*',\s*'(?<stype>[a-z_]+)',\s*(?<secorder>\d+)\)",
            RegexOptions.Compiled);

        return [.. rx.Matches(sql).Select(m => new SeedRow(
            m.Groups["type"].Value,
            m.Groups["step"].Value,
            int.Parse(m.Groups["sorder"].Value),
            m.Groups["stype"].Value,
            int.Parse(m.Groups["secorder"].Value)))];
    }

    private static string LocateSeedFile()
    {
        const string rel = "src/Flit.Infrastructure/Persistence/Sql/Ddl/81-parametrizacion-tipos-operativos.sql";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"No se encontró {rel} subiendo desde {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Matricula_TieneLosCincoPasosDelContratoEnOrden()
    {
        var pasos = ParseSeed()
            .Where(r => r.TypeCode == "MATRICULA_NUEVA")
            .OrderBy(r => r.StepOrder)
            .ToList();

        // Paridad con WizardStateQuery.StepKey(traspaso: false, ...).
        pasos.Select(p => p.StepCode).Should().ContainInOrder(
            "consulta_vin", "comprador", "documentos", "identidad", "fur");
        pasos.Select(p => p.SectionType).Should().ContainInOrder(
            ProcedureSectionTypes.VehicleQuery,
            ProcedureSectionTypes.ActorForm,
            ProcedureSectionTypes.DocumentChecklist,
            ProcedureSectionTypes.Biometric,
            ProcedureSectionTypes.SignatureFur);
    }

    [Fact]
    public void Traspaso_TieneLosSeisPasosDelContratoEnOrden()
    {
        var pasos = ParseSeed()
            .Where(r => r.TypeCode == "TRASPASO_STANDARD")
            .OrderBy(r => r.StepOrder).ThenBy(r => r.SectionOrder)
            .ToList();

        // Paridad con WizardStateQuery.StepKey(traspaso: true, ...) — 6 pasos, 7 secciones.
        pasos.Select(p => p.StepCode).Distinct().Should().ContainInOrder(
            "consulta", "vendedor", "comprador", "documentos", "identidad", "fur");
    }

    [Fact]
    public void Traspaso_ElPasoDeDocumentosLlevaChecklistYComercial()
    {
        // Los datos comerciales se absorbieron en Documentos (paridad de pasos 2026-08). Es el caso
        // multi-sección: antes solo sobrevivía sectionTypes[0] y el gate comercial no se evaluaba.
        var documentos = ParseSeed()
            .Where(r => r.TypeCode == "TRASPASO_STANDARD" && r.StepCode == "documentos")
            .OrderBy(r => r.SectionOrder)
            .ToList();

        documentos.Should().HaveCount(2);
        documentos.Select(d => d.SectionType).Should().ContainInOrder(
            ProcedureSectionTypes.DocumentChecklist, ProcedureSectionTypes.Commercial);
    }

    [Fact]
    public void TodosLosSectionTypeSembradosPertenecenAlCatalogoCerrado()
    {
        // El CHECK del DDL lo rechazaría en el arranque; aquí falla en CI, que es mucho más barato.
        var invalidos = ParseSeed()
            .Select(r => r.SectionType)
            .Where(t => !ProcedureSectionTypes.IsValid(t))
            .Distinct()
            .ToList();

        invalidos.Should().BeEmpty();
    }

    [Fact]
    public void ElSeedNoQuedaVacio()
    {
        // Guarda contra un cambio de formato del SQL que dejara la regex sin capturar nada y volviera
        // vacuos todos los tests de arriba.
        ParseSeed().Should().HaveCount(12);
    }
}
