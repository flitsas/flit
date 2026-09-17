using Flit.Tramites.Domain.RuntConfirmation;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.RuntConfirmation;

/// <summary>
/// Epic #12550 (HU #12648, ADR-0059 §Ruta Corta) — «ya matriculado» y organismo de la Ruta Corta se
/// leen del historial de solicitudes del RUNT. Los casos son las SIETE consultas por VIN reales del
/// 2026-09-17 (cinco vehículos con placa preasignada en FLIT 1 —Envigado, Palmira, La Calera— y dos ya
/// matriculados en Medellín): es lo que decide que la regla no dependa del estado del automotor.
/// </summary>
public sealed class RuntMatriculaPolicyTests
{
    private static RuntVehicleSnapshot Snap(string fixture) =>
        RuntVehicleSnapshotParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuntConfirmation", "Fixtures", fixture)));

    public static TheoryData<string, string> Preasignados => new()
    {
        { "kyverum-vin-asignado-1-9FBHJD209VM679113.json", "STRIA TTEyTTO ENVIGADO" },
        { "kyverum-vin-asignado-2-9FBHJD408VM722868.json", "STRIA TTEyTTO ENVIGADO" },
        { "kyverum-vin-asignado-3-LRWYGCFJ3TC856327.json", "STRIA TTOyTTE PALMIRA" },
        { "kyverum-vin-asignado-4-VF1RJF013VA541804.json", "STRIA TTOyTTE MCPAL LA CALERA/CUND" },
        { "kyverum-vin-asignado-5-VF1RJF016VA542638.json", "STRIA TTOyTTE MCPAL LA CALERA/CUND" },
    };

    [Theory]
    [MemberData(nameof(Preasignados))]
    public void PlacaPreasignada_NoEstaMatriculado_YElOrganismoSaleDeLaSolicitud(string fixture, string organismo)
    {
        var snap = Snap(fixture);

        // Lo que hace a este caso distinto: hay placa, el estado es REGISTRADO y el bloque del vehículo
        // NO trae organismo. Solo el historial dice ante quién está preasignada la placa.
        snap.Placa.Should().NotBeNullOrWhiteSpace();
        snap.EstadoAutomotor.Should().Be("REGISTRADO");
        snap.OrganismoTransito.Should().BeNullOrWhiteSpace();

        var previa = RuntMatriculaPolicy.EvaluarMatriculaPrevia(snap.Solicitudes);

        previa.Veredicto.Should().Be(MatriculaPreviaRuntVeredicto.NoMatriculado);
        previa.Organismo.Should().Be(organismo);
        previa.Fecha.Should().Be(new DateOnly(2026, 9, 16));
        RuntMatriculaPolicy.OrganismoDelVehiculo(snap.OrganismoTransito, snap.Solicitudes).Should().Be(organismo);
    }

    [Theory]
    [InlineData("kyverum-vin-matriculado-1-LRWYGCFJ3TC817964.json")]
    [InlineData("kyverum-vin-matriculado-2-LRW3E7FS0TC943019.json")]
    public void MatriculaInicialAutorizada_EstaMatriculado_AunqueTambienTengaPreasignacion(string fixture)
    {
        var snap = Snap(fixture);
        snap.EstadoAutomotor.Should().Be("ACTIVO");

        var previa = RuntMatriculaPolicy.EvaluarMatriculaPrevia(snap.Solicitudes);

        previa.Veredicto.Should().Be(MatriculaPreviaRuntVeredicto.Matriculado);
        previa.Organismo.Should().Be("STRIA DE TTOyTTE MEDELLIN");
        previa.Fecha.Should().Be(new DateOnly(2026, 9, 16));
        // Con organismo en el vehículo, ese manda sobre el de la solicitud.
        RuntMatriculaPolicy.OrganismoDelVehiculo(snap.OrganismoTransito, snap.Solicitudes)
            .Should().Be(snap.OrganismoTransito);
    }

    [Fact]
    public void SinHistorial_NoAfirmaNada()
    {
        RuntMatriculaPolicy.EvaluarMatriculaPrevia(null).Should().Be(MatriculaPreviaRunt.SinHistorial);
        RuntMatriculaPolicy.EvaluarMatriculaPrevia([]).Should().Be(MatriculaPreviaRunt.SinHistorial);
        // Solicitudes sin `tramitesRealizados` (captura de referencia de julio): historial ilegible.
        RuntMatriculaPolicy.EvaluarMatriculaPrevia([new RuntSolicitud("295945563", null, "AUTORIZADA", null, null)])
            .Should().Be(MatriculaPreviaRunt.SinHistorial);
        RuntMatriculaPolicy.OrganismoDelVehiculo("  ", []).Should().BeNull();
    }

    [Fact]
    public void MatriculaInicialRechazada_NoCuentaComoMatriculado()
    {
        var solicitudes = new[]
        {
            new RuntSolicitud("1", new DateOnly(2026, 9, 15), "APROBADA", "TRÁMITE PREASIGNACIÓN PLACA CONTINGENCIA, ", "OT A"),
            new RuntSolicitud("2", new DateOnly(2026, 9, 16), "RECHAZADA", "TRÁMITE MATRÍCULA INICIAL, ", "OT A"),
        };

        var previa = RuntMatriculaPolicy.EvaluarMatriculaPrevia(solicitudes);

        previa.Veredicto.Should().Be(MatriculaPreviaRuntVeredicto.NoMatriculado);
        previa.Organismo.Should().Be("OT A");
    }

    [Fact]
    public void MatriculaInicialAprobada_TambienCuenta_YMandaLaMasReciente()
    {
        var solicitudes = new[]
        {
            new RuntSolicitud("1", new DateOnly(2026, 9, 10), "APROBADA", "TRÁMITE MATRÍCULA INICIAL, ", "OT VIEJO"),
            new RuntSolicitud("2", new DateOnly(2026, 9, 16), "AUTORIZADA", "TRÁMITE CAMBIO COLOR, TRÁMITE MATRÍCULA INICIAL, ", "OT NUEVO"),
        };

        var previa = RuntMatriculaPolicy.EvaluarMatriculaPrevia(solicitudes);

        previa.Veredicto.Should().Be(MatriculaPreviaRuntVeredicto.Matriculado);
        previa.Organismo.Should().Be("OT NUEVO");
        previa.Fecha.Should().Be(new DateOnly(2026, 9, 16));
    }
}
