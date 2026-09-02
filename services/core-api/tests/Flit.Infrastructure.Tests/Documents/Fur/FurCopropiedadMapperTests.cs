using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

public sealed class FurCopropiedadMapperTests
{
    [Fact]
    public void Map_TwoNaturalBuyers_ConcatenatesNameColumns_AndStacksDocuments()
    {
        var data = SampleMatricula(
            new DocumentParte("comprador", "EUGENIA MARIA CARDENAS TORRES", "43623787", null, "CC",
                Address: "CL 1 1 1", City: "Medellín", Phone: "3122191449", Ordinal: 1, OwnershipPercentage: 50m),
            new DocumentParte("comprador", "JULIO MARIO FONNEGRA SUCERQUIA", "98624794", null, "CC",
                Ordinal: 2, OwnershipPercentage: 50m));

        var mapped = FurFieldMapper.Map(data);

        mapped["vehicle_owner_first_last_name"].Text.Should().Be("CARDENAS\nFONNEGRA");
        mapped["vehicle_owner_second_last_name"].Text.Should().Be("TORRES\nSUCERQUIA");
        mapped["vehicle_owner_name"].Text.Should().Be("EUGENIA MARIA\nJULIO MARIO");
        mapped["vehicle_owner_document_number"].Text.Should().Be("43623787\n98624794");
        mapped["vehicle_owner_first_last_name"].FontSizeDelta.Should().Be(-1.5);
        mapped["vehicle_owner_document_type_c"].Text.Should().Be("X");
        mapped["vehicle_owner_document_type_c"].CheckboxRepeat.Should().Be(2);
        mapped["vehicle_owner_address"].Text.Should().Be("CL 1 1 1");
        mapped["observations"].Text.Should().Contain("EUGENIA MARIA CARDENAS TORRES es el propietario del 50%.");
        mapped["observations"].Text.Should().Contain("JULIO MARIO FONNEGRA SUCERQUIA es el propietario del 50%.");
        mapped["observations"].Text.Should().Contain("Inscripción de prenda a favor de");
    }

    [Fact]
    public void Map_SingleBuyer_DoesNotChangeNameLayout()
    {
        var data = SampleMatricula(
            new DocumentParte("comprador", "JAIME EDUARDO BOLAÑOS SEVILLANO", "12915150", null, "CC"));

        var mapped = FurFieldMapper.Map(data);

        mapped["vehicle_owner_first_last_name"].Text.Should().Be("BOLAÑOS");
        mapped["vehicle_owner_second_last_name"].Text.Should().Be("SEVILLANO");
        mapped["vehicle_owner_name"].Text.Should().Be("JAIME EDUARDO");
        mapped["vehicle_owner_document_number"].Text.Should().Be("12915150");
        mapped["vehicle_owner_document_type_c"].CheckboxRepeat.Should().Be(1);
        mapped["observations"].Text.Should().NotContain("es el propietario del");
    }

    [Fact]
    public void Map_FourNaturalBuyers_JoinsFourTokens()
    {
        var data = SampleMatricula(
            Parte("ANA LUCIA PEREZ GOMEZ", "1", 1, 25m),
            Parte("CARLOS ANDRES RUIZ DIAZ", "2", 2, 25m),
            Parte("DIANA SOFIA MEJIA LARA", "3", 3, 25m),
            Parte("EDGAR IVAN SOTO RIOS", "4", 4, 25m));

        var mapped = FurFieldMapper.Map(data);
        mapped["vehicle_owner_first_last_name"].Text.Should().Be("PEREZ\nRUIZ\nMEJIA\nSOTO");
        mapped["vehicle_owner_document_number"].Text!.Split('\n').Should().HaveCount(4);
        mapped["vehicle_owner_document_type_c"].CheckboxRepeat.Should().Be(4);
        mapped["vehicle_owner_first_last_name"].FontSizeDelta.Should().Be(-3.2);
        mapped["vehicle_owner_signature"].SignatureStamps.Should().HaveCount(4);
    }

    private static DocumentParte Parte(string nombre, string doc, int ordinal, decimal pct) =>
        new("comprador", nombre, doc, null, "CC", Ordinal: ordinal, OwnershipPercentage: pct);

    private static FurDocumentData SampleMatricula(params DocumentParte[] partes) =>
        new(
            ProcedureInstanceId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ReferenceNumber: "FUR-COPROP",
            Modalidad: "matricula_inicial",
            TipologiaCodigo: "MATRICULA_NUEVA",
            Vehiculo: new VehiculoDatos("TESLA", "MODEL Y", "2026", "PLATA", "CUADRICICLO", "GASOLINA", "0", "VIN123", "QOV000", TipoServicio: "PARTICULAR"),
            Organismo: new OrganismoTransito("76520000", "PALMIRA", "PALMIRA"),
            Partes: partes,
            ValorVenta: null,
            Causal: null,
            SellosFirma: [],
            FechaTramite: new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
            Observaciones: FurPrendaObservation.Join(
                FurCopropiedadObservation.Compose(partes),
                FurPrendaObservation.Compose(Flit.Tramites.Domain.Tramites.ValueObjects.FurPrendaMarking.Constitucion, "PRESENTE FONDO DE EMPLEADOS", "890900608")),
            IdentidadValidada: true,
            PrendaMarking: Flit.Tramites.Domain.Tramites.ValueObjects.FurPrendaMarking.Constitucion,
            AcreedorPrenda: "PRESENTE FONDO DE EMPLEADOS",
            FieldToFill: "CUATRIMOTO");
}
