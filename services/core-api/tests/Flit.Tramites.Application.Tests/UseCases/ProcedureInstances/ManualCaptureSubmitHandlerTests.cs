using System.Security.Cryptography;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13290 (Feature #13281 B, Épica #13202) — <see cref="EnviarCapturaManualHandler"/>: todos los errores del contrato
/// (404/410/409/422/413/415), nada guardado ante un error, clave de almacenamiento estable por validación (trámite y
/// standalone), sin sobrescribir imágenes de un intento previo, todo o nada si el storage falla y consumo atómico del enlace.
/// <para>Uso: <c>await Handler().HandleAsync(Command(), ct)</c> con imágenes sintéticas de <see cref="Img"/> (nunca fotos reales).</para>
/// </summary>
public sealed class ManualCaptureSubmitHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private const string Token = "token-de-prueba-del-flujo-manual";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();
    private readonly InMemoryStorage _storage = new();
    private DateTimeOffset _clock = Now;

    public ManualCaptureSubmitHandlerTests()
    {
        _repo.TryPersistManualCaptureAsync(Arg.Any<ProcedureInstanceBiometricValidation>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private EnviarCapturaManualHandler Handler() => new(_repo, _storage, _audit, new FixedTime(() => _clock));

    private ProcedureInstanceBiometricValidation Fila(
        string status = BiometricEstados.ManualActivo,
        string provider = BiometricProviders.Manual,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? consentAt = null,
        bool tramite = false,
        string? tramiteStatus = null)
    {
        var instanceId = tramite ? Guid.NewGuid() : (Guid?)null;
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = instanceId,
            ProcedureInstance = instanceId is null
                ? null
                : new ProcedureInstance { Id = instanceId.Value, Status = tramiteStatus ?? TramiteEstado.Entregado },
            PersonId = tramite ? null : Guid.NewGuid(),
            Name = "Ana Prueba",
            DocumentType = "CC",
            DocumentNumber = "900111222",
            Email = "ana@example.test",
            Status = status,
            Provider = provider,
            TokenHash = BiometricToken.Hash(Token),
            ExpiresAt = expiresAt ?? Now.AddHours(20),
            ManualActivatedAt = Now.AddHours(-4),
            ConsentAt = consentAt ?? Now.AddHours(-1),
            ConsentTextVersion = ManualCaptureConsent.TextVersion,
        };
        _repo.GetBiometricByTokenHashAsync(BiometricToken.Hash(Token), Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    private static EnviarCapturaManualCommand Command(
        ManualCaptureUpload? rostro = null,
        ManualCaptureUpload? anverso = null,
        ManualCaptureUpload? reverso = null,
        ManualCaptureUpload? firma = null,
        string token = Token,
        string? userAgent = "Mozilla/5.0 (pruebas)") =>
        new(token, rostro ?? Up(Img.Jpeg()), anverso ?? Up(Img.Png()), reverso ?? Up(Img.Webp()), firma ?? Up(Img.Png(50)), userAgent);

    private static ManualCaptureUpload Up(byte[] bytes) => new(new MemoryStream(bytes), bytes.Length);

    private void NadaGuardado()
    {
        _storage.Saved.Should().BeEmpty();
        _repo.DidNotReceive().TryPersistManualCaptureAsync(Arg.Any<ProcedureInstanceBiometricValidation>(), Arg.Any<CancellationToken>());
        _audit.DidNotReceive().LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
    }

    // ── AC1 — captura completa ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_CapturaCompleta_GuardaLas4ImagenesAsignaRutasYPasaARevision()
    {
        var v = Fila(tramite: true);
        var firma = Img.Png(80);
        IdentityValidationAuditEntry? entry = null;
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(e => entry = e), Arg.Any<CancellationToken>());

        var (result, error) = await Handler().HandleAsync(Command(firma: Up(firma)), Ct);

        error.Should().BeNull();
        result.Should().Be(new EnviarCapturaManualResult("pendiente_revision_manual"));
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        v.FacePhotoPath.Should().Be(_storage.Saved[0].Path);
        v.IdFrontPhotoPath.Should().Be(_storage.Saved[1].Path);
        v.IdBackPhotoPath.Should().Be(_storage.Saved[2].Path);
        v.SignatureImagePath.Should().Be(_storage.Saved[3].Path);
        v.SignatureImageSha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(firma)), "mismo cálculo que la rúbrica Kyverum (ADR-0054)");
        _storage.Saved.Select(s => s.Tipo).Should().Equal("manual_rostro", "manual_anverso", "manual_reverso", "identity_signature");
        _storage.Saved.Select(s => Path.GetExtension(s.Filename)).Should().Equal(".jpg", ".png", ".webp", ".png");
        await _repo.Received(1).TryPersistManualCaptureAsync(v, Arg.Any<CancellationToken>());

        entry.Should().NotBeNull();
        entry!.Stage.Should().Be("manual_captura_recibida");
        entry.ValidationId.Should().Be(v.Id);
        entry.Detail.Should().Contain(Convert.ToHexStringLower(SHA256.HashData(firma))).And.Contain("user_agent=Mozilla/5.0");
        $"{entry.Message} {entry.Detail}".Should().NotContain("900111222").And.NotContain("Ana").And.NotContain(Token);
    }

    // ── HU #13299 — repetición de la captura estando 'rechazado' (rechazo con enlace nuevo) ─────────────────

    private ProcedureInstanceBiometricValidation FilaRechazada(DateTimeOffset? expiresAt = null)
    {
        var v = Fila(BiometricEstados.Rechazado, expiresAt: expiresAt);
        v.RejectionReasonCode = "imagen_borrosa";
        v.ReviewedBy = Guid.NewGuid();
        v.ReviewedAt = Now.AddHours(-1);
        v.FacePhotoPath = "ruta-previa-rostro";
        return v;
    }

    [Fact]
    public async Task Rechazada_con_enlace_vigente_acepta_la_captura_pasa_a_revision_y_limpia_el_motivo_y_la_revision()
    {
        var v = FilaRechazada();
        v.ManualActivatedAt = Now.AddHours(-2);
        v.ConsentAt = Now.AddMinutes(-30); // consentimiento NUEVO del ciclo abierto por el rechazo

        var (result, error) = await Handler().HandleAsync(Command(), Ct);

        error.Should().BeNull();
        result.Should().Be(new EnviarCapturaManualResult("pendiente_revision_manual"));
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        v.RejectionReasonCode.Should().BeNull();
        v.ReviewedBy.Should().BeNull();
        v.ReviewedAt.Should().BeNull();
        v.FacePhotoPath.Should().Be(_storage.Saved[0].Path).And.NotBe("ruta-previa-rostro");
    }

    [Fact]
    public async Task Rechazada_sin_consentimiento_del_ciclo_nuevo_exige_consentimiento()
    {
        var v = FilaRechazada();
        v.ManualActivatedAt = Now.AddHours(-1);
        v.ConsentAt = Now.AddHours(-5); // el del ciclo anterior ya no vale

        (await Handler().HandleAsync(Command(), Ct)).Error.Should().Be(ManualCaptureErrors.ConsentimientoRequerido);
        NadaGuardado();
    }

    [Fact]
    public async Task Rechazada_con_enlace_vencido_responde_expirada()
    {
        FilaRechazada(expiresAt: Now.AddSeconds(-1));

        (await Handler().HandleAsync(Command(), Ct)).Error.Should().Be(ManualCaptureErrors.Expirada);
        NadaGuardado();
    }

    [Fact]
    public async Task ClaveDeAlmacenamiento_EsElIdDeLaValidacion_TantoEnTramiteComoEnStandalone()
    {
        var standalone = Fila();
        await Handler().HandleAsync(Command(), Ct);
        _storage.Saved.Should().OnlyContain(s => s.Key == standalone.Id && s.Key != Guid.Empty);

        _storage.Saved.Clear();
        var tramite = Fila(tramite: true);
        await Handler().HandleAsync(Command(), Ct);
        _storage.Saved.Should().OnlyContain(s => s.Key == tramite.Id && s.Key != tramite.ProcedureInstanceId);
    }

    // ── AC5 — repetición tras rechazo conserva lo anterior ──────────────────────────────────

    [Fact]
    public async Task AC5_UnIntentoNuevoNoSobrescribeNiBorraLoAnteriorYDejaSusRutasEnAuditoria()
    {
        var v = Fila();
        await Handler().HandleAsync(Command(), Ct);
        var primeras = _storage.Saved.Select(s => s.Path).ToList();
        var nombresPrimeros = _storage.Saved.Select(s => s.Filename).ToList();

        // El revisor rechazó y el Super Admin reactivó: nueva activación, nuevo consentimiento, intento posterior.
        v.Status = BiometricEstados.ManualActivo;
        v.ExpiresAt = Now.AddHours(48);
        v.ManualActivatedAt = Now.AddHours(1);
        v.ConsentAt = Now.AddHours(2);
        _clock = Now.AddHours(3);
        IdentityValidationAuditEntry? entry = null;
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(e => entry = e), Arg.Any<CancellationToken>());

        var (_, error) = await Handler().HandleAsync(Command(), Ct);

        error.Should().BeNull();
        var segundas = _storage.Saved.Skip(4).ToList();
        segundas.Select(s => s.Path).Should().NotIntersectWith(primeras);
        segundas.Select(s => s.Filename).Should().NotIntersectWith(nombresPrimeros, "el nombre lleva el sufijo UTC del intento");
        _storage.Deleted.Should().BeEmpty("las imágenes se conservan siempre");
        foreach (var previa in primeras)
            entry!.Detail.Should().Contain(previa);
        v.FacePhotoPath.Should().Be(segundas[0].Path);
    }

    // ── Errores de sesión (nada se guarda) ──────────────────────────────────────────────────

    [Fact]
    public async Task TokenInexistenteOVacio_NotFound()
    {
        _repo.GetBiometricByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        (await Handler().HandleAsync(Command(token: "x"), Ct)).Error.Should().Be(ManualCaptureErrors.NotFound);
        (await Handler().HandleAsync(Command(token: " "), Ct)).Error.Should().Be(ManualCaptureErrors.NotFound);
        NadaGuardado();
    }

    [Fact]
    public async Task EnlaceVencido_Expirada()
    {
        Fila(expiresAt: Now.AddSeconds(-1));

        (await Handler().HandleAsync(Command(), Ct)).Error.Should().Be(ManualCaptureErrors.Expirada);
        NadaGuardado();
    }

    [Theory]
    [InlineData(BiometricEstados.PendienteRevisionManual)]
    [InlineData(BiometricEstados.Aprobado)]
    [InlineData(BiometricEstados.Rechazado)]
    [InlineData(BiometricEstados.Expirado)]
    public async Task EnlaceYaUsado_EstadoInvalido(string estado)
    {
        Fila(estado);

        (await Handler().HandleAsync(Command(), Ct)).Error.Should().Be(ManualCaptureErrors.EstadoInvalido);
        NadaGuardado();
    }

    [Fact]
    public async Task SegundoEnvio_ConElMismoToken_EsEstadoInvalido()
    {
        Fila();
        (await Handler().HandleAsync(Command(), Ct)).Error.Should().BeNull();
        var guardadosPrimero = _storage.Saved.Count;

        (await Handler().HandleAsync(Command(), Ct)).Error.Should().Be(ManualCaptureErrors.EstadoInvalido);
        _storage.Saved.Should().HaveCount(guardadosPrimero);
    }

    [Fact]
    public async Task TramiteAnulado_EstadoInvalido()
    {
        Fila(tramite: true, tramiteStatus: TramiteEstado.Anulado);

        (await Handler().HandleAsync(Command(), Ct)).Error.Should().Be(ManualCaptureErrors.EstadoInvalido);
        NadaGuardado();
    }

    [Theory]
    [InlineData(BiometricProviders.Mock)]
    [InlineData(BiometricProviders.Kyverum)]
    public async Task OtroProveedor_NotFound(string proveedor)
    {
        Fila(BiometricEstados.Enviado, proveedor);

        (await Handler().HandleAsync(Command(), Ct)).Error.Should().Be(ManualCaptureErrors.NotFound);
        NadaGuardado();
    }

    // ── Consentimiento ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinConsentimiento_ConsentimientoRequerido()
    {
        var v = Fila();
        v.ConsentAt = null;

        (await Handler().HandleAsync(Command(), Ct)).Error.Should().Be(ManualCaptureErrors.ConsentimientoRequerido);
        NadaGuardado();
    }

    [Fact]
    public async Task ConsentimientoDeUnCicloAnterior_ConsentimientoRequerido()
    {
        var v = Fila();
        v.ConsentAt = v.ManualActivatedAt!.Value.AddMinutes(-1); // aceptó antes de la activación vigente

        (await Handler().HandleAsync(Command(), Ct)).Error.Should().Be(ManualCaptureErrors.ConsentimientoRequerido);
        NadaGuardado();
    }

    // ── AC2 / AC3 — presencia, tamaño y tipo ────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SinFirma_FirmaRequerida_YElEstadoNoCambia(bool vacia)
    {
        var v = Fila();
        var cmd = Command() with { Firma = vacia ? new ManualCaptureUpload(new MemoryStream(), 0) : null };

        (await Handler().HandleAsync(cmd, Ct)).Error.Should().Be(ManualCaptureErrors.FirmaRequerida);
        v.Status.Should().Be(BiometricEstados.ManualActivo);
        NadaGuardado();
    }

    [Theory]
    [InlineData("rostro")]
    [InlineData("anverso")]
    [InlineData("reverso")]
    public async Task FaltaUnaFoto_ArchivoRequerido(string campo)
    {
        Fila();
        var cmd = campo switch
        {
            "rostro" => Command() with { Rostro = null },
            "anverso" => Command() with { Anverso = null },
            _ => Command() with { Reverso = new ManualCaptureUpload(new MemoryStream(), 0) },
        };

        (await Handler().HandleAsync(cmd, Ct)).Error.Should().Be(ManualCaptureErrors.ArchivoRequerido);
        NadaGuardado();
    }

    [Fact]
    public async Task FotoMayorAlMaximoDeclarado_413_SinLeerNiGuardar()
    {
        Fila();
        var grande = new ManualCaptureUpload(new MemoryStream(Img.Jpeg()), ManualCaptureImages.MaxImageBytes + 1);

        (await Handler().HandleAsync(Command(rostro: grande), Ct)).Error.Should().Be(ManualCaptureErrors.ArchivoDemasiadoGrande);
        NadaGuardado();
    }

    [Fact]
    public async Task FirmaMayorAlMaximo_413()
    {
        Fila();
        var grande = new ManualCaptureUpload(new MemoryStream(Img.Png()), ManualCaptureImages.MaxSignatureBytes + 1);

        (await Handler().HandleAsync(Command(firma: grande), Ct)).Error.Should().Be(ManualCaptureErrors.ArchivoDemasiadoGrande);
        NadaGuardado();
    }

    [Fact]
    public async Task TamanoDeclaradoMentiroso_SeCortaPorElContenidoReal()
    {
        Fila();
        var real = Img.Png((int)ManualCaptureImages.MaxImageBytes + 10);
        var mentiroso = new ManualCaptureUpload(new MemoryStream(real), 100);

        (await Handler().HandleAsync(Command(anverso: mentiroso), Ct)).Error.Should().Be(ManualCaptureErrors.ArchivoDemasiadoGrande);
        NadaGuardado();
    }

    [Fact]
    public async Task ElLimiteExactoSeAcepta()
    {
        Fila();
        var justo = Img.Jpeg((int)ManualCaptureImages.MaxImageBytes);

        (await Handler().HandleAsync(Command(rostro: Up(justo)), Ct)).Error.Should().BeNull();
    }

    [Theory]
    [InlineData("rostro")]
    [InlineData("anverso")]
    [InlineData("reverso")]
    [InlineData("firma")]
    public async Task ContenidoQueNoEsImagen_415_AunqueSeaMarcadoComoPng(string campo)
    {
        Fila();
        var texto = "%PDF-1.7 esto no es una imagen, aunque el cliente diga image/png"u8.ToArray();
        var cmd = campo switch
        {
            "rostro" => Command(rostro: Up(texto)),
            "anverso" => Command(anverso: Up(texto)),
            "reverso" => Command(reverso: Up(texto)),
            _ => Command(firma: Up(texto)),
        };

        (await Handler().HandleAsync(cmd, Ct)).Error.Should().Be(ManualCaptureErrors.TipoNoSoportado);
        NadaGuardado();
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("webp")]
    public async Task LaFirmaDebeSerPng_415_ConOtroFormatoDeImagen(string formato)
    {
        Fila();
        var firma = formato == "jpeg" ? Img.Jpeg() : Img.Webp();

        (await Handler().HandleAsync(Command(firma: Up(firma)), Ct)).Error.Should().Be(ManualCaptureErrors.TipoNoSoportado);
        NadaGuardado();
    }

    [Fact]
    public async Task Gif_415_ComoFoto()
    {
        Fila();

        (await Handler().HandleAsync(Command(rostro: Up("GIF89a\0\0\0\0\0\0\0\0"u8.ToArray())), Ct)).Error
            .Should().Be(ManualCaptureErrors.TipoNoSoportado);
        NadaGuardado();
    }

    // ── Todo o nada ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SiElStorageFallaAMitad_SeLimpiaLoSubidoYLaFilaNoCambia()
    {
        var v = Fila();
        _storage.FailOnSave = 3; // rostro y anverso suben; el reverso falla

        var act = async () => await Handler().HandleAsync(Command(), Ct);

        await act.Should().ThrowAsync<IOException>();
        _storage.Deleted.Should().BeEquivalentTo(_storage.Saved.Select(s => s.Path));
        _storage.Saved.Should().HaveCount(2);
        v.Status.Should().Be(BiometricEstados.ManualActivo);
        v.FacePhotoPath.Should().BeNull();
        v.SignatureImagePath.Should().BeNull();
        await _repo.DidNotReceive().TryPersistManualCaptureAsync(Arg.Any<ProcedureInstanceBiometricValidation>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SiLaPersistenciaFalla_SeLimpianLas4ImagenesYSePropagaElError()
    {
        Fila();
        _repo.TryPersistManualCaptureAsync(Arg.Any<ProcedureInstanceBiometricValidation>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("bd caída"));

        var act = async () => await Handler().HandleAsync(Command(), Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _storage.Deleted.Should().HaveCount(4);
        await _audit.DidNotReceive().LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SiOtroEnvioConsumioElEnlaceAntes_EstadoInvalidoYSeLimpia()
    {
        Fila();
        _repo.TryPersistManualCaptureAsync(Arg.Any<ProcedureInstanceBiometricValidation>(), Arg.Any<CancellationToken>()).Returns(false);

        (await Handler().HandleAsync(Command(), Ct)).Error.Should().Be(ManualCaptureErrors.EstadoInvalido);
        _storage.Deleted.Should().HaveCount(4);
        await _audit.DidNotReceive().LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ElAgenteDeUsuarioSeLimpiaAntesDeAuditar()
    {
        Fila();
        IdentityValidationAuditEntry? entry = null;
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(e => entry = e), Arg.Any<CancellationToken>());

        await Handler().HandleAsync(Command(userAgent: "Agente\r\nInyectado\t" + new string('x', 400)), Ct);

        entry!.Detail.Should().NotContain("\r").And.NotContain("\n").And.NotContain("\t");
        entry.Detail!.Length.Should().BeLessThan(1000);
    }

    // ── Doubles ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Imágenes sintéticas de bytes mínimos válidos (solo cabecera + relleno): nunca fotos reales.</summary>
    private static class Img
    {
        public static byte[] Jpeg(int length = 64) => Pad([0xFF, 0xD8, 0xFF, 0xE0], length, tail: [0xFF, 0xD9]);

        public static byte[] Png(int length = 64) => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], length);

        public static byte[] Webp(int length = 64) => Pad("RIFF\0\0\0\0WEBP"u8.ToArray(), length);

        private static byte[] Pad(byte[] head, int length, byte[]? tail = null)
        {
            var bytes = new byte[Math.Max(length, head.Length + (tail?.Length ?? 0))];
            head.CopyTo(bytes, 0);
            tail?.CopyTo(bytes, bytes.Length - tail.Length);
            return bytes;
        }
    }

    private sealed record SavedFile(Guid Key, string Tipo, string Filename, string Path);

    private sealed class InMemoryStorage : IAttachmentStorage
    {
        public List<SavedFile> Saved { get; } = [];

        public List<string> Deleted { get; } = [];

        /// <summary>Número de guardado (1 = el primero) que debe fallar; 0 = ninguno.</summary>
        public int FailOnSave { get; set; }

        private int _calls;

        public Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            if (++_calls == FailOnSave)
                throw new IOException("storage caído");
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            var bytes = ms.ToArray();
            var path = $"fm-{Guid.NewGuid():N}";
            Saved.Add(new SavedFile(procedureInstanceId, tipo, originalFilename, path));
            return Task.FromResult(new StoredFile(path, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength));
        }

        public void Delete(string storagePath) => Deleted.Add(storagePath);

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTime(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }
}
