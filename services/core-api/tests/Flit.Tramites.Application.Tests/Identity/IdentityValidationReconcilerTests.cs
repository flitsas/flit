using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// Regresión Bug #11503: Kyverum permite varios intentos DENTRO de una misma validación y reporta
/// rechazo tras CADA intento fallido. <see cref="IdentityValidationReconciler.ApplyStatusAsync"/> solo debe
/// terminalizar en <c>rechazado</c> cuando el conteo local de intentos (autoritativo por webhook) se agotó
/// (<c>Attempts &gt;= MaxAttempts</c>) — o cuando Kyverum reporta explícitamente el cierre (mapeado por
/// <c>KyverumVerifyClient</c> al status <c>rechazado</c> terminal). Un rechazo de intento intermedio
/// (<c>rechazado_intento</c>) NUNCA debe congelar la fila.
/// </summary>
public sealed class IdentityValidationReconcilerTests
{
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IdentityValidationResultApplier _applier;
    private readonly DateTimeOffset _now = new(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);

    public IdentityValidationReconcilerTests() => _applier = new IdentityValidationResultApplier(_events);

    /// <summary>
    /// <paramref name="procedureInstanceId"/> null ⇒ prevalidación STANDALONE (HU #10865). El alcance del
    /// Bug #11503 cubre ambas rutas porque el worker de reconciliación no filtra por instancia de trámite.
    /// </summary>
    private static ProcedureInstanceBiometricValidation Seed(
        int attempts, int maxAttempts, Guid? procedureInstanceId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = procedureInstanceId,
            PartyRole = procedureInstanceId is null ? null : "comprador",
            Status = BiometricEstados.EnProceso,
            Provider = BiometricProviders.Kyverum,
            KyverumVerificationId = "kyv-1",
            Name = "X",
            DocumentType = "CC",
            DocumentNumber = "1",
            Email = "x@y.com",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
            Attempts = attempts,
            MaxAttempts = maxAttempts,
        };

