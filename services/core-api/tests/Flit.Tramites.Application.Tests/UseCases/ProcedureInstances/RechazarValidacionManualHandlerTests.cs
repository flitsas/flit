using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13299 (Feature #13282 C4, Épica #13202) — <see cref="RechazarValidacionManualHandler"/>: valida el motivo (lista cerrada),
/// rechaza solo desde <c>pendiente_revision_manual</c>, deja la fila en <c>rechazado</c> con un token nuevo de 24 h guardado como hash, envía el
/// correo con el motivo legible, audita <c>manual_rechazado</c> sin texto libre ni PII, y un fallo de correo no revierte el rechazo.
/// <para>Uso: <c>await handler.HandleAsync(new RechazarValidacionManualCommand(validationId, userId, "imagen_borrosa"), ct)</c>.</para>
/// </summary>
public sealed class RechazarValidacionManualHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();
    private readonly IManualCaptureLinkNotifier _notifier = Substitute.For<IManualCaptureLinkNotifier>();

    public RechazarValidacionManualHandlerTests()
    {
        _notifier.NotifyAsync(Arg.Any<ManualCaptureLink>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private RechazarValidacionManualHandler Handler() => new(_repo, _audit, _notifier, new FixedTime(Now));

    private ProcedureInstanceBiometricValidation Fila(
        string status = BiometricEstados.PendienteRevisionManual, string provider = BiometricProviders.Manual)
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            PartyRole = "vendedor",
            Name = "Persona de prueba",
            DocumentNumber = "900123456",
            Email = "persona@example.test",
            Status = status,
            Provider = provider,
            TokenHash = BiometricToken.Hash("token-viejo"),
            ExpiresAt = Now.AddHours(-2),
            ManualActivatedAt = Now.AddHours(-6),
            ConsentAt = Now.AddHours(-5),
            FacePhotoPath = "ruta-opaca-rostro",
        };
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    private static RechazarValidacionManualCommand Cmd(Guid id, string? motivo = "imagen_borrosa") => new(id, User, motivo);

    [Fact]
    public async Task AC1_AC4_Rechaza_registra_motivo_y_revisor_queda_rechazado_con_token_nuevo_de_24h()
    {
        var v = Fila();
        var hashViejo = v.TokenHash;
        ManualCaptureLink? enviado = null;
        _notifier.NotifyAsync(Arg.Do<ManualCaptureLink>(l => enviado = l), Arg.Any<CancellationToken>()).Returns(true);

        var (result, error) = await Handler().HandleAsync(Cmd(v.Id, "rostro_no_coincide"), Ct);

        error.Should().BeNull();
        result!.Status.Should().Be("rechazado");
        result.RejectionReasonCode.Should().Be("rostro_no_coincide");
        result.LinkExpiresAt.Should().Be(Now.AddHours(24));
        result.ReviewedAt.Should().Be(Now);
        result.EmailEnviado.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Rechazado);
        v.RejectionReasonCode.Should().Be("rostro_no_coincide");
        v.ReviewedBy.Should().Be(User);
        v.ReviewedAt.Should().Be(Now);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        enviado.Should().NotBeNull();
        v.TokenHash.Should().Be(BiometricToken.Hash(enviado!.Token)).And.NotBe(hashViejo).And.NotBe(enviado.Token);
        enviado.ExpiresAt.Should().Be(Now.AddHours(24));
    }

    [Fact]
    public async Task AC4_El_correo_lleva_el_motivo_en_texto_legible_y_el_destinatario_de_la_fila_una_sola_vez()
    {
        var v = Fila();

        await Handler().HandleAsync(Cmd(v.Id, "documento_ilegible_o_incompleto"), Ct);

        await _notifier.Received(1).NotifyAsync(
            Arg.Is<ManualCaptureLink>(l => l.RecipientEmail == "persona@example.test" && l.ValidationId == v.Id
                && l.TenantId == v.TenantId && l.RejectionReasonLabel == "Documento ilegible o incompleto"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Audita_manual_rechazado_con_el_codigo_sin_texto_libre_PII_ni_token()
    {
        var v = Fila();
        var entradas = new List<IdentityValidationAuditEntry>();
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(entradas.Add), Arg.Any<CancellationToken>());
        string? token = null;
        _notifier.NotifyAsync(Arg.Do<ManualCaptureLink>(l => token = l.Token), Arg.Any<CancellationToken>()).Returns(true);

        await Handler().HandleAsync(Cmd(v.Id, "firma_ilegible_o_no_corresponde"), Ct);

        var e = entradas.Should().ContainSingle().Subject;
        e.Stage.Should().Be(IdentityValidationAuditStages.ManualRechazado).And.Be("manual_rechazado");
        e.TenantId.Should().Be(v.TenantId);
        e.ValidationId.Should().Be(v.Id);
        e.Detail.Should().Contain(User.ToString()).And.Contain("motivo=firma_ilegible_o_no_corresponde");
        (e.Message + e.Detail).Should()
            .NotContain(token!).And.NotContain(v.TokenHash).And.NotContain("persona@example.test")
            .And.NotContain("900123456").And.NotContain("Persona de prueba").And.NotContain("ruta-opaca-rostro");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no_existe")]
    [InlineData("Imagen_Borrosa")]
    public async Task AC2_AC3_Motivo_ausente_o_fuera_de_la_lista_responde_motivo_invalido_y_no_cambia_nada(string? motivo)
    {
        var v = Fila();
        var hashAntes = v.TokenHash;

        var (result, error) = await Handler().HandleAsync(Cmd(v.Id, motivo), Ct);

        result.Should().BeNull();
        error.Should().Be(RechazarValidacionManualHandler.MotivoInvalido).And.Be("motivo_invalido");
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        v.TokenHash.Should().Be(hashAntes);
        v.RejectionReasonCode.Should().BeNull();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<ManualCaptureLink>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BiometricEstados.ManualActivo, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Rechazado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Aprobado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Expirado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.PendienteRevisionManual, BiometricProviders.Kyverum)]
    [InlineData(BiometricEstados.EnProceso, BiometricProviders.Kyverum)]
    public async Task Fuera_de_pendiente_de_revision_responde_estado_invalido_y_no_cambia_nada(string status, string provider)
    {
        var v = Fila(status, provider);
        var hashAntes = v.TokenHash;

        var (result, error) = await Handler().HandleAsync(Cmd(v.Id), Ct);

        result.Should().BeNull();
        error.Should().Be(RechazarValidacionManualHandler.EstadoInvalido).And.Be("estado_invalido");
        v.TokenHash.Should().Be(hashAntes);
        v.RejectionReasonCode.Should().BeNull();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<ManualCaptureLink>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validacion_inexistente_devuelve_not_found()
    {
        var (result, error) = await Handler().HandleAsync(Cmd(Guid.NewGuid()), Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Tramite_anulado_conserva_la_validacion_y_responde_tramite_inactivo()
    {
        var v = Fila();
        v.ProcedureInstance = new ProcedureInstance { Status = "revocado" };

        var (result, error) = await Handler().HandleAsync(Cmd(v.Id), Ct);

        result.Should().BeNull();
        error.Should().Be("tramite_inactivo");
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
    }

    [Fact]
    public async Task El_correo_no_sale_no_revierte_el_rechazo_audita_manual_correo_fallido_y_lo_informa()
    {
        var v = Fila();
        _notifier.NotifyAsync(Arg.Any<ManualCaptureLink>(), Arg.Any<CancellationToken>()).Returns(false);
        var entradas = new List<IdentityValidationAuditEntry>();
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(entradas.Add), Arg.Any<CancellationToken>());

        var (result, error) = await Handler().HandleAsync(Cmd(v.Id), Ct);

        error.Should().BeNull();
        result!.EmailEnviado.Should().BeFalse();
        result.Status.Should().Be("rechazado");
        v.RejectionReasonCode.Should().Be("imagen_borrosa");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        entradas.Select(e => e.Stage).Should().Equal(
            IdentityValidationAuditStages.ManualRechazado, IdentityValidationAuditStages.ManualCorreoFallido);
    }

    [Fact]
    public async Task El_notificador_lanza_no_revierte_el_rechazo()
    {
        var v = Fila();
        _notifier.NotifyAsync(Arg.Any<ManualCaptureLink>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("smtp caido"));

        var (result, error) = await Handler().HandleAsync(Cmd(v.Id), Ct);

        error.Should().BeNull();
        result!.EmailEnviado.Should().BeFalse();
        v.Status.Should().Be(BiometricEstados.Rechazado);
    }

    [Fact]
    public async Task AC5_Rechazos_consecutivos_emiten_siempre_un_enlace_nuevo_sin_tope()
    {
        var v = Fila();
        var tokens = new List<string>();
        _notifier.NotifyAsync(Arg.Do<ManualCaptureLink>(l => tokens.Add(l.Token)), Arg.Any<CancellationToken>()).Returns(true);

        for (var i = 0; i < 6; i++)
        {
            var (result, error) = await Handler().HandleAsync(Cmd(v.Id, ManualRejectionReasons.Todos[i].Code), Ct);
            error.Should().BeNull();
            result!.RejectionReasonCode.Should().Be(ManualRejectionReasons.Todos[i].Code);

            // El cliente repite la captura: consentimiento del ciclo nuevo + envío, y vuelve a pendiente de revisión.
            v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, null, Now);
            v.RegistrarCapturaManual($"r{i}", $"a{i}", $"v{i}", $"f{i}", new string('9', 64), Now);
        }

        tokens.Should().HaveCount(6).And.OnlyHaveUniqueItems();
        v.TokenHash.Should().Be(BiometricToken.Hash(tokens[5]));
        await _repo.Received(6).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
