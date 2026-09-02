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
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
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
        var instance = new ProcedureInstance {
        ProcedureType = ProcedureTypeFixture.For("traspaso"), Plate = "ABC123" };
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

    [Fact]
    public void CodigoDivipola_UsaCiudadDelCompradorEnMetadata()
    {
        var instance = new ProcedureInstance {
        ProcedureType = ProcedureTypeFixture.For("matricula_inicial"), Plate = "ABC123" };
        var actors = new List<ProcedureInstanceActor>
        {
            new()
            {
                ActorType = "comprador",
                FullName = "Comprador X",
                Metadata = """{"Ciudad":"Bogotá"}""",
            },
        };
        var fields = new Dictionary<string, string?>
        {
            ["transit_office_city"] = "25286",
            ["transit_office_name"] = "OT",
        };

        var model = TramiteCambioEstadoEmailProjector.Project(instance, actors, fields, "APROBADO");

        model.CiudadOt.Should().Be("Bogotá");
        model.EsTraspaso.Should().BeFalse();
    }

    [Fact]
    public void MatriculaInicial_NoExponeVendedorEnModelo()
    {
        var instance = new ProcedureInstance {
        ProcedureType = ProcedureTypeFixture.For("matricula_inicial"), Plate = "XYZ99" };
        var actors = new List<ProcedureInstanceActor>
        {
            new() { ActorType = "comprador", FullName = "Comprador" },
            new() { ActorType = "vendedor", FullName = "No Debe Aparecer" },
        };

        var model = TramiteCambioEstadoEmailProjector.Project(
            instance, actors, new Dictionary<string, string?>(), "APROBADO");

        model.VendedorNombre.Should().BeEmpty();
        model.EsTraspaso.Should().BeFalse();
        model.NombreTipoTramite.Should().BeEmpty();
    }

    [Fact]
    public void NombreTipoTramite_SeCopiaTalCualDelCatalogo()
    {
        // ADR-0050 — la familia la deriva el tipo; `modalidad_entrada` ya no existe.
        var instance = new ProcedureInstance { ProcedureType = ProcedureTypeFixture.Traspaso, Plate = "ABC123" };

        var model = TramiteCambioEstadoEmailProjector.Project(
            instance,
            [],
            new Dictionary<string, string?>(),
            "APROBADO",
            nombreTipoTramite: "Matrícula inicial");

        model.NombreTipoTramite.Should().Be("Matrícula inicial");
        model.EsTraspaso.Should().BeTrue();
    }

    /// <summary>
    /// ADR-0051 — «¿hay parte vendedora que nombrar?» lo declara el tipo (`requiresSeller`), no su
    /// familia. En TRASPASO_UNILATERAL el propietario no pasa por el wizard, pero SÍ es parte del
    /// trámite y el correo tiene que nombrarlo.
    /// </summary>
    [Fact]
    public void TraspasoUnilateral_NombraAlPropietarioAunqueNoSeCapturePorFormulario()
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.TraspasoUnilateral,
            Plate = "ABC123",
        };
        var actors = new List<ProcedureInstanceActor>
        {
            new() { ActorType = "comprador", FullName = "Ana Locataria" },
            new() { ActorType = "vendedor", FullName = "Leasing S.A." },
        };

        var model = TramiteCambioEstadoEmailProjector.Project(
            instance, actors, new Dictionary<string, string?>(), "APROBADO");

        model.EsTraspaso.Should().BeTrue();
        model.VendedorNombre.Should().Be("Leasing S.A.");
        model.CompradorNombre.Should().Be("Ana Locataria");
    }

    [Fact]
    public void Rechazado_MapeaCausalesYObservacion()
    {
        var instance = new ProcedureInstance {
        ProcedureType = ProcedureTypeFixture.For("traspaso"), Plate = "ABC123" };
        var causales = new[] { "Documentos ilegibles", "Improntas no coinciden" };

        var model = TramiteCambioEstadoEmailProjector.Project(
            instance,
            [],
            new Dictionary<string, string?>(),
            "RECHAZADO",
            causales,
            "Adjuntar SOAT vigente.");

        model.CausalesRechazo.Should().Equal(causales);
        model.ObservacionRechazo.Should().Be("Adjuntar SOAT vigente.");
        model.EstadoActual.Should().Be("RECHAZADO");
    }
}
