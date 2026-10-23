using System;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #13284 (Feature #13280 A2, Épica #13202) — <see cref="ProcedureInstanceBiometricValidation.ActivarFlujoManual"/>
/// concentra las invariantes de la activación: matriz de estado origen x resultado, token solo como hash, TTL de 24 h y
/// cancelación de lo operativo de Kyverum.
/// <para>Uso: <c>v.ActivarFlujoManual(userId, now, BiometricTokenHashFixture)</c> sobre una fila no aprobada y vigente.</para>
/// </summary>
public sealed class IdentidadManualActivacionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly string Hash = new('a', 64);

    private static ProcedureInstanceBiometricValidation Fila(string status, string provider = BiometricProviders.Kyverum) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Status = status,
        Provider = provider,
        TokenHash = new string('0', 64),
        ExpiresAt = Now.AddHours(-3),
        KyverumVerificationId = "kyv_ext_1",
        CaptureUrl = "https://captura.example.test/abc",
        WebhookSecretEncrypted = "cifrado",
        ProviderStatus = "pending",
        Attempts = 2,
        ReconcilePollCount = 3,
        LastAttemptAt = "2026-10-05T10:00:00Z",
    };

    [Theory]
    [InlineData(BiometricEstados.Enviado)]
    [InlineData(BiometricEstados.EnProceso)]
    [InlineData(BiometricEstados.Rechazado)]
    [InlineData(BiometricEstados.Expirado)]
    [InlineData(BiometricEstados.PendienteEnvio)]
    [InlineData(BiometricEstados.ErrorEnvio)]
    [InlineData(BiometricEstados.ManualActivo)]
    [InlineData(BiometricEstados.PendienteRevisionManual)]
    public void DesdeUnEstadoNoAprobado_Activa(string origen)
    {
        var v = Fila(origen);

        v.PuedeActivarFlujoManual(Now).Should().BeTrue();
        v.ActivarFlujoManual(User, Now, Hash);

        v.Provider.Should().Be(BiometricProviders.Manual);
        v.Status.Should().Be(BiometricEstados.ManualActivo);
    }

    [Fact]
    public void AprobadaVencida_Activa()
    {
        var v = Fila(BiometricEstados.Aprobado);
        v.ValidatedAt = Now.AddDays(-40);
        v.ValidUntil = Now.AddDays(-10);

        v.PuedeActivarFlujoManual(Now).Should().BeTrue();
        v.ActivarFlujoManual(User, Now, Hash);

        v.Status.Should().Be(BiometricEstados.ManualActivo);
    }

    [Fact]
    public void AprobadaYVigente_NoActiva_YNoCambiaNada()
    {
        var v = Fila(BiometricEstados.Aprobado);
        v.ValidatedAt = Now.AddDays(-1);
        v.ValidUntil = Now.AddDays(29);

        v.PuedeActivarFlujoManual(Now).Should().BeFalse();
        var act = () => v.ActivarFlujoManual(User, Now, Hash);

        act.Should().Throw<IdentidadManualNoActivableException>();
        v.Provider.Should().Be(BiometricProviders.Kyverum);
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.KyverumVerificationId.Should().Be("kyv_ext_1");
        v.ManualActivatedBy.Should().BeNull();
    }

    [Fact]
    public void GuardaElHashDelToken_YElEnlaceDura24Horas()
    {
        var v = Fila(BiometricEstados.EnProceso);

        v.ActivarFlujoManual(User, Now, Hash);

        v.TokenHash.Should().Be(Hash);
        v.ExpiresAt.Should().Be(Now.AddHours(24));
        BiometricRules.TokenTtlHoras.Should().Be(24);
        v.ManualActivatedBy.Should().Be(User);
        v.ManualActivatedAt.Should().Be(Now);
        v.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void CancelaLoOperativoDeKyverum()
    {
        var v = Fila(BiometricEstados.EnProceso);

        v.ActivarFlujoManual(User, Now, Hash);

        v.KyverumVerificationId.Should().BeNull("el id externo queda solo en la auditoría");
        v.CaptureUrl.Should().BeNull();
        v.WebhookSecretEncrypted.Should().BeNull("sin secreto, un webhook posterior no se puede verificar");
        v.ProviderStatus.Should().BeNull();
        v.Attempts.Should().Be(0);
        v.ReconcilePollCount.Should().Be(0);
        v.LastAttemptAt.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tokencrudo")]
    [InlineData("abc123")]
    public void ExigeUnHashSha256_NoUnTokenCrudo(string valor)
    {
        var v = Fila(BiometricEstados.Rechazado);

        var act = () => v.ActivarFlujoManual(User, Now, valor);

        act.Should().Throw<ArgumentException>();
        v.Status.Should().Be(BiometricEstados.Rechazado);
    }

    [Fact]
    public void ExigeElUsuarioQueActiva()
    {
        var v = Fila(BiometricEstados.Rechazado);

        var act = () => v.ActivarFlujoManual(Guid.Empty, Now, Hash);

        act.Should().Throw<ArgumentException>();
        v.Provider.Should().Be(BiometricProviders.Kyverum);
    }
}
