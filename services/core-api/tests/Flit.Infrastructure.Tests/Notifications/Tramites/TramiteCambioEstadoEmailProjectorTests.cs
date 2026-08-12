using Flit.Infrastructure.Notifications.Tramites;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

/// <summary>HU #11463 — proyección persistido → modelo de correo.</summary>
public sealed class TramiteCambioEstadoEmailProjectorTests
{
    [Fact]
    public void TramiteSinPlaca_NoRompeLaProyeccion()
    {
        var instance = new ProcedureInstance
        {
            ModalidadEntrada = "matricula_inicial",
            Plate = null,
        };
        var actors = new List<ProcedureInstanceActor>
        {
            new()
            {
                ActorType = "comprador",
                FullName = "Comprador X",
            },
        };
        var fields = new Dictionary<string, string?>
        {
            ["transit_office_name"] = "OT Funza",
            ["transit_office_city"] = "FUNZA",
        };

        var model = TramiteCambioEstadoEmailProjector.Project(instance, actors, fields, "APROBADO");

        model.Placa.Should().BeEmpty();
        model.CompradorNombre.Should().Be("Comprador X");
        model.CiudadOt.Should().Be("FUNZA");
        model.NombreOt.Should().Be("OT Funza");
        model.EsTraspaso.Should().BeFalse();
    }

    [Fact]
    public void CodigoDivipola_NoSeImprimeComoCiudad()
    {
        var instance = new ProcedureInstance { ModalidadEntrada = "traspaso", Plate = "ABC123" };
        var fields = new Dictionary<string, string?>
        {
            ["transit_office_city"] = "25286",
            ["transit_office_name"] = "OT",
        };

        var model = TramiteCambioEstadoEmailProjector.Project(instance, [], fields, "RECHAZADO");

        model.CiudadOt.Should().BeEmpty();
        model.EsTraspaso.Should().BeTrue();
        model.Placa.Should().Be("ABC123");
    }
}
