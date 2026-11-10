using System.Text;
using System.Text.Json;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// L4 — cuando <see cref="IdentityValidationReconciler.ApplyStatusAsync"/> aplica <c>expirado</c>, la bitácora del
/// trámite gana la frase «Identidad del {parte}: expirada» (mismo formato que aprobada/rechazada de HU #14179) y
/// NO se publica <see cref="IdentityValidationCompleted"/> (sus consumidores encadenan la firma). El reconciliador
/// es el punto que comparten el botón (reconcile manual), el respaldo del webhook y el proceso de fondo
/// (<c>IdentityValidationReconcileProcessor</c>), así que este test cubre las tres entradas; el webhook se prueba
/// además de punta a punta por <see cref="KyverumWebhookHandler"/>.
/// <para>Uso de ejemplo: <c>await IdentityValidationReconciler.ApplyStatusAsync(applier, v, new KyverumVerifyStatus("expirado", null, "{}"), now, ct);</c></para>
/// </summary>
public sealed class BitacoraIdentidadExpiradaTests
{
    private const string Secret = "whsec_abc";
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid InstanceId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly List<ProcedureInstanceEvent> _bitacora = [];

    public BitacoraIdentidadExpiradaTests()
    {
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(_bitacora.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private IdentityValidationResultApplier Applier() => new(_events, _repo);

    private static KyverumVerifyStatus Expirado() => new("expirado", null, "{\"status\":\"expirado\"}");

    private static ProcedureInstanceBiometricValidation Validacion(Guid? instanceId, string? parte = "vendedor") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ProcedureInstanceId = instanceId,
        PartyRole = parte,
        Status = BiometricEstados.EnProceso,
        Provider = BiometricProviders.Kyverum,
        KyverumVerificationId = "kyv_123",
        Name = "X",
        DocumentType = "CC",
        DocumentNumber = "123",
        Email = "x@y.com",
        TokenHash = "h",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        CreatedAt = DateTimeOffset.UtcNow,
        WebhookSecretEncrypted = new FakeWebhookSecretProtector().Protect(Secret),
    };

    [Fact]
    public async Task L4_ProcesoDeFondoYApplier_ExpiradoConTramite_DejaFraseSinEvento()
    {
        var v = Validacion(InstanceId);

        var updated = await IdentityValidationReconciler.ApplyStatusAsync(Applier(), v, Expirado(), Now, Ct);

        updated.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Expirado);
        var evt = _bitacora.Should().ContainSingle().Subject;
        evt.Tipo.Should().Be(ProcedureFieldChangeRecorder.EventType);
        evt.TenantId.Should().Be(TenantId);
        evt.ProcedureInstanceId.Should().Be(InstanceId);
        evt.CreatedAt.Should().Be(Now);
        evt.CreatedBy.Should().BeNull();
        using var doc = JsonDocument.Parse(evt.Payload);
        doc.RootElement.GetProperty("etiqueta").GetString().Should().Be("Validación de identidad");
        doc.RootElement.GetProperty("campo").GetString().Should().Be("identidad");
        doc.RootElement.GetProperty("anterior").GetString().Should().BeEmpty();
        doc.RootElement.GetProperty("nuevo").GetString().Should().Be("Identidad del vendedor: expirada");
        evt.Payload.Should().NotContain("status");
        await _events.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task L4_ExpiradoSinTramite_NoEscribeBitacoraNiEvento()
    {
        var v = Validacion(instanceId: null, parte: null);

        var updated = await IdentityValidationReconciler.ApplyStatusAsync(Applier(), v, Expirado(), Now, Ct);

        updated.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Expirado);
        await _repo.DidNotReceiveWithAnyArgs().AddEventAsync(default!, Ct);
        await _events.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task L4_ExpiradoAplicadoDosVeces_UnaSolaFrase()
    {
        var applier = Applier();
        var v = Validacion(InstanceId);

        (await IdentityValidationReconciler.ApplyStatusAsync(applier, v, Expirado(), Now, Ct)).Should().BeTrue();
        (await IdentityValidationReconciler.ApplyStatusAsync(applier, v, Expirado(), Now, Ct)).Should().BeFalse();

        _bitacora.Should().ContainSingle();
    }

    [Fact]
    public async Task L4_FraseYaPersistida_PorOtraEntrada_NoSeDuplica()
    {
        // Botón y proceso de fondo llegan a la vez: la frase que ya quedó en la bitácora no se repite.
        const string frase = "Identidad del vendedor: expirada";
        var yaRegistrada = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Validación de identidad", "identidad", "", frase, Now, null, null, null);
        _repo.GetModificacionesPageAsync(InstanceId, TenantId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[yaRegistrada], 1));

        var updated = await IdentityValidationReconciler.ApplyStatusAsync(Applier(), Validacion(InstanceId), Expirado(), Now, Ct);

        updated.Should().BeTrue();
        _bitacora.Should().BeEmpty();
    }

