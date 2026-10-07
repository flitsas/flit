using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13287 (Feature #13280 A5, Épica #13202) — <see cref="RegenerarEnlaceManualHandler"/>: regenera el enlace de una
/// validación en <c>manual_activo</c> (token nuevo de 24 h guardado como hash que reemplaza al anterior), responde 409
/// <c>flujo_manual_no_activo</c> en cualquier otro estado, envía el correo una sola vez, y un fallo de correo no revierte la
/// regeneración.
/// <para>Uso: <c>await handler.HandleAsync(new RegenerarEnlaceManualCommand(validationId, userId), ct)</c>.</para>
/// </summary>
public sealed class RegenerarEnlaceManualHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();
    private readonly IManualCaptureLinkNotifier _notifier = Substitute.For<IManualCaptureLinkNotifier>();

    public RegenerarEnlaceManualHandlerTests()
    {
        _notifier.NotifyAsync(Arg.Any<ManualCaptureLink>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private RegenerarEnlaceManualHandler Handler() => new(_repo, _audit, _notifier, new FixedTime(Now));

    private ProcedureInstanceBiometricValidation Fila(
        string status = BiometricEstados.ManualActivo, string provider = BiometricProviders.Manual)
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            Name = "Persona de prueba",
            Email = "persona@example.test",
            Status = status,
            Provider = provider,
            TokenHash = BiometricToken.Hash("token-viejo"),
            ExpiresAt = Now.AddHours(-30),
            ManualActivatedAt = Now.AddHours(-54),
        };
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    [Fact]
    public async Task AC3_Regenera_ConTokenNuevoDe24h_YElViejoDejaDeEncontrarsePorHash()
    {
        var v = Fila();
        var hashViejo = v.TokenHash;
        ManualCaptureLink? enviado = null;
        _notifier.NotifyAsync(Arg.Do<ManualCaptureLink>(l => enviado = l), Arg.Any<CancellationToken>()).Returns(true);

        var (result, error) = await Handler().HandleAsync(new RegenerarEnlaceManualCommand(v.Id, User), Ct);

        error.Should().BeNull();
        result!.Status.Should().Be("manual_activo");
        result.Provider.Should().Be("manual");
        result.ExpiresAt.Should().Be(Now.AddHours(24));
        result.TenantId.Should().Be(v.TenantId);
        result.EmailEnviado.Should().BeTrue();
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // El token nuevo reemplaza al viejo: en la fila solo está el hash nuevo; el viejo ya no coincide con nada.
        enviado.Should().NotBeNull();
        v.TokenHash.Should().Be(BiometricToken.Hash(enviado!.Token)).And.NotBe(hashViejo).And.NotBe(enviado.Token);
        BiometricToken.Hash("token-viejo").Should().NotBe(v.TokenHash);
        v.ExpiresAt.Should().Be(Now.AddHours(24));
        v.ResendCount.Should().Be(1);
        v.LastResentAt.Should().Be(Now);
    }

    [Fact]
    public async Task AC3_EnviaElCorreoUnaSolaVez_ConElDestinatarioDeLaFila()
    {
        var v = Fila();

        await Handler().HandleAsync(new RegenerarEnlaceManualCommand(v.Id, User), Ct);

        await _notifier.Received(1).NotifyAsync(
            Arg.Is<ManualCaptureLink>(l => l.RecipientEmail == "persona@example.test" && l.ValidationId == v.Id
                && l.TenantId == v.TenantId && l.ExpiresAt == Now.AddHours(24)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_Audita_manual_enlace_regenerado_SinTokenNiHashNiCorreo()
    {
        var v = Fila();
        var entradas = new List<IdentityValidationAuditEntry>();
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(entradas.Add), Arg.Any<CancellationToken>());
        string? token = null;
        _notifier.NotifyAsync(Arg.Do<ManualCaptureLink>(l => token = l.Token), Arg.Any<CancellationToken>()).Returns(true);

        await Handler().HandleAsync(new RegenerarEnlaceManualCommand(v.Id, User), Ct);

        var regenerado = entradas.Should().ContainSingle(e => e.Stage == IdentityValidationAuditStages.ManualEnlaceRegenerado).Subject;
        regenerado.TenantId.Should().Be(v.TenantId);
        regenerado.ValidationId.Should().Be(v.Id);
        regenerado.Detail.Should().Contain(User.ToString());
        foreach (var e in entradas)
        {
            (e.Message + e.Detail).Should().NotContain(token!).And.NotContain(v.TokenHash).And.NotContain("persona@example.test");
        }
    }

    [Theory]
    [InlineData(BiometricEstados.Aprobado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.PendienteRevisionManual, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Rechazado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Expirado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.EnProceso, BiometricProviders.Kyverum)]
    [InlineData(BiometricEstados.Aprobado, BiometricProviders.Kyverum)]
    public async Task AC4_SiNoEstaEnManualActivo_Responde409_YNoCambiaNada(string status, string provider)
    {
        var v = Fila(status, provider);
        var hashAntes = v.TokenHash;

        var (result, error) = await Handler().HandleAsync(new RegenerarEnlaceManualCommand(v.Id, User), Ct);

        result.Should().BeNull();
        error.Should().Be(RegenerarEnlaceManualHandler.FlujoManualNoActivo).And.Be("flujo_manual_no_activo");
        v.TokenHash.Should().Be(hashAntes);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<ManualCaptureLink>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidacionInexistente_DevuelveNotFound()
    {
        var (result, error) = await Handler().HandleAsync(new RegenerarEnlaceManualCommand(Guid.NewGuid(), User), Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    [Fact]
    public async Task ElCorreoNoSale_NoRevierteLaRegeneracion_AuditaYLoInforma()
    {
        var v = Fila();
        var hashViejo = v.TokenHash;
        _notifier.NotifyAsync(Arg.Any<ManualCaptureLink>(), Arg.Any<CancellationToken>()).Returns(false);
        var entradas = new List<IdentityValidationAuditEntry>();
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(entradas.Add), Arg.Any<CancellationToken>());

        var (result, error) = await Handler().HandleAsync(new RegenerarEnlaceManualCommand(v.Id, User), Ct);

        error.Should().BeNull();
        result!.EmailEnviado.Should().BeFalse();
        v.TokenHash.Should().NotBe(hashViejo, "el enlace nuevo ya está emitido aunque el correo no haya salido");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        entradas.Select(e => e.Stage).Should().BeEquivalentTo(
            [IdentityValidationAuditStages.ManualEnlaceRegenerado, IdentityValidationAuditStages.ManualCorreoFallido]);
    }

    [Fact]
    public async Task ElNotificadorLanza_NoRevierteLaRegeneracion()
    {
        var v = Fila();
        _notifier.NotifyAsync(Arg.Any<ManualCaptureLink>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("smtp caido"));

        var (result, error) = await Handler().HandleAsync(new RegenerarEnlaceManualCommand(v.Id, User), Ct);

        error.Should().BeNull();
        result!.EmailEnviado.Should().BeFalse();
        v.ExpiresAt.Should().Be(Now.AddHours(24));
    }

    [Fact]
    public async Task DosRegeneraciones_DejanSoloElUltimoToken()
    {
        var v = Fila();
        var tokens = new List<string>();
        _notifier.NotifyAsync(Arg.Do<ManualCaptureLink>(l => tokens.Add(l.Token)), Arg.Any<CancellationToken>()).Returns(true);

        await Handler().HandleAsync(new RegenerarEnlaceManualCommand(v.Id, User), Ct);
        await Handler().HandleAsync(new RegenerarEnlaceManualCommand(v.Id, User), Ct);

        tokens.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        v.TokenHash.Should().Be(BiometricToken.Hash(tokens[1])).And.NotBe(BiometricToken.Hash(tokens[0]));
        v.ResendCount.Should().Be(2);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
