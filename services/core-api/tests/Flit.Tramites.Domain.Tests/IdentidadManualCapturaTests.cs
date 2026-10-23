using System;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #13290 (Feature #13281 B, Épica #13202) — <see cref="ProcedureInstanceBiometricValidation.RegistrarCapturaManual"/>:
/// la transición a <c>pendiente_revision_manual</c> solo sale de <c>manual_activo</c> con sesión vigente y consentimiento del
/// ciclo actual, asigna las 4 rutas y el SHA-256 de la firma (ADR-0054) y consume el enlace.
/// <para>Uso: <c>v.RegistrarCapturaManual("rostro", "anverso", "reverso", "firma", sha256Hex, now)</c>.</para>
/// </summary>
public sealed class IdentidadManualCapturaTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Sha = new('b', 64);

    private static ProcedureInstanceBiometricValidation Fila(
        string status = BiometricEstados.ManualActivo,
        string provider = BiometricProviders.Manual,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? consentAt = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Status = status,
        Provider = provider,
        TokenHash = new string('a', 64),
        ExpiresAt = expiresAt ?? Now.AddHours(20),
        ManualActivatedAt = Now.AddHours(-4),
        ConsentAt = consentAt ?? Now.AddHours(-1),
    };

    private static void Registrar(ProcedureInstanceBiometricValidation v, DateTimeOffset? now = null) =>
        v.RegistrarCapturaManual("fm-rostro", "fm-anverso", "fm-reverso", "fm-firma", Sha, now ?? Now);

    [Fact]
    public void Captura_valida_asigna_rutas_y_hash_de_firma_y_pasa_a_revision()
    {
        var v = Fila();

        Registrar(v);

        v.FacePhotoPath.Should().Be("fm-rostro");
        v.IdFrontPhotoPath.Should().Be("fm-anverso");
        v.IdBackPhotoPath.Should().Be("fm-reverso");
        v.SignatureImagePath.Should().Be("fm-firma");
        v.SignatureImageSha256.Should().Be(Sha);
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        v.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Tras_la_captura_el_enlace_queda_consumido()
    {
        var v = Fila();
        Registrar(v);

        v.EstadoSesionManual(Now).Should().Be(ManualCaptureSessionState.EstadoInvalido);
        var otra = () => Registrar(v);

        otra.Should().Throw<ManualCaptureStateException>().Which.Code.Should().Be(ManualCaptureStateCodes.EstadoInvalido);
    }

    [Fact]
    public void Un_intento_nuevo_tras_reactivar_pisa_las_rutas_de_la_fila_pero_exige_nuevo_consentimiento()
    {
        var v = Fila();
        Registrar(v);

        // Rechazado y reactivado (A2): vuelve a manual_activo con activación nueva; el consentimiento viejo ya no vale.
        v.Status = BiometricEstados.ManualActivo;
        v.ManualActivatedAt = Now.AddHours(1);
        v.ExpiresAt = Now.AddHours(25);
        var sinConsentir = () => Registrar(v, Now.AddHours(2));
        sinConsentir.Should().Throw<ManualCaptureStateException>().Which.Code.Should().Be(ManualCaptureStateCodes.ConsentimientoRequerido);

        v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, "203.0.113.7", Now.AddHours(2));
        v.RegistrarCapturaManual("fm-2", "fm-3", "fm-4", "fm-5", new string('c', 64), Now.AddHours(3));

        v.FacePhotoPath.Should().Be("fm-2");
        v.SignatureImageSha256.Should().Be(new string('c', 64));
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
    }

    [Theory]
    [InlineData(BiometricEstados.PendienteRevisionManual)]
    [InlineData(BiometricEstados.Aprobado)]
    [InlineData(BiometricEstados.Rechazado)]
    [InlineData(BiometricEstados.Expirado)]
    public void Solo_se_captura_desde_manual_activo(string estado)
    {
        var v = Fila(estado);

        var act = () => Registrar(v);

        act.Should().Throw<ManualCaptureStateException>().Which.Code.Should().Be(ManualCaptureStateCodes.EstadoInvalido);
        v.FacePhotoPath.Should().BeNull();
        v.Status.Should().Be(estado);
    }

    [Fact]
    public void Enlace_vencido_no_captura()
    {
        var v = Fila(expiresAt: Now.AddSeconds(-1));

        var act = () => Registrar(v);

        act.Should().Throw<ManualCaptureStateException>().Which.Code.Should().Be(ManualCaptureStateCodes.Expirada);
        v.Status.Should().Be(BiometricEstados.ManualActivo);
    }

    [Fact]
    public void Otro_proveedor_no_captura()
    {
        var v = Fila(BiometricEstados.Enviado, BiometricProviders.Kyverum);

        var act = () => Registrar(v);

        act.Should().Throw<ManualCaptureStateException>();
    }

    [Fact]
    public void Sin_consentimiento_del_ciclo_actual_no_captura()
    {
        var sinConsentimiento = Fila();
        sinConsentimiento.ConsentAt = null;
        var deCicloAnterior = Fila();
        deCicloAnterior.ConsentAt = deCicloAnterior.ManualActivatedAt!.Value.AddTicks(-1);

        foreach (var v in new[] { sinConsentimiento, deCicloAnterior })
        {
            var act = () => Registrar(v);
            act.Should().Throw<ManualCaptureStateException>().Which.Code.Should().Be(ManualCaptureStateCodes.ConsentimientoRequerido);
            v.Status.Should().Be(BiometricEstados.ManualActivo);
        }
    }

    [Theory]
    [InlineData("", "fm-anverso", "fm-reverso", "fm-firma")]
    [InlineData("fm-rostro", " ", "fm-reverso", "fm-firma")]
    [InlineData("fm-rostro", "fm-anverso", "", "fm-firma")]
    [InlineData("fm-rostro", "fm-anverso", "fm-reverso", "")]
    public void Exige_las_4_rutas(string rostro, string anverso, string reverso, string firma)
    {
        var v = Fila();

        var act = () => v.RegistrarCapturaManual(rostro, anverso, reverso, firma, Sha, Now);

        act.Should().Throw<ArgumentException>();
        v.Status.Should().Be(BiometricEstados.ManualActivo);
        v.FacePhotoPath.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void El_hash_de_la_firma_debe_ser_sha256(string hash)
    {
        var v = Fila();

        var act = () => v.RegistrarCapturaManual("fm-rostro", "fm-anverso", "fm-reverso", "fm-firma", hash, Now);

        act.Should().Throw<ArgumentException>();
        v.SignatureImagePath.Should().BeNull();
    }
}
