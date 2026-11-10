using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

/// <summary>HU #14500 — actor PPT no marca casilla tipológica; el literal va en observaciones.</summary>
public sealed class FurPptCasillaTests
{
    [Fact]
    public void Map_ActorPpt_NoMarcaNingunaCasillaTipoDocumento_YObservacionesLlevanLiteral()
    {
        var data = new FurDocumentData(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "TRM-PPT-1",
            Modalidad: "MATRICULAS",
            TipologiaCodigo: "MATRICULA_NUEVA",
            Vehiculo: new VehiculoDatos("MAZDA", "3", "2020", "ROJO", "AUTOMOVIL", "GASOLINA", "2000", "VIN12345678901234", "ABC123"),
            Organismo: new OrganismoTransito("25286000", "OT DEMO", "Bogotá"),
            Partes:
            [
                new DocumentParte("comprador", "Ana PPT", "V12345678", "a@x.com", DocumentType: "PPT"),
            ],
            ValorVenta: null,
            Causal: null,
            SellosFirma: [],
            Observaciones: FurPptObservation.Literal);

        var mapped = FurFieldMapper.Map(data);

        foreach (var id in new[] { "c", "nit", "nn", "p", "ce", "ti", "nuip", "cd" })
        {
            mapped[$"vehicle_owner_document_type_{id}"].Text.Should().NotBe("X",
                because: $"PPT no debe marcar casilla {id}");
        }

        mapped["observations"].Text.Should().Contain(FurPptObservation.Literal);
    }
}
