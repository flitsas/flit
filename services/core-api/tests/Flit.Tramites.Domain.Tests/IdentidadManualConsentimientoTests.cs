using System;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #13289 (Feature #13281 B, Épica #13202) — invariantes del consentimiento de la captura manual en la entidad:
/// estado de la sesión por proveedor/estado/vencimiento, constancia (fecha, IP, versión) y sobrescritura de un ciclo previo.
/// <para>Uso: <c>v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, "203.0.113.7", now)</c> sobre una fila
/// manual en <c>manual_activo</c>.</para>
/// </summary>
public sealed class IdentidadManualConsentimientoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static ProcedureInstanceBiometricValidation Fila(
        string status = BiometricEstados.ManualActivo,
        string provider = BiometricProviders.Manual,
        DateTimeOffset? expiresAt = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Status = status,
        Provider = provider,
        TokenHash = new string('a', 64),
        ExpiresAt = expiresAt ?? Now.AddHours(20),
        ManualActivatedAt = Now.AddHours(-4),
    };

    [Fact]
    public void Sesion_vigente_solo_en_manual_activo_y_dentro_de_las_24h()
    {
        Fila().EstadoSesionManual(Now).Should().Be(ManualCaptureSessionState.Vigente);
        Fila(expiresAt: Now).EstadoSesionManual(Now).Should().Be(ManualCaptureSessionState.Vigente, "el límite es exclusivo");
        Fila(expiresAt: Now.AddSeconds(-1)).EstadoSesionManual(Now).Should().Be(ManualCaptureSessionState.Vencida);
    }

    [Theory]
    [InlineData(BiometricEstados.PendienteRevisionManual)]
    [InlineData(BiometricEstados.Aprobado)]
    [InlineData(BiometricEstados.Rechazado)]
    [InlineData(BiometricEstados.Expirado)]
    public void Manual_en_otro_estado_es_estado_invalido(string estado) =>
        Fila(estado).EstadoSesionManual(Now).Should().Be(ManualCaptureSessionState.EstadoInvalido);

    [Theory]
    [InlineData(BiometricProviders.Mock)]
    [InlineData(BiometricProviders.Kyverum)]
    [InlineData(BiometricProviders.MigracionV1)]
    public void Otro_proveedor_no_es_del_flujo_manual(string proveedor) =>
        Fila(BiometricEstados.Enviado, proveedor).EstadoSesionManual(Now).Should().Be(ManualCaptureSessionState.NoManual);

    [Fact]
    public void Consentimiento_guarda_fecha_ip_y_version_del_servidor()
    {
        var v = Fila();

        v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, " 203.0.113.7 ", Now);

        v.ConsentAt.Should().Be(Now);
        v.ConsentIp.Should().Be("203.0.113.7");
        v.ConsentTextVersion.Should().Be(ManualCaptureConsent.TextVersion);
        v.UpdatedAt.Should().Be(Now);
        v.TieneConsentimientoManualVigente.Should().BeTrue();
    }

    [Fact]
    public void Consentimiento_sobrescribe_el_de_un_ciclo_previo()
    {
        var v = Fila();
        v.ConsentAt = Now.AddDays(-3);
        v.ConsentIp = "198.51.100.1";
        v.ConsentTextVersion = "version-vieja";
        v.TieneConsentimientoManualVigente.Should().BeFalse("consintió antes de la activación actual");

        v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, "203.0.113.7", Now);

        v.ConsentAt.Should().Be(Now);
        v.ConsentIp.Should().Be("203.0.113.7");
        v.ConsentTextVersion.Should().Be(ManualCaptureConsent.TextVersion);
        v.TieneConsentimientoManualVigente.Should().BeTrue();
    }

    [Fact]
    public void Sin_IP_resoluble_deja_la_columna_nula_en_vez_de_inventarla()
    {
        var v = Fila();
        v.ConsentIp = "198.51.100.1";

        v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, "  ", Now);

        v.ConsentIp.Should().BeNull();
    }

    [Fact]
    public void Consentimiento_de_un_ciclo_anterior_no_cuenta_como_vigente()
    {
        var v = Fila();
        v.ConsentAt = v.ManualActivatedAt!.Value.AddSeconds(-1);
        v.TieneConsentimientoManualVigente.Should().BeFalse();

        v.ConsentAt = v.ManualActivatedAt;
        v.TieneConsentimientoManualVigente.Should().BeTrue();

        v.ManualActivatedAt = null;
        v.TieneConsentimientoManualVigente.Should().BeFalse();
    }

    [Theory]
    [InlineData(BiometricEstados.PendienteRevisionManual, ManualCaptureStateCodes.EstadoInvalido)]
    [InlineData(BiometricEstados.Aprobado, ManualCaptureStateCodes.EstadoInvalido)]
    public void Consentimiento_fuera_de_manual_activo_lanza(string estado, string codigo)
    {
        var v = Fila(estado);

        var act = () => v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, "203.0.113.7", Now);

        act.Should().Throw<ManualCaptureStateException>().Which.Code.Should().Be(codigo);
        v.ConsentAt.Should().BeNull();
    }

    [Fact]
    public void Consentimiento_con_enlace_vencido_lanza_expirada()
    {
        var v = Fila(expiresAt: Now.AddMinutes(-1));

        var act = () => v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, "203.0.113.7", Now);

        act.Should().Throw<ManualCaptureStateException>().Which.Code.Should().Be(ManualCaptureStateCodes.Expirada);
    }

    [Fact]
    public void Consentimiento_en_otro_proveedor_lanza()
    {
        var v = Fila(BiometricEstados.Enviado, BiometricProviders.Mock);

        var act = () => v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, "203.0.113.7", Now);

        act.Should().Throw<ManualCaptureStateException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("otra-version")]
    public void Consentimiento_con_version_distinta_de_la_vigente_lanza(string version)
    {
        var v = Fila();

        var act = () => v.RegistrarConsentimientoManual(version, "203.0.113.7", Now);

        act.Should().Throw<ArgumentException>();
        v.ConsentAt.Should().BeNull();
    }
}