    [Theory]
    [InlineData(null)] // prevalidación standalone
    [InlineData("procedure")] // identidad de trámite
    public async Task RechazadoIntento_ConIntentosDisponibles_NoTerminaliza(string? procedureFlag)
    {
        var ct = TestContext.Current.CancellationToken;
        var procedureInstanceId = procedureFlag is null ? (Guid?)null : Guid.NewGuid();
        var v = Seed(attempts: 1, maxAttempts: 3, procedureInstanceId);
        var status = new KyverumVerifyStatus("rechazado_intento", 40, "{\"status\":\"rechazado_intento\"}", Motivo: "rostro no visible");

        var updated = await IdentityValidationReconciler.ApplyStatusAsync(_applier, v, status, _now, ct);

        // No terminaliza: sigue reconciliable (en_proceso), no queda "congelada" en rechazado.
        v.Status.Should().Be(BiometricEstados.EnProceso);
        v.ProviderStatus.Should().Be("rechazado_intento");
        await _events.DidNotReceive().PublishAsync(Arg.Any<IdentityValidationEvent>(), Arg.Any<CancellationToken>());

        // La misma fila, en un intento posterior, SÍ se aprueba (demuestra que no quedó congelada).
        var aprobado = new KyverumVerifyStatus("aprobado", 92, "{\"status\":\"aprobado\"}");
        var updatedAprobado = await IdentityValidationReconciler.ApplyStatusAsync(_applier, v, aprobado, _now.AddMinutes(5), ct);

        updatedAprobado.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ValidatedAt.Should().NotBeNull();
        v.ValidUntil.Should().NotBeNull();
        await _events.Received(1).PublishAsync(
            Arg.Is<IdentityValidationCompleted>(e => e.ValidationId == v.Id && e.Estado == BiometricEstados.Aprobado),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RechazadoIntento_ConIntentosAgotados_TerminalizaRechazado()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed(attempts: 3, maxAttempts: 3, procedureInstanceId: Guid.NewGuid());
        var status = new KyverumVerifyStatus("rechazado_intento", 35, "{\"status\":\"rechazado_intento\"}");

        var updated = await IdentityValidationReconciler.ApplyStatusAsync(_applier, v, status, _now, ct);

        updated.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Rechazado);
        await _events.Received(1).PublishAsync(
            Arg.Is<IdentityValidationCompleted>(e => e.ValidationId == v.Id && e.Estado == BiometricEstados.Rechazado),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rechazado_ConSenalDeCierre_PeroConIntentosDisponibles_NoTerminaliza()
    {
        // Kyverum trae result.closedAt también tras un intento intermedio: con 1 de 3 intentos el rechazo NO es
        // terminal; la fila sigue en_proceso y el aprobado del intento siguiente se aplica.
        var ct = TestContext.Current.CancellationToken;
        var v = Seed(attempts: 1, maxAttempts: 3, procedureInstanceId: Guid.NewGuid());
        var status = new KyverumVerifyStatus("rechazado", 20, "{\"status\":\"rechazado\"}");

        await IdentityValidationReconciler.ApplyStatusAsync(_applier, v, status, _now, ct);

        v.Status.Should().Be(BiometricEstados.EnProceso);
        await _events.DidNotReceive().PublishAsync(Arg.Any<IdentityValidationEvent>(), Arg.Any<CancellationToken>());

        var aprobado = new KyverumVerifyStatus("aprobado", 90, "{\"status\":\"aprobado\"}");
        (await IdentityValidationReconciler.ApplyStatusAsync(_applier, v, aprobado, _now.AddMinutes(3), ct))
            .Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Aprobado);
    }

    [Fact]
    public async Task Rechazado_ConSenalDeCierre_ConIntentosAgotados_Terminaliza()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed(attempts: 3, maxAttempts: 3, procedureInstanceId: Guid.NewGuid());
        var status = new KyverumVerifyStatus("rechazado", 20, "{\"status\":\"rechazado\"}");

        var updated = await IdentityValidationReconciler.ApplyStatusAsync(_applier, v, status, _now, ct);

        updated.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Rechazado);
    }

    [Theory]
    [InlineData(null)] // prevalidación standalone
    [InlineData("procedure")] // identidad de trámite (comprador)
    [InlineData("mandatario")] // validación propia del mandatario
    public async Task Aprobado_ReemplazaRechazoAplicadoAntesDeAgotarLosIntentos(string? rol)
    {
        // Fila que una versión anterior dejó en rechazado con 1 de 3 intentos: la persona aprobó en el segundo
        // intento del mismo enlace y ese aprobado debe aplicarse, con su evento.
        var ct = TestContext.Current.CancellationToken;
        var v = Seed(attempts: 1, maxAttempts: 3, procedureInstanceId: rol == "procedure" ? Guid.NewGuid() : null);
        if (rol == "mandatario")
        {
            v.PartyRole = BiometricRules.ParteMandatario;
            v.MandateSignerId = Guid.NewGuid();
        }
        v.Status = BiometricEstados.Rechazado;

        var aprobado = new KyverumVerifyStatus("aprobado", 91, "{\"status\":\"aprobado\"}", FirmaSerie: "SER-1");
        var updated = await IdentityValidationReconciler.ApplyStatusAsync(_applier, v, aprobado, _now, ct);

        updated.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ValidatedAt.Should().Be(_now);
        v.CertificateHash.Should().Be("SER-1");
        await _events.Received(1).PublishAsync(
            Arg.Is<IdentityValidationCompleted>(e => e.ValidationId == v.Id && e.Estado == BiometricEstados.Aprobado),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(3, BiometricProviders.Kyverum, null)] // intentos agotados: el rechazo es definitivo
    [InlineData(1, BiometricProviders.Manual, null)] // flujo manual: no lo decide Kyverum
    [InlineData(1, BiometricProviders.Kyverum, "documento_ilegible")] // rechazo de la revisión humana
    public async Task Aprobado_NoReemplazaUnRechazoDefinitivo(int attempts, string provider, string? motivoManual)
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed(attempts, maxAttempts: 3, procedureInstanceId: Guid.NewGuid());
        v.Status = BiometricEstados.Rechazado;
        v.Provider = provider;
        v.RejectionReasonCode = motivoManual;

        var aprobado = new KyverumVerifyStatus("aprobado", 91, "{\"status\":\"aprobado\"}");
        var updated = await IdentityValidationReconciler.ApplyStatusAsync(_applier, v, aprobado, _now, ct);

        updated.Should().BeFalse();
        v.Status.Should().Be(BiometricEstados.Rechazado);
        await _events.DidNotReceive().PublishAsync(Arg.Any<IdentityValidationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RechazoAplicadoAntesDeAgotarLosIntentos_UnNuevoRechazoNoLoCambia()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed(attempts: 1, maxAttempts: 3, procedureInstanceId: Guid.NewGuid());
        v.Status = BiometricEstados.Rechazado;

        var rechazo = new KyverumVerifyStatus("rechazado", 20, "{\"status\":\"rechazado\"}");
        (await IdentityValidationReconciler.ApplyStatusAsync(_applier, v, rechazo, _now, ct)).Should().BeFalse();

        v.Status.Should().Be(BiometricEstados.Rechazado);
        await _events.DidNotReceive().PublishAsync(Arg.Any<IdentityValidationEvent>(), Arg.Any<CancellationToken>());
    }
}
