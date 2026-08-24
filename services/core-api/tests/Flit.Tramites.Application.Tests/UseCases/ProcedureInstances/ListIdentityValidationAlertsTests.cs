using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Alertas y recordatorios de validación de identidad (HU #10873): AC1 clasifica cada validación en
/// rechazada|expirada|por_vencer|atascada; AC2 marca cuáles ameritan recordatorio de reenvío
/// (pendiente|por_vencer). Entrega POR PULL — sin push/notificación in-app (decisión de alcance).
/// </summary>
public sealed class ListIdentityValidationAlertsTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IIdentityValidationOutboxRepository _outboxRepo =
        Substitute.For<IIdentityValidationOutboxRepository>();

    private ListIdentityValidationAlertsHandler Handler() => new(_repo, _outboxRepo);

    private static ProcedureInstanceBiometricValidation Val(
        Guid tenant,
        string estado,
        Guid? id = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? validatedAt = null,
        DateTimeOffset? validUntil = null,
        Guid? createdByUserId = null,
        string reference = "TRM-2026-000001") =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = Guid.NewGuid(),
            PartyRole = "comprador",
            Name = "Ana",
            DocumentType = "CC",
            DocumentNumber = "123456",
            Email = "ana@x.com",
            Status = estado,
            TokenHash = "h",
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
            ValidatedAt = validatedAt,
            ValidUntil = validUntil,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcedureInstance = new ProcedureInstance
            {
                ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ReferenceNumber = reference,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedByUserId = createdByUserId ?? Guid.NewGuid(),
            },
        };

    private void NoStuck(Guid tenant, CancellationToken ct) =>
        _outboxRepo.ListStuckAsync(tenant, Arg.Any<int>(), ct)
            .Returns(new List<StuckIdentityValidationRow>());

    // ── AC1: alerta por estado accionable ───────────────────────────────────────

    [Fact]
    public async Task Rechazada_genera_alerta_sin_recordatorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var v = Val(tenant, BiometricEstados.Rechazado);
        _repo.ListBiometricValidationsByTenantAsync(tenant, 0, ListIdentityValidationAlertsHandler.MaxRows, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation> { v });
        NoStuck(tenant, ct);

        var result = await Handler().HandleTenantAsync(tenant, ct);

        result.Total.Should().Be(1);
        var dto = result.Alerts[0];
        dto.Id.Should().Be(v.Id);
        dto.RecipientUserId.Should().Be(v.ProcedureInstance!.CreatedByUserId);
        dto.AlertKind.Should().Be(IdentityValidationAlertKinds.Rechazada);
        dto.RequiresResendReminder.Should().BeFalse();
    }

    [Fact]
    public async Task Expirada_por_status_genera_alerta_sin_recordatorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var v = Val(tenant, BiometricEstados.Expirado);
        _repo.ListBiometricValidationsByTenantAsync(tenant, 0, ListIdentityValidationAlertsHandler.MaxRows, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation> { v });
        NoStuck(tenant, ct);

        var result = await Handler().HandleTenantAsync(tenant, ct);

        result.Alerts.Should().ContainSingle();
        result.Alerts[0].AlertKind.Should().Be(IdentityValidationAlertKinds.Expirada);
        result.Alerts[0].RequiresResendReminder.Should().BeFalse();
    }

    [Fact]
    public async Task No_terminal_con_expiresAt_vencido_se_clasifica_expirada()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var v = Val(tenant, BiometricEstados.Enviado, expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        _repo.ListBiometricValidationsByTenantAsync(tenant, 0, ListIdentityValidationAlertsHandler.MaxRows, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation> { v });
        NoStuck(tenant, ct);

        var result = await Handler().HandleTenantAsync(tenant, ct);

        result.Alerts.Should().ContainSingle();
        result.Alerts[0].AlertKind.Should().Be(IdentityValidationAlertKinds.Expirada);
        result.Alerts[0].RequiresResendReminder.Should().BeFalse();
    }

    [Fact]
    public async Task Aprobada_vigente_por_vencer_genera_alerta_y_recordatorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        // Aprobada hace 25 días → quedan ~5 días de vigencia (≤ VigenciaPorVencerDias = 7).
        var validatedAt = now.AddDays(-25);
        var v = Val(tenant, BiometricEstados.Aprobado, validatedAt: validatedAt);
        _repo.ListBiometricValidationsByTenantAsync(tenant, 0, ListIdentityValidationAlertsHandler.MaxRows, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation> { v });
        NoStuck(tenant, ct);

        var result = await Handler().HandleTenantAsync(tenant, ct);

        result.Alerts.Should().ContainSingle();
        var dto = result.Alerts[0];
        dto.AlertKind.Should().Be(IdentityValidationAlertKinds.PorVencer);
        dto.RequiresResendReminder.Should().BeTrue();
        dto.DaysRemainingVigencia.Should().BeInRange(1, BiometricRules.VigenciaPorVencerDias);
    }

    [Fact]
    public async Task Aprobada_vigencia_agotada_se_clasifica_expirada_sin_recordatorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        // Aprobada hace 40 días (> VigenciaDias = 30) → ya no vigente.
        var validatedAt = now.AddDays(-40);
        var v = Val(tenant, BiometricEstados.Aprobado, validatedAt: validatedAt);
        _repo.ListBiometricValidationsByTenantAsync(tenant, 0, ListIdentityValidationAlertsHandler.MaxRows, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation> { v });
        NoStuck(tenant, ct);

        var result = await Handler().HandleTenantAsync(tenant, ct);

        result.Alerts.Should().ContainSingle();
        result.Alerts[0].AlertKind.Should().Be(IdentityValidationAlertKinds.Expirada);
        result.Alerts[0].RequiresResendReminder.Should().BeFalse();
    }

    [Fact]
    public async Task Aprobada_vigente_dentro_del_rango_no_genera_alerta_ni_recordatorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        // Aprobada hoy → muy lejos de vencer, no debe entrar a la respuesta.
        var v = Val(tenant, BiometricEstados.Aprobado, validatedAt: now);
        _repo.ListBiometricValidationsByTenantAsync(tenant, 0, ListIdentityValidationAlertsHandler.MaxRows, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation> { v });
        NoStuck(tenant, ct);

        var result = await Handler().HandleTenantAsync(tenant, ct);

        result.Total.Should().Be(0);
        result.Alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Atascada_prevalece_sobre_cualquier_otro_estado_y_no_genera_recordatorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var id = Guid.NewGuid();
        var v = Val(tenant, BiometricEstados.ErrorEnvio, id: id);
        _repo.ListBiometricValidationsByTenantAsync(tenant, 0, ListIdentityValidationAlertsHandler.MaxRows, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation> { v });
        _outboxRepo.ListStuckAsync(tenant, Arg.Any<int>(), ct)
            .Returns(new List<StuckIdentityValidationRow>
            {
                new(Guid.NewGuid(), id, IdentityValidationEventTypes.Completed, IdentityValidationOutbox.MaxDeliveryAttempts,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Ana", "CC", "123456", StuckIdentityValidationKinds.Envio),
            });

        var result = await Handler().HandleTenantAsync(tenant, ct);

        result.Alerts.Should().ContainSingle();
        result.Alerts[0].AlertKind.Should().Be(IdentityValidationAlertKinds.Atascada);
        result.Alerts[0].RequiresResendReminder.Should().BeFalse();
    }

    // ── AC2: recordatorio de reenvío (pendiente | por_vencer) ───────────────────

    [Theory]
    [InlineData(BiometricEstados.PendienteEnvio)]
    [InlineData(BiometricEstados.Enviado)]
    [InlineData(BiometricEstados.EnProceso)]
    public async Task Pendiente_no_vencida_genera_recordatorio_sin_alerta(string estado)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var v = Val(tenant, estado, expiresAt: DateTimeOffset.UtcNow.AddHours(2));
        _repo.ListBiometricValidationsByTenantAsync(tenant, 0, ListIdentityValidationAlertsHandler.MaxRows, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation> { v });
        NoStuck(tenant, ct);

        var result = await Handler().HandleTenantAsync(tenant, ct);

        result.Alerts.Should().ContainSingle();
        var dto = result.Alerts[0];
        dto.AlertKind.Should().BeNull();
        dto.RequiresResendReminder.Should().BeTrue();
    }

    // ── Vista por instancia ──────────────────────────────────────────────────────

    [Fact]
    public async Task HandleInstance_devuelve_not_found_si_la_instancia_no_existe()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var id = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns((ProcedureInstance?)null);

        var (result, error) = await Handler().HandleInstanceAsync(id, tenant, ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    [Fact]
    public async Task HandleInstance_clasifica_solo_las_validaciones_de_esa_instancia()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var id = Guid.NewGuid();
        var rejected = Val(tenant, BiometricEstados.Rechazado);
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = tenant,
            ReferenceNumber = "TRM-2026-000099",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid(),
        };
        rejected.ProcedureInstanceId = id;
        rejected.ProcedureInstance = instance;
        instance.BiometricValidations.Add(rejected);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        NoStuck(tenant, ct);

        var (result, error) = await Handler().HandleInstanceAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Alerts.Should().ContainSingle();
        result.Alerts[0].InstanceId.Should().Be(id);
        result.Alerts[0].AlertKind.Should().Be(IdentityValidationAlertKinds.Rechazada);
    }
}
