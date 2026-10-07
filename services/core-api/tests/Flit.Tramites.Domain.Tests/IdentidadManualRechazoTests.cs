using System;
using System.Linq;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #13299 (Feature #13282 C4, Épica #13202) — <see cref="ProcedureInstanceBiometricValidation.RechazarRevisionManual"/>:
/// registra motivo (lista cerrada) y revisor, deja la fila en <c>rechazado</c> con un enlace nuevo de 24 h (hash distinto) con el
/// que se puede repetir la captura, abre un ciclo nuevo (el consentimiento anterior ya no vale), conserva las rutas anteriores y no
/// tiene tope de intentos; la captura siguiente limpia motivo y revisión. Más el catálogo cerrado
/// <see cref="ManualRejectionReasons"/>.
/// <para>Uso: <c>v.RechazarRevisionManual(revisor, "imagen_borrosa", now, hashNuevo)</c> sobre una fila pendiente de revisión.</para>
/// </summary>
public sealed class IdentidadManualRechazoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 15, 0, 0, TimeSpan.Zero);
    private static readonly string HashViejo = new('0', 64);
    private static readonly string HashNuevo = new('b', 64);

    private static ProcedureInstanceBiometricValidation Pendiente() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Provider = BiometricProviders.Manual,
        Status = BiometricEstados.PendienteRevisionManual,
        TokenHash = HashViejo,
        ExpiresAt = Now.AddHours(-1),
        ManualActivatedBy = Guid.NewGuid(),
        ManualActivatedAt = Now.AddHours(-5),
        ConsentAt = Now.AddHours(-4),
        ConsentTextVersion = ManualCaptureConsent.TextVersion,
        FacePhotoPath = "ruta-rostro-1",
        IdFrontPhotoPath = "ruta-anverso-1",
        IdBackPhotoPath = "ruta-reverso-1",
        SignatureImagePath = "ruta-firma-1",
        SignatureImageSha256 = new string('a', 64),
    };

    [Fact]
    public void AC1_Rechazar_registra_motivo_y_revisor_deja_rechazado_con_enlace_nuevo_de_24h_vigente()
    {
        var v = Pendiente();
        var revisor = Guid.NewGuid();
        var activadoPor = v.ManualActivatedBy;

        v.RechazarRevisionManual(revisor, ManualRejectionReasons.ImagenBorrosa, Now, HashNuevo);

        v.Status.Should().Be(BiometricEstados.Rechazado);
        v.Provider.Should().Be(BiometricProviders.Manual);
        v.RejectionReasonCode.Should().Be("imagen_borrosa");
        v.ReviewedBy.Should().Be(revisor);
        v.ReviewedAt.Should().Be(Now);
        v.TokenHash.Should().Be(HashNuevo).And.NotBe(HashViejo);
        v.ExpiresAt.Should().Be(Now.AddHours(24));
        v.ManualActivatedBy.Should().Be(activadoPor);
        v.ManualActivatedAt.Should().Be(Now);
        v.EsperaCapturaManual.Should().BeTrue();
        v.PuedeRegenerarEnlaceManual.Should().BeTrue();
        v.EstadoSesionManual(Now).Should().Be(ManualCaptureSessionState.Vigente);
        v.EstadoSesionManual(Now.AddHours(24).AddSeconds(1)).Should().Be(ManualCaptureSessionState.Vencida);
        v.UpdatedAt.Should().Be(Now);
        v.ApprovalOrigin.Should().BeNull();
    }

    [Fact]
    public void Abre_un_ciclo_nuevo_el_consentimiento_anterior_ya_no_vale_y_las_rutas_se_conservan()
    {
        var v = Pendiente();
        v.TieneConsentimientoManualVigente.Should().BeTrue();

        v.RechazarRevisionManual(Guid.NewGuid(), ManualRejectionReasons.RostroNoCoincide, Now, HashNuevo);

        v.TieneConsentimientoManualVigente.Should().BeFalse("la captura nueva exige aceptar el consentimiento de nuevo");
        v.FacePhotoPath.Should().Be("ruta-rostro-1");
        v.IdFrontPhotoPath.Should().Be("ruta-anverso-1");
        v.IdBackPhotoPath.Should().Be("ruta-reverso-1");
        v.SignatureImagePath.Should().Be("ruta-firma-1");
    }

    [Fact]
    public void La_captura_siguiente_limpia_motivo_y_revision_del_rechazo_previo()
    {
        var v = Pendiente();
        v.RechazarRevisionManual(Guid.NewGuid(), ManualRejectionReasons.CapturaFueraDeEncuadre, Now, HashNuevo);
        v.Status.Should().Be(BiometricEstados.Rechazado);
        v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, "203.0.113.9", Now.AddMinutes(10));
        v.ConsentIp.Should().Be("203.0.113.9", "el consentimiento nuevo sobrescribe al anterior");

        v.RegistrarCapturaManual("r2", "a2", "v2", "f2", new string('c', 64), Now.AddMinutes(11));

        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        v.RejectionReasonCode.Should().BeNull();
        v.ReviewedBy.Should().BeNull();
        v.ReviewedAt.Should().BeNull();
        v.FacePhotoPath.Should().Be("r2");
    }

    [Fact]
    public void AC5_Sin_tope_de_intentos_rechaza_captura_y_vuelve_a_rechazar_las_veces_que_haga_falta()
    {
        var v = Pendiente();
        var ahora = Now;
        var motivos = ManualRejectionReasons.Todos.Select(m => m.Code).ToList();

        for (var i = 0; i < 12; i++)
        {
            var hash = new string((char)('d' + (i % 3)), 63) + i.ToString("x");
            v.RechazarRevisionManual(Guid.NewGuid(), motivos[i % motivos.Count], ahora, hash);
            v.Status.Should().Be(BiometricEstados.Rechazado);
            v.RejectionReasonCode.Should().Be(motivos[i % motivos.Count]);

            ahora = ahora.AddMinutes(30);
            v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, null, ahora);
            v.RegistrarCapturaManual($"r{i}", $"a{i}", $"v{i}", $"f{i}", new string('e', 64), ahora.AddMinutes(1));
            v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
            ahora = ahora.AddMinutes(30);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("inventado")]
    [InlineData("IMAGEN_BORROSA")]
    [InlineData(" imagen_borrosa")]
    public void AC2_AC3_Motivo_ausente_o_fuera_de_la_lista_lanza_y_no_cambia_nada(string? motivo)
    {
        var v = Pendiente();

        var act = () => v.RechazarRevisionManual(Guid.NewGuid(), motivo!, Now, HashNuevo);

        act.Should().Throw<ArgumentException>();
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        v.TokenHash.Should().Be(HashViejo);
        v.RejectionReasonCode.Should().BeNull();
        v.ReviewedBy.Should().BeNull();
    }

    [Theory]
    [InlineData(BiometricEstados.ManualActivo, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Aprobado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Rechazado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.PendienteRevisionManual, BiometricProviders.Kyverum)]
    public void Fuera_de_pendiente_de_revision_lanza_RevisionManualNoPendiente(string status, string provider)
    {
        var v = Pendiente();
        v.Status = status;
        v.Provider = provider;

        var act = () => v.RechazarRevisionManual(Guid.NewGuid(), ManualRejectionReasons.ImagenBorrosa, Now, HashNuevo);

        act.Should().Throw<RevisionManualNoPendienteException>();
        v.RejectionReasonCode.Should().BeNull();
        v.TokenHash.Should().Be(HashViejo);
    }

    [Fact]
    public void Exige_revisor_y_un_hash_sha256_distinto_del_anterior()
    {
        var v = Pendiente();

        var sinRevisor = () => v.RechazarRevisionManual(Guid.Empty, ManualRejectionReasons.ImagenBorrosa, Now, HashNuevo);
        var hashCorto = () => v.RechazarRevisionManual(Guid.NewGuid(), ManualRejectionReasons.ImagenBorrosa, Now, "abc");
        var hashIgual = () => v.RechazarRevisionManual(Guid.NewGuid(), ManualRejectionReasons.ImagenBorrosa, Now, HashViejo);

        sinRevisor.Should().Throw<ArgumentException>();
        hashCorto.Should().Throw<ArgumentException>();
        hashIgual.Should().Throw<ArgumentException>();
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
    }

    [Fact]
    public void Una_fila_rechazada_con_enlace_vigente_acepta_consentimiento_y_captura_y_pasa_a_pendiente_de_revision()
    {
        var v = Pendiente();
        v.RechazarRevisionManual(Guid.NewGuid(), ManualRejectionReasons.ImagenBorrosa, Now, HashNuevo);

        v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, null, Now.AddMinutes(1));
        v.RegistrarCapturaManual("r", "a", "v", "f", new string('c', 64), Now.AddMinutes(2));

        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        v.RejectionReasonCode.Should().BeNull();
    }

    [Fact]
    public void Una_rechazada_con_el_enlace_vencido_no_admite_captura_pero_si_regenerar_el_enlace()
    {
        var v = Pendiente();
        v.RechazarRevisionManual(Guid.NewGuid(), ManualRejectionReasons.ImagenBorrosa, Now, HashNuevo);
        var tarde = Now.AddHours(25);

        v.EstadoSesionManual(tarde).Should().Be(ManualCaptureSessionState.Vencida);
        var capturar = () => v.RegistrarCapturaManual("r", "a", "v", "f", new string('c', 64), tarde);
        capturar.Should().Throw<ManualCaptureStateException>();

        v.PuedeRegenerarEnlaceManual.Should().BeTrue();
        v.RegenerarEnlaceManual(tarde, new string('d', 64));
        v.Status.Should().Be(BiometricEstados.Rechazado, "regenerar no cambia el estado");
        v.ExpiresAt.Should().Be(tarde.AddHours(24));
        v.EstadoSesionManual(tarde).Should().Be(ManualCaptureSessionState.Vigente);
    }

    [Fact]
    public void Una_rechazada_sin_motivo_o_de_otro_proveedor_no_espera_captura()
    {
        var sinMotivo = new ProcedureInstanceBiometricValidation
        {
            Provider = BiometricProviders.Manual, Status = BiometricEstados.Rechazado, ExpiresAt = Now.AddHours(5),
        };
        var kyverum = new ProcedureInstanceBiometricValidation
        {
            Provider = BiometricProviders.Kyverum, Status = BiometricEstados.Rechazado, RejectionReasonCode = "imagen_borrosa",
            ExpiresAt = Now.AddHours(5),
        };

        sinMotivo.EsperaCapturaManual.Should().BeFalse();
        sinMotivo.EstadoSesionManual(Now).Should().Be(ManualCaptureSessionState.EstadoInvalido);
        sinMotivo.PuedeRegenerarEnlaceManual.Should().BeFalse();
        kyverum.EstadoSesionManual(Now).Should().Be(ManualCaptureSessionState.NoManual);
        kyverum.PuedeRegenerarEnlaceManual.Should().BeFalse();
    }

    [Fact]
    public void La_activacion_normal_sobre_una_rechazada_manual_sigue_siendo_valida_y_pasa_a_manual_activo()
    {
        var v = Pendiente();
        v.RechazarRevisionManual(Guid.NewGuid(), ManualRejectionReasons.ImagenBorrosa, Now, HashNuevo);

        v.PuedeActivarFlujoManual(Now).Should().BeTrue();
        v.ActivarFlujoManual(Guid.NewGuid(), Now.AddHours(1), new string('e', 64));

        v.Status.Should().Be(BiometricEstados.ManualActivo);
        v.TokenHash.Should().Be(new string('e', 64));
    }

    [Fact]
    public void AC6_El_catalogo_es_la_lista_cerrada_de_6_motivos_con_etiquetas_en_espanol()
    {
        ManualRejectionReasons.Todos.Select(r => r.Code).Should().Equal(
            "imagen_borrosa",
            "rostro_no_coincide",
            "documento_ilegible_o_incompleto",
            "documento_no_corresponde",
            "firma_ilegible_o_no_corresponde",
            "captura_fuera_de_encuadre");
        ManualRejectionReasons.Todos.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Label));
        ManualRejectionReasons.Todos.Select(r => r.Label).Should().OnlyHaveUniqueItems();
        ManualRejectionReasons.Todos.Select(r => r.Code).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("imagen_borrosa", true)]
    [InlineData("captura_fuera_de_encuadre", true)]
    [InlineData("Imagen_Borrosa", false)]
    [InlineData("otro", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_es_exacto_sobre_la_lista_cerrada(string? code, bool esperado)
    {
        ManualRejectionReasons.IsValid(code).Should().Be(esperado);
        (ManualRejectionReasons.LabelFor(code) is not null).Should().Be(esperado);
    }
}
