using Flit.Admin.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #11059 / HU #11060 — vigencia de la identidad administrativa, compartida por el representante
/// legal y el mandatario del OT. Lo que estas pruebas fijan y antes no existía: que además del estado
/// se devuelva HASTA CUÁNDO es válida (el cálculo previo descartaba la fecha).
/// </summary>
public sealed class AdminIdentityVigenciaTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    private static AdminIdentityVigencia.Entrada Entrada(string status, DateTimeOffset? validUntil = null) =>
        new(status, validUntil);

    [Fact]
    public void SinValidaciones_EsNone()
    {
        var r = AdminIdentityVigencia.Resumir([], Now);

        r.Status.Should().Be(AdminIdentityVigencia.None);
        r.ValidUntil.Should().BeNull();
    }

    [Fact]
    public void AprobadaVigente_EsValidEInformaHastaCuando()
    {
        var hasta = Now.AddDays(12);

        var r = AdminIdentityVigencia.Resumir([Entrada(AdminIdentityEstados.Aprobado, hasta)], Now);

        r.Status.Should().Be(AdminIdentityVigencia.Valid);
        r.ValidUntil.Should().Be(hasta);
    }

    [Fact]
    public void AprobadaSinCaducidad_EsValidSinFecha()
    {
        var r = AdminIdentityVigencia.Resumir([Entrada(AdminIdentityEstados.Aprobado)], Now);

        r.Status.Should().Be(AdminIdentityVigencia.Valid);
        r.ValidUntil.Should().BeNull();
    }

    [Fact]
    public void AprobadaVencida_EsExpired()
    {
        var r = AdminIdentityVigencia.Resumir(
            [Entrada(AdminIdentityEstados.Aprobado, Now.AddDays(-1))], Now);

        r.Status.Should().Be(AdminIdentityVigencia.Expired);
        // Vencida ⇒ no se informa vigencia: no hay nada vigente que reportar.
        r.ValidUntil.Should().BeNull();
    }

    [Theory]
    [InlineData(AdminIdentityEstados.Expirado)]
    [InlineData(AdminIdentityEstados.Rechazado)]
    public void RechazadaOExpirada_EsExpired(string status)
    {
        var r = AdminIdentityVigencia.Resumir([Entrada(status)], Now);

        r.Status.Should().Be(AdminIdentityVigencia.Expired);
    }

    [Theory]
    [InlineData(AdminIdentityEstados.Enviado)]
    [InlineData(AdminIdentityEstados.EnProceso)]
    public void EnCurso_EsPending(string status)
    {
        var r = AdminIdentityVigencia.Resumir([Entrada(status)], Now);

        r.Status.Should().Be(AdminIdentityVigencia.Pending);
        r.ValidUntil.Should().BeNull();
    }

    [Fact]
    public void AprobadaVigente_GanaSobreRechazadaYEnCurso()
    {
        var hasta = Now.AddDays(5);

        var r = AdminIdentityVigencia.Resumir(
            [
                Entrada(AdminIdentityEstados.Rechazado),
                Entrada(AdminIdentityEstados.Enviado),
                Entrada(AdminIdentityEstados.Aprobado, hasta),
            ],
            Now);

        r.Status.Should().Be(AdminIdentityVigencia.Valid);
        r.ValidUntil.Should().Be(hasta);
    }

    [Fact]
    public void EnCurso_GanaSobreVencida()
    {
        var r = AdminIdentityVigencia.Resumir(
            [
                Entrada(AdminIdentityEstados.Aprobado, Now.AddDays(-3)),
                Entrada(AdminIdentityEstados.EnProceso),
            ],
            Now);

        // Ya hay una captura en curso: ofrecer "renovar" sería pedir lo que ya se está haciendo.
        r.Status.Should().Be(AdminIdentityVigencia.Pending);
    }

    [Fact]
    public void VariasVigentes_InformaLaQueCaducaMasTarde()
    {
        var pronto = Now.AddDays(2);
        var tarde = Now.AddDays(20);

        var r = AdminIdentityVigencia.Resumir(
            [
                Entrada(AdminIdentityEstados.Aprobado, pronto),
                Entrada(AdminIdentityEstados.Aprobado, tarde),
            ],
            Now);

        r.Status.Should().Be(AdminIdentityVigencia.Valid);
        r.ValidUntil.Should().Be(tarde);
    }

    [Fact]
    public void VigenteSinCaducidad_GanaSobreVigenteConFecha()
    {
        var r = AdminIdentityVigencia.Resumir(
            [
                Entrada(AdminIdentityEstados.Aprobado, Now.AddDays(2)),
                Entrada(AdminIdentityEstados.Aprobado),
            ],
            Now);

        r.Status.Should().Be(AdminIdentityVigencia.Valid);
        // Una aprobada sin caducidad no expira: informar una fecha sería mentir por defecto.
        r.ValidUntil.Should().BeNull();
    }

    [Fact]
    public void EstadoDesconocido_NoCuentaComoValidacion()
    {
        var r = AdminIdentityVigencia.Resumir([Entrada("vete_a_saber")], Now);

        r.Status.Should().Be(AdminIdentityVigencia.None);
    }
}
