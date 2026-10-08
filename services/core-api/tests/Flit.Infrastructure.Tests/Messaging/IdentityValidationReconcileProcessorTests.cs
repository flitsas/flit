using Flit.Infrastructure.Messaging;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>
/// HU #13351 — el presupuesto de sondeos de la conciliación solo se gasta cuando Kyverum respondió. Visto en la prueba de
/// falla en vivo: con Consultas caído, cada sondeo fallido sumaba y al volver el servicio la validación ya resuelta en
/// Kyverum quedaba «en proceso» sin que nadie volviera a preguntar.
/// </summary>
public sealed class IdentityValidationReconcileProcessorTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static ProcedureInstanceBiometricValidation Validacion(string status = BiometricEstados.EnProceso) => new()
    {
        Status = status,
        ReconcilePollCount = 1,
        UpdatedAt = Ahora.AddMinutes(-10),
    };

    [Fact]
    public void ConsultasOKyverumSinResponder_NoGastaPresupuesto_PeroEspaciaElSiguienteSondeo()
    {
        var v = Validacion();

        IdentityValidationReconcileProcessor.EstamparSondeo(v, Ahora, sinRespuesta: true);

        v.ReconcilePollCount.Should().Be(1);
        v.UpdatedAt.Should().Be(Ahora, "la ventana de frescura sigue espaciando los intentos");
    }

    [Fact]
    public void KyverumRespondioQueSiguePendiente_GastaPresupuesto()
    {
        var v = Validacion();

        IdentityValidationReconcileProcessor.EstamparSondeo(v, Ahora, sinRespuesta: false);

        v.ReconcilePollCount.Should().Be(2);
        v.UpdatedAt.Should().Be(Ahora);
    }

    [Fact]
    public void ValidacionYaResuelta_NoSeToca()
    {
        var v = Validacion(BiometricEstados.Aprobado);

        IdentityValidationReconcileProcessor.EstamparSondeo(v, Ahora, sinRespuesta: false);

        v.ReconcilePollCount.Should().Be(1);
        v.UpdatedAt.Should().Be(Ahora.AddMinutes(-10));
    }
}