    [Fact]
    public async Task L4_TramiteAnulado_ValidacionCongelada_NoExpiraNiEscribe()
    {
        var v = Validacion(InstanceId);
        v.ProcedureInstance = new ProcedureInstance { Id = InstanceId, TenantId = TenantId, Status = TramiteEstado.Anulado };

        var updated = await IdentityValidationReconciler.ApplyStatusAsync(Applier(), v, Expirado(), Now, Ct);

        updated.Should().BeFalse();
        v.Status.Should().Be(BiometricEstados.EnProceso);
        _bitacora.Should().BeEmpty();
    }

    [Fact]
    public async Task L4_RespaldoDelWebhook_ExpiradoConTramite_DejaFraseYAuditoria_SinEvento()
    {
        var audit = Substitute.For<IIdentityValidationAuditLog>();
        var kyverum = Substitute.For<IKyverumVerifyClient>();
        var protector = new FakeWebhookSecretProtector();
        var handler = new KyverumWebhookHandler(
            _repo, protector, kyverum, Applier(), audit, NullLogger<KyverumWebhookHandler>.Instance);

        var v = Validacion(InstanceId);
        v.MaxAttempts = 3;
        v.Attempts = 0;
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        _repo.TryCountKyverumAttemptAsync(v.Id, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                v.Attempts += 1;
                return true;
            });
        // Intento rechazado con intentos disponibles → el webhook consulta a Kyverum, que ya lo reporta expirado.
        kyverum.GetStatusAsync("kyv_123", "vendedor", Arg.Any<CancellationToken>()).Returns(Expirado());

        var body = Encoding.UTF8.GetBytes(
            "{\"evento\":\"validation.rejected\",\"requestId\":\"r1\",\"data\":{\"aprobado\":false,"
            + "\"closedAt\":\"2026-11-10T15:00:00.000Z\",\"subjects\":[{\"id\":\"s1\",\"rol\":\"vendedor\",\"status\":\"rechazado\",\"score\":10}]},"
            + "\"ts\":\"2026-11-10T15:00:01.000Z\"}");

        var (result, error) = await handler.HandleAsync(
            new KyverumWebhookInput(v.Id, body, KyverumWebhookVerifier.ComputeHmac(body, Secret)), Ct);

        error.Should().BeNull();
        result.Should().Be("ok");
        v.Status.Should().Be(BiometricEstados.Expirado);
        _bitacora.Should().ContainSingle(e => e.Payload.Contains("Identidad del vendedor: expirada"));
        await audit.Received(1).LogAsync(Arg.Is<IdentityValidationAuditEntry>(a =>
            a.Stage == IdentityValidationAuditStages.Reconcile
            && a.Outcome == IdentityValidationAuditOutcomes.Expired
            && a.ProcedureInstanceId == InstanceId), Ct);
        await _events.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }
}
