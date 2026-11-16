using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>HU #14500 / ADR-0082 — literal PPT en observaciones (una vez por FUR).</summary>
public sealed class FurPptObservationTests
{
    [Fact]
    public void Compose_SinPpt_DevuelveNull()
    {
        var partes = new[]
        {
            new DocumentParte("comprador", "Ana", "123", "a@x.com", DocumentType: "CC"),
        };

        FurPptObservation.Compose(partes).Should().BeNull();
    }

    [Fact]
    public void Compose_UnActorPpt_EmiteLiteralExacto()
    {
        var partes = new[]
        {
            new DocumentParte("comprador", "Ana", "V12345678", "a@x.com", DocumentType: "PPT"),
        };

        FurPptObservation.Compose(partes).Should().Be(FurPptObservation.Literal);
        FurPptObservation.Literal.Should().Be("tipo de documento (PPT) Permiso por Protección Temporal");
    }

    [Fact]
    public void Compose_VariosActoresPpt_EmiteUnaSolaVez()
    {
        var partes = new[]
        {
            new DocumentParte("vendedor", "Pedro", "V111", "p@x.com", DocumentType: "PPT"),
            new DocumentParte("comprador", "Ana", "V222", "a@x.com", DocumentType: "PPT"),
        };

        FurPptObservation.Compose(partes).Should().Be(FurPptObservation.Literal);
    }

    [Fact]
    public void Compose_RepresentanteLegalPpt_EmiteLiteral()
    {
        var partes = new[]
        {
            new DocumentParte(
                "comprador",
                "EMPRESA SAS",
                "900123456",
                "e@x.com",
                DocumentType: "NIT",
                EsJuridica: true,
                RepresentanteLegalNombre: "RL PPT",
                RepresentanteLegalTipoDoc: "PPT",
                RepresentanteLegalDocumento: "V999"),
        };

        FurPptObservation.Compose(partes).Should().Be(FurPptObservation.Literal);
    }

    [Fact]
    public void Join_ConOtrosBloques_NoPisa()
    {
        var partes = new[]
        {
            new DocumentParte("comprador", "Ana", "V123", "a@x.com", DocumentType: "PPT"),
        };

        var joined = FurPrendaObservation.Join(
            "bloque tipo",
            FurPrendaObservation.Join(FurPptObservation.Compose(partes), "bloque prenda"));

        joined.Should().Be("bloque tipo, tipo de documento (PPT) Permiso por Protección Temporal, bloque prenda");
    }
}
