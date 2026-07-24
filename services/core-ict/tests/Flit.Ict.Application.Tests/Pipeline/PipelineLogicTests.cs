using Flit.Ict.Domain.Enums;
using Flit.Ict.Domain.Jobs;
using Flit.Ict.Domain.Validation;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Pipeline;

public sealed class ExternalSourceValidatorsTests
{
    private const int CurrentYear = 2026;

    [Fact]
    public void Soat_not_vigente_blocks()
    {
        var issues = ExternalSourceValidators.Validate(3, "VEHICLE", new ConsultationResult(SoatStatus: "VENCIDO", VehicleModelYear: 2024), CurrentYear);
        issues.Should().ContainSingle().Which.Should().Contain("SOAT");
    }

    [Fact]
    public void Rtm_expired_on_old_vehicle_blocks_except_unilateral_transfer()
    {
        var old = new ConsultationResult(SoatStatus: "VIGENTE", RtmStatus: "VENCIDO", VehicleModelYear: 2015);

        ExternalSourceValidators.Validate(3, "VEHICLE", old, CurrentYear).Should().Contain(i => i.Contains("RTM"));
        // Traspaso unilateral (tipo 4) solo advierte: no bloquea.
        ExternalSourceValidators.Validate(4, "VEHICLE", old, CurrentYear).Should().BeEmpty();
    }

    [Fact]
    public void Active_sanctions_block()
    {
        var issues = ExternalSourceValidators.Validate(3, "RNMC", new ConsultationResult(HasActiveSanctions: true), CurrentYear);
        issues.Should().Contain(i => i.Contains("sanciones"));
    }

    [Fact]
    public void All_valid_passes()
    {
        var ok = new ConsultationResult(SoatStatus: "VIGENTE", RtmStatus: "VIGENTE", VehicleModelYear: 2024, PazYSalvo: true);
        ExternalSourceValidators.Validate(3, "VEHICLE", ok, CurrentYear).Should().BeEmpty();
        ExternalSourceValidators.Warnings(ok).Should().BeEmpty();
    }

    [Fact]
    public void Paz_y_salvo_false_is_an_informative_warning_not_a_blocking_issue()
    {
        // DRIVER (paz y salvo del conductor): fiel a v1, es INFORMATIVO — no bloquea el paso a borrador.
        var result = new ConsultationResult(SoatStatus: "VIGENTE", RtmStatus: "VIGENTE", VehicleModelYear: 2024, PazYSalvo: false);
        ExternalSourceValidators.Validate(3, "DRIVER", result, CurrentYear).Should().BeEmpty();
        ExternalSourceValidators.Warnings(result).Should().ContainSingle().Which.Should().Contain("paz y salvo");
    }

    [Fact]
    public void Transfer_with_unfound_vehicle_blocks()
    {
        // Traspaso (3,4) + consulta 'VEHICLE' (por placa) sin datos del vehículo = no encontrado en RUNT → novedad.
        // Con fuentes reales una placa inexistente devuelve todo null; sin esta regla materializaba igual.
        var notFound = new ConsultationResult(); // SoatStatus null, VehicleModelYear null
        ExternalSourceValidators.Validate(3, "VEHICLE", notFound, CurrentYear)
            .Should().ContainSingle().Which.Should().Contain("RUNT");
        ExternalSourceValidators.Validate(4, "VEHICLE", notFound, CurrentYear)
            .Should().Contain(i => i.Contains("RUNT"));
    }

    [Fact]
    public void Registration_with_unfound_vehicle_does_not_block()
    {
        // Matrícula (1,2) consulta por 'VIN': el vehículo NUEVO aún no está en el RUNT → resultado vacío es válido.
        ExternalSourceValidators.Validate(1, "VIN", new ConsultationResult(), CurrentYear).Should().BeEmpty();
        ExternalSourceValidators.Validate(2, "VIN", new ConsultationResult(), CurrentYear).Should().BeEmpty();
    }

    [Fact]
    public void Transfer_non_vehicle_query_with_null_vehicle_does_not_falsely_block()
    {
        // Crítico: el orquestador valida CADA source_query por separado. Las consultas 'DRIVER'/'RNMC' de un
        // traspaso traen SoatStatus/VehicleModelYear en null, pero NO deben disparar "vehículo no encontrado"
        // (solo la consulta 'VEHICLE' lo hace). Prueba que el gate por query_type funciona.
        ExternalSourceValidators.Validate(3, "DRIVER", new ConsultationResult(PazYSalvo: true), CurrentYear).Should().BeEmpty();
        ExternalSourceValidators.Validate(3, "RNMC", new ConsultationResult(HasActiveSanctions: false), CurrentYear).Should().BeEmpty();
    }

    [Fact]
    public void Paz_y_salvo_true_or_unknown_has_no_warning()
    {
        ExternalSourceValidators.Warnings(new ConsultationResult(PazYSalvo: true)).Should().BeEmpty();
        ExternalSourceValidators.Warnings(new ConsultationResult(PazYSalvo: null)).Should().BeEmpty();
    }
}

public sealed class IctWindowEvaluatorTests
{
    [Theory]
    [InlineData("2026-07-22T14:00:00Z", true)]  // Bogotá 09:00 -> dentro
    [InlineData("2026-07-22T12:00:00Z", false)] // Bogotá 07:00 -> antes de las 08
    [InlineData("2026-07-23T02:00:00Z", false)] // Bogotá 21:00 -> después de las 20
    public void Window_08_20(string utc, bool expected)
    {
        var when = DateTime.Parse(utc, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal);
        IctWindowEvaluator.IsWithinWindow(when, 8, 20).Should().Be(expected);
    }
}

public sealed class IctEstadoMapTests
{
    [Fact]
    public void Maps_internal_status_to_v2_vocabulary()
    {
        IctEstado.Map(1, hasProcedureInstance: false, businessValidated: false, externalStarted: false)
            .Should().Be(IctEstado.Recibido);
        IctEstado.Map(2, false, businessValidated: false, false).Should().Be(IctEstado.EnValidacionNegocio);
        IctEstado.Map(2, false, businessValidated: true, false).Should().Be(IctEstado.EnValidacionExterna);
        IctEstado.Map(4, false, true, false).Should().Be(IctEstado.ConNovedades);
        IctEstado.Map(3, hasProcedureInstance: true, true, false).Should().Be(IctEstado.BorradorCreado);
    }
}
