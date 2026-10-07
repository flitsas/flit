using System;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #13298 (Feature #13282 C3, Épica #13202) — la revisión manual aprueba por el MISMO método de dominio que Kyverum
/// (<see cref="ProcedureInstanceBiometricValidation.Approve"/>: ValidatedAt = ahora, ValidUntil = +30 días) y
/// <see cref="ProcedureInstanceBiometricValidation.SellarAprobacionManual"/> solo estampa el origen <c>manual</c> y quién/cuándo
/// revisó, sin tocar la vigencia.
/// <para>Uso: <c>v.Approve(now); v.SellarAprobacionManual(revisor, now);</c> sobre una fila manual pendiente de revisión.</para>
/// </summary>
public sealed class IdentidadManualAprobacionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 15, 0, 0, TimeSpan.Zero);

    private static ProcedureInstanceBiometricValidation Pendiente() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Provider = BiometricProviders.Manual,
        Status = BiometricEstados.PendienteRevisionManual,
        TokenHash = new string('a', 64),
    };

    [Fact]
    public void AC1_Approve_y_sello_dejan_aprobado_con_30_dias_y_origen_manual()
    {
        var v = Pendiente();
        var revisor = Guid.NewGuid();

        v.PuedeRevisarManual.Should().BeTrue();
        v.Approve(Now);
        v.SellarAprobacionManual(revisor, Now);

        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ValidatedAt.Should().Be(Now);
        v.ValidUntil.Should().Be(BiometricRules.FechaFinVigencia(Now));
        v.ApprovalOrigin.Should().Be(BiometricApprovalOrigins.Manual).And.Be("manual");
        v.ReviewedBy.Should().Be(revisor);
        v.ReviewedAt.Should().Be(Now);
        BiometricRules.EsAprobadaVigente(v, Now).Should().BeTrue();
        BiometricRules.EsAprobadaVigente(v, Now.AddDays(BiometricRules.VigenciaDias + 1)).Should().BeFalse();
    }

    [Fact]
    public void El_sello_no_cambia_la_vigencia_que_estampo_Approve()
    {
        var v = Pendiente();
        v.Approve(Now);
        var validUntil = v.ValidUntil;

        v.SellarAprobacionManual(Guid.NewGuid(), Now.AddMinutes(5));

        v.ValidatedAt.Should().Be(Now);
        v.ValidUntil.Should().Be(validUntil);
    }

    [Theory]
    [InlineData(BiometricEstados.ManualActivo, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Aprobado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Rechazado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Expirado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.PendienteRevisionManual, BiometricProviders.Kyverum)]
    [InlineData(BiometricEstados.EnProceso, BiometricProviders.Kyverum)]
    public void AC4_Solo_el_flujo_manual_pendiente_de_revision_puede_revisarse(string status, string provider)
    {
        var v = new ProcedureInstanceBiometricValidation { Status = status, Provider = provider };

        v.PuedeRevisarManual.Should().BeFalse();
    }

    [Fact]
    public void El_sello_exige_una_validacion_manual_ya_aprobada_y_un_revisor()
    {
        var sinAprobar = Pendiente();
        var kyverum = new ProcedureInstanceBiometricValidation { Provider = BiometricProviders.Kyverum, Status = BiometricEstados.Aprobado };
        var aprobada = Pendiente();
        aprobada.Approve(Now);

        var a = () => sinAprobar.SellarAprobacionManual(Guid.NewGuid(), Now);
        var b = () => kyverum.SellarAprobacionManual(Guid.NewGuid(), Now);
        var c = () => aprobada.SellarAprobacionManual(Guid.Empty, Now);

        a.Should().Throw<InvalidOperationException>();
        b.Should().Throw<InvalidOperationException>();
        c.Should().Throw<ArgumentException>();
        kyverum.ApprovalOrigin.Should().BeNull("una aprobación de Kyverum no se sella como manual");
    }
}
