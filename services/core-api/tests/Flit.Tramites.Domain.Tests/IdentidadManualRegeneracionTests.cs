using System;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #13287 (Feature #13280 A5, Épica #13202) — <see cref="ProcedureInstanceBiometricValidation.RegenerarEnlaceManual"/>
/// concentra las invariantes de la regeneración: solo en <c>manual_activo</c>, hash SHA-256 distinto del anterior que
/// reemplaza al viejo, TTL de 24 h desde ahora y conteo del reenvío; no toca el estado ni la activación.
/// <para>Uso: <c>v.RegenerarEnlaceManual(now, nuevoHash)</c> sobre una fila manual activa.</para>
/// </summary>
public sealed class IdentidadManualRegeneracionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly string HashViejo = new('0', 64);
    private static readonly string HashNuevo = new('b', 64);

    private static ProcedureInstanceBiometricValidation Fila(
        string status = BiometricEstados.ManualActivo, string provider = BiometricProviders.Manual) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Status = status,
        Provider = provider,
        TokenHash = HashViejo,
        ExpiresAt = Now.AddHours(-30),
        ManualActivatedBy = Guid.NewGuid(),
        ManualActivatedAt = Now.AddHours(-54),
    };

    [Fact]
    public void EnManualActivo_ReemplazaElHash_ReiniciaLaVigenciaA24h_YCuentaElReenvio()
    {
        var v = Fila();
        var activadoPor = v.ManualActivatedBy;
        var activadoEn = v.ManualActivatedAt;

        v.PuedeRegenerarEnlaceManual.Should().BeTrue();
        v.RegenerarEnlaceManual(Now, HashNuevo);

        v.TokenHash.Should().Be(HashNuevo).And.NotBe(HashViejo);
        v.ExpiresAt.Should().Be(Now.AddHours(BiometricRules.TokenTtlHoras)).And.Be(Now.AddHours(24));
        v.ResendCount.Should().Be(1);
        v.LastResentAt.Should().Be(Now);
        v.UpdatedAt.Should().Be(Now);
        v.Status.Should().Be(BiometricEstados.ManualActivo);
        v.Provider.Should().Be(BiometricProviders.Manual);
        v.ManualActivatedBy.Should().Be(activadoPor);
        v.ManualActivatedAt.Should().Be(activadoEn);
    }

    [Fact]
    public void ConElEnlaceVencido_TambienRegenera()
    {
        var v = Fila();
        v.ExpiresAt = Now.AddDays(-5);

        v.RegenerarEnlaceManual(Now, HashNuevo);

        v.ExpiresAt.Should().Be(Now.AddHours(24));
    }

    [Theory]
    [InlineData(BiometricEstados.Aprobado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.PendienteRevisionManual, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Rechazado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Expirado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Enviado, BiometricProviders.Kyverum)]
    [InlineData(BiometricEstados.ManualActivo, BiometricProviders.Kyverum)]
    public void FueraDeManualActivo_NoRegenera(string status, string provider)
    {
        var v = Fila(status, provider);

        v.PuedeRegenerarEnlaceManual.Should().BeFalse();
        var act = () => v.RegenerarEnlaceManual(Now, HashNuevo);

        act.Should().Throw<FlujoManualNoActivoException>();
        v.TokenHash.Should().Be(HashViejo);
        v.ResendCount.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("corto")]
    public void ElTokenCrudoOUnHashInvalido_SeRechaza(string hash)
    {
        var v = Fila();

        var act = () => v.RegenerarEnlaceManual(Now, hash);

        act.Should().Throw<ArgumentException>();
        v.TokenHash.Should().Be(HashViejo);
    }

    [Fact]
    public void ElMismoHashDelAnterior_SeRechaza()
    {
        var v = Fila();

        var act = () => v.RegenerarEnlaceManual(Now, HashViejo);

        act.Should().Throw<ArgumentException>();
        v.ResendCount.Should().Be(0);
    }
}
