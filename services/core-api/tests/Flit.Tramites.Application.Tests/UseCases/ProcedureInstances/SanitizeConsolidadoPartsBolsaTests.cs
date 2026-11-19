using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #14570 — los tipos «bolsa» (<c>AttachmentRules.TiposMultiples</c>) entran COMPLETOS al consolidado,
/// en orden de carga; el resto de tipos conserva la deduplicación de siempre.
/// </summary>
public sealed class SanitizeConsolidadoPartsBolsaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static ProcedureInstanceAttachment A(string tipo, string file, int minutos, string source = "user") => new()
    {
        Id = Guid.NewGuid(),
        Tipo = tipo,
        Filename = file,
        Mimetype = "application/pdf",
        Source = source,
        UploadedAt = T0.AddMinutes(minutos),
    };

    private static string[] Files(IReadOnlyList<ProcedureInstanceAttachment> r) => r.Select(x => x.Filename).ToArray();

    [Fact]
    public void VariosOtro_TodosIncluidosEnOrdenDeCarga()
    {
        // Entrada deliberadamente desordenada por fecha dentro del tipo.
        var r = GenerarConsolidadoHandler.SanitizeConsolidadoParts(
            [A("fur", "fur.pdf", 0), A("otro", "otro2.pdf", 5), A("otro", "otro1.pdf", 2), A("soat", "soat.pdf", 9)]);

        Files(r).Should().Equal("fur.pdf", "otro1.pdf", "otro2.pdf", "soat.pdf");
    }

    [Fact]
    public void AnexosGenerales_TodosIncluidos()
    {
        var r = GenerarConsolidadoHandler.SanitizeConsolidadoParts(
            [A("anexos_generales", "a1.pdf", 1), A("anexos_generales", "a2.pdf", 2), A("anexos_generales", "a3.pdf", 3)]);

        Files(r).Should().Equal("a1.pdf", "a2.pdf", "a3.pdf");
    }

    [Fact]
    public void TipoNormalDuplicado_SigueElMasReciente()
    {
        var r = GenerarConsolidadoHandler.SanitizeConsolidadoParts(
            [A("soat", "viejo.pdf", 1), A("soat", "nuevo.pdf", 7), A("otro", "o1.pdf", 2), A("otro", "o2.pdf", 3)]);

        Files(r).Should().Equal("nuevo.pdf", "o1.pdf", "o2.pdf");
    }

    [Fact]
    public void MandatoYTramiteVirtual_ConservanPrecedenciaDeclarada()
    {
        // Misma regla de siempre: gana la fuente por precedencia declarada (no se toca), un solo archivo por tipo.
        var r = GenerarConsolidadoHandler.SanitizeConsolidadoParts(
            [A("mandato", "m1.pdf", 1, "system"), A("mandato", "m2.pdf", 2, "system"),
             A("tramite_virtual", "t1.pdf", 3, "system"), A("tramite_virtual", "t2.pdf", 4, "system")]);

        r.Count(x => x.Tipo == "mandato").Should().Be(1);
        r.Count(x => x.Tipo == "tramite_virtual").Should().Be(1);
    }

    [Fact]
    public void Consolidados_SiguenExcluidos_AunqueHayaBolsa()
    {
        var r = GenerarConsolidadoHandler.SanitizeConsolidadoParts(
            [A("consolidado", "c.pdf", 1), A("consolidado_maestro", "cm.pdf", 2), A("otro", "o1.pdf", 3), A("otro", "o2.pdf", 4)]);

        Files(r).Should().Equal("o1.pdf", "o2.pdf");
    }

    [Fact]
    public void Bolsa_ComparaTipoSinDistinguirMayusculas()
    {
        var r = GenerarConsolidadoHandler.SanitizeConsolidadoParts(
            [A("Otro", "o1.pdf", 1), A("otro", "o2.pdf", 2)]);

        Files(r).Should().Equal("o1.pdf", "o2.pdf");
    }
}
