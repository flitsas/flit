using Flit.Infrastructure.Notifications.Tramites;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

/// <summary>HU #11486 — proyección instancia + field_values (AC1, AC2).</summary>
public sealed class PlateAssignmentEmailModelProjectorTests
{
    [Fact]
    public void FieldValuesCompletos_ProyectaPlacaCiudadYSecretaria()
    {
        var instance = new ProcedureInstance
        {
            Plate = "ABC123",
            PlateFlowStatus = "asignado",
            TransitOfficeId = Guid.NewGuid(),
        };
        var actors = new List<ProcedureInstanceActor>
        {
            new() { ActorType = "comprador", FullName = "  Comprador Test  " },
        };
        var fields = new Dictionary<string, string?>
        {
            ["transit_office_name"] = " Secretaría de Movilidad ",
            ["transit_office_city"] = "Medellín",
        };

        var model = PlateAssignmentEmailModelProjector.Project(instance, actors, fields);

        model.Placa.Should().Be("ABC123");
        model.ClienteNombre.Should().Be("Comprador Test");
        model.Ciudad.Should().Be("Medellín");
        model.SecretariaTransito.Should().Be("Secretaría de Movilidad");
        model.EstadoActual.Should().Be("asignado");
    }

    [Fact]
    public void FieldValuesIncompletos_NoLanzaYUsaDefaults()
    {
        var instance = new ProcedureInstance { Plate = null, PlateFlowStatus = null };
        var model = PlateAssignmentEmailModelProjector.Project(instance, [], new Dictionary<string, string?>());

        model.Placa.Should().BeEmpty();
        model.ClienteNombre.Should().BeEmpty();
        model.Ciudad.Should().BeEmpty();
        model.SecretariaTransito.Should().BeEmpty();
        model.EstadoActual.Should().Be(PlateAssignmentEmailModelProjector.DefaultEstadoAsignado);
    }

    [Fact]
    public void CodigoDivipola_NoSeImprimeComoCiudad()
    {
        var instance = new ProcedureInstance { Plate = "XYZ99" };
        var fields = new Dictionary<string, string?> { ["transit_office_city"] = "25286" };

        var model = PlateAssignmentEmailModelProjector.Project(instance, [], fields);

        model.Ciudad.Should().BeEmpty();
        model.Placa.Should().Be("XYZ99");
    }
}
