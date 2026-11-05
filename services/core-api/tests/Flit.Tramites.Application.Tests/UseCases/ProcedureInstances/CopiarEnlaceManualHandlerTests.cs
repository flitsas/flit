using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #14305 (Feature #14304, Épica #14271) — <see cref="CopiarEnlaceManualHandler"/>: entrega el enlace completo de una validación
/// que espera captura con enlace vigente SIN regenerar (token y hash no cambian, sin correo), audita quién y cuándo sin token ni
/// URL, y responde 409 (<c>enlace_vencido</c>, <c>enlace_no_disponible</c>, <c>flujo_manual_no_activo</c>) en los demás casos.
/// <para>Uso: <c>await handler.HandleAsync(new CopiarEnlaceManualCommand(validationId, userId), ct)</c>.</para>
/// </summary>
public sealed class CopiarEnlaceManualHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly string Token = new('t', 18);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();
    private readonly IManualCaptureLinkUrlBuilder _urls = Substitute.For<IManualCaptureLinkUrlBuilder>();
    private readonly FakeManualCaptureTokenProtector _protector = new();

    public CopiarEnlaceManualHandlerTests()
    {
        _urls.Build(Arg.Any<string>()).Returns(c => "https://app.flit.test/verificacion/" + c.Arg<string>());
    }

    private CopiarEnlaceManualHandler Handler() => new(_repo, _audit, _protector, _urls, new FixedTime(Now));

    private ProcedureInstanceBiometricValidation Fila(
        string status = BiometricEstados.ManualActivo,
        string provider = BiometricProviders.Manual,
        DateTimeOffset? expiresAt = null,
        string? cifrado = "enc:" + Token,
        string? rejectionReason = null)
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
            TokenHash = BiometricToken.Hash(Token),
            ManualCaptureTokenEncrypted = cifrado,
            RejectionReasonCode = rejectionReason,
            ExpiresAt = expiresAt ?? Now.AddHours(5),
            ManualActivatedAt = Now.AddHours(-19),
        };
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    [Fact]
    public async Task AC2_ConEnlaceVigente_DevuelveLaUrlCompleta_SinTocarTokenNiHash_NiMandarCorreo()
    {
        var v = Fila();
        var hash = v.TokenHash;
        var cifrado = v.ManualCaptureTokenEncrypted;
        var expira = v.ExpiresAt;

        var (result, error) = await Handler().HandleAsync(new CopiarEnlaceManualCommand(v.Id, User), Ct);

        error.Should().BeNull();
        result!.Url.Should().Be("https://app.flit.test/verificacion/" + Token);
        result.ExpiresAt.Should().Be(expira);
        v.TokenHash.Should().Be(hash);
        v.ManualCaptureTokenEncrypted.Should().Be(cifrado);
        v.ExpiresAt.Should().Be(expira);
        v.ResendCount.Should().Be(0);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_RechazadaConMotivoYEnlaceVigente_TambienSeCopia()
    {
        var v = Fila(status: BiometricEstados.Rechazado, rejectionReason: "imagen_borrosa");

        var (result, error) = await Handler().HandleAsync(new CopiarEnlaceManualCommand(v.Id, User), Ct);

        error.Should().BeNull();
        result!.Url.Should().EndWith(Token);
    }

    [Fact]
    public async Task AC2_AuditaQuienYCuando_SinTokenNiUrlNiHashNiCorreo()
    {
        var v = Fila();
        IdentityValidationAuditEntry? entrada = null;
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(e => entrada = e), Arg.Any<CancellationToken>());

        await Handler().HandleAsync(new CopiarEnlaceManualCommand(v.Id, User), Ct);

        await _audit.Received(1).LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
        entrada.Should().NotBeNull();
        entrada!.Stage.Should().Be(IdentityValidationAuditStages.ManualEnlaceCopiado).And.Be("manual_enlace_copiado");
        entrada.TenantId.Should().Be(v.TenantId);
        entrada.ValidationId.Should().Be(v.Id);
        var texto = $"{entrada.Message} {entrada.Detail}";
        texto.Should().Contain(User.ToString())
            .And.NotContain(Token).And.NotContain("https://").And.NotContain(v.TokenHash)
            .And.NotContain(v.ManualCaptureTokenEncrypted!).And.NotContain("persona@example.test");
    }

    [Fact]
    public async Task AC3_EnlaceVencido_Responde409Vencido_SinAuditar()
    {
        var v = Fila(expiresAt: Now.AddMinutes(-1));

        var (result, error) = await Handler().HandleAsync(new CopiarEnlaceManualCommand(v.Id, User), Ct);

        result.Should().BeNull();
        error.Should().Be(CopiarEnlaceManualHandler.EnlaceVencido).And.Be("enlace_vencido");
        await _audit.DidNotReceive().LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_CapturaYaEnviada_Responde409EnlaceNoDisponible()
    {
        var v = Fila(status: BiometricEstados.PendienteRevisionManual, cifrado: null);

        var (result, error) = await Handler().HandleAsync(new CopiarEnlaceManualCommand(v.Id, User), Ct);

        result.Should().BeNull();
        error.Should().Be("enlace_no_disponible");
    }

    [Fact]
    public async Task AC3_EnlaceAntiguoSinTokenCifrado_Responde409EnlaceNoDisponible()
    {
        var v = Fila(cifrado: null);

        var (result, error) = await Handler().HandleAsync(new CopiarEnlaceManualCommand(v.Id, User), Ct);

        result.Should().BeNull();
        error.Should().Be("enlace_no_disponible");
    }

    [Fact]
    public async Task DescifradoFallido_LlavesRotadas_Responde409EnlaceNoDisponible_NuncaLanza()
    {
        var v = Fila(cifrado: "CfDJ8-blob-con-llaves-rotadas");

        var (result, error) = await Handler().HandleAsync(new CopiarEnlaceManualCommand(v.Id, User), Ct);

        result.Should().BeNull();
        error.Should().Be("enlace_no_disponible");
        await _audit.DidNotReceive().LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BiometricEstados.Aprobado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Expirado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Enviado, BiometricProviders.Kyverum)]
    [InlineData(BiometricEstados.ManualActivo, BiometricProviders.Kyverum)]
    public async Task OtrosEstados_Responden409FlujoManualNoActivo(string status, string provider)
    {
        var v = Fila(status: status, provider: provider);

        var (result, error) = await Handler().HandleAsync(new CopiarEnlaceManualCommand(v.Id, User), Ct);

        result.Should().BeNull();
        error.Should().Be("flujo_manual_no_activo");
    }

    [Fact]
    public async Task ValidacionInexistente_Responde404()
    {
        var (result, error) = await Handler().HandleAsync(new CopiarEnlaceManualCommand(Guid.NewGuid(), User), Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
