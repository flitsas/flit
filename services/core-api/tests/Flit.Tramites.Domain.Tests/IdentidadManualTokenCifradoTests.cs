using System;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #14305 (Feature #14304, Épica #14271) — ciclo de vida del token cifrado de la captura manual en la entidad: se guarda al
/// activar, regenerar y rechazar (AC1), se sustituye al regenerar y se BORRA al enviar la captura y al aprobar (AC5).
/// <para>Uso: <c>v.ActivarFlujoManual(userId, now, hash, cifrado)</c>.</para>
/// </summary>
public sealed class IdentidadManualTokenCifradoTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly string Hash1 = new('a', 64);
    private static readonly string Hash2 = new('b', 64);
    private static readonly string Hash3 = new('c', 64);

    private static ProcedureInstanceBiometricValidation Fila(string status, string provider = BiometricProviders.Manual) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Status = status,
        Provider = provider,
        TokenHash = Hash1,
        ExpiresAt = Now.AddHours(10),
        ManualActivatedAt = Now.AddHours(-4),
        ConsentAt = Now.AddHours(-1),
    };

    [Fact]
    public void AC1_Activar_guarda_el_cifrado_ademas_del_hash()
    {
        var v = Fila(BiometricEstados.Rechazado, BiometricProviders.Kyverum);

        v.ActivarFlujoManual(User, Now, Hash2, "enc-1");

        v.TokenHash.Should().Be(Hash2);
        v.ManualCaptureTokenEncrypted.Should().Be("enc-1");
    }

    [Fact]
    public void AC1_Regenerar_sustituye_el_cifrado_anterior()
    {
        var v = Fila(BiometricEstados.ManualActivo);
        v.ManualCaptureTokenEncrypted = "enc-viejo";

        v.RegenerarEnlaceManual(Now, Hash2, "enc-nuevo");

        v.ManualCaptureTokenEncrypted.Should().Be("enc-nuevo");
        v.TokenHash.Should().Be(Hash2);
    }

    [Fact]
    public void AC1_Rechazar_la_revision_guarda_el_cifrado_del_enlace_nuevo()
    {
        var v = Fila(BiometricEstados.PendienteRevisionManual);
        v.ManualCaptureTokenEncrypted = null;

        v.RechazarRevisionManual(User, "imagen_borrosa", Now, Hash2, "enc-rechazo");

        v.Status.Should().Be(BiometricEstados.Rechazado);
        v.ManualCaptureTokenEncrypted.Should().Be("enc-rechazo");
        v.EsperaCapturaManual.Should().BeTrue();
    }

    [Fact]
    public void AC5_Enviar_la_captura_borra_el_cifrado_y_conserva_el_hash()
    {
        var v = Fila(BiometricEstados.ManualActivo);
        v.ManualCaptureTokenEncrypted = "enc-vivo";

        v.RegistrarCapturaManual("fm-rostro", "fm-anverso", "fm-reverso", "fm-firma", new string('d', 64), Now);

        v.ManualCaptureTokenEncrypted.Should().BeNull();
        v.TokenHash.Should().Be(Hash1);
    }

    [Fact]
    public void AC5_Aprobar_nunca_deja_el_cifrado_vivo()
    {
        var v = Fila(BiometricEstados.PendienteRevisionManual);
        v.ManualCaptureTokenEncrypted = "enc-residual";

        v.Approve(Now);

        v.ManualCaptureTokenEncrypted.Should().BeNull();

        var w = Fila(BiometricEstados.Aprobado);
        w.ManualCaptureTokenEncrypted = "enc-residual";
        w.SellarAprobacionManual(User, Now);
        w.ManualCaptureTokenEncrypted.Should().BeNull();
    }

    [Fact]
    public void Un_ciclo_completo_rechazo_captura_deja_el_cifrado_en_null()
    {
        var v = Fila(BiometricEstados.PendienteRevisionManual);
        v.RechazarRevisionManual(User, "imagen_borrosa", Now, Hash3, "enc-rechazo");
        v.ConsentAt = Now.AddMinutes(1);

        v.RegistrarCapturaManual("fm-rostro", "fm-anverso", "fm-reverso", "fm-firma", new string('d', 64), Now.AddMinutes(2));

        v.ManualCaptureTokenEncrypted.Should().BeNull();
    }
}
