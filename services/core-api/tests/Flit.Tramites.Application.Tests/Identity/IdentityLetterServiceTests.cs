using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// HU #14301 (Feature #14300, Épica #14271) — <see cref="IdentityLetterService"/>: la carta de identidad de FLIT se genera UNA sola
/// vez al aprobar una validación MANUAL, queda en columnas propias (<c>letter_*</c>, sin tocar <c>certificate_hash</c> de Kyverum),
/// una falla no revierte la aprobación y se reintenta, y una aprobación de Kyverum no genera carta. Datos ficticios.
/// </summary>
public sealed class IdentityLetterServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 15, 30, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly IIdentityLetterPdfGenerator _generator = Substitute.For<IIdentityLetterPdfGenerator>();
    private readonly ICompaniaRadicadoraDirectory _companies = Substitute.For<ICompaniaRadicadoraDirectory>();
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal)
    {
        ["ruta/rostro"] = [1, 1, 1],
        ["ruta/anverso"] = [2, 2, 2],
        ["ruta/reverso"] = [3, 3, 3],
        ["ruta/firma"] = [4, 4, 4],
    };

    private readonly byte[] _pdf = Encoding.ASCII.GetBytes("%PDF-prueba");
    private IdentityLetterData? _lastData;
    private int _generaciones;

    public IdentityLetterServiceTests()
    {
        _storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => _files.TryGetValue(ci.Arg<string>(), out var b) ? new MemoryStream(b) : null);
        _storage.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ci => new StoredFile($"carta/{ci.ArgAt<Guid>(0):N}", new string('a', 64), 11));
        _generator.Generate(Arg.Any<IdentityLetterData>()).Returns(ci =>
        {
            _lastData = ci.Arg<IdentityLetterData>();
            _generaciones++;
            return _pdf;
        });
        _companies.GetRazonSocialAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns("Compañía de prueba SAS");
    }

    private IdentityLetterService Service(DateTimeOffset? now = null) =>
        new(_repo, _storage, _generator, _companies, new FixedTime(now ?? Now));

    private ProcedureInstanceBiometricValidation Manual(
        Guid? instanceId = null, string provider = BiometricProviders.Manual, string origin = BiometricApprovalOrigins.Manual)
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = instanceId,
            PersonId = Guid.NewGuid(),
            PartyRole = "comprador",
            Name = "PERSONA DE PRUEBA UNO",
            DocumentType = "CC",
            DocumentNumber = "1000000001",
            Email = "persona@example.test",
            Provider = provider,
            Status = BiometricEstados.Aprobado,
            ApprovalOrigin = origin,
            TokenHash = new string('c', 64),
            FacePhotoPath = "ruta/rostro",
            IdFrontPhotoPath = "ruta/anverso",
            IdBackPhotoPath = "ruta/reverso",
            SignatureImagePath = "ruta/firma",
            ConsentAt = new DateTimeOffset(2026, 11, 5, 14, 0, 0, TimeSpan.Zero),
            ConsentIp = "203.0.113.7",
            ConsentTextVersion = "manual-ley1581-v2",
            ValidatedAt = new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero),
            ValidUntil = new DateTimeOffset(2026, 12, 5, 5, 0, 0, TimeSpan.Zero),
            ReviewedBy = Guid.NewGuid(),
            ReviewedAt = new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero),
            CertificateHash = null,
        };
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    [Fact]
    public async Task AC1_Genera_la_carta_y_guarda_serie_hash_y_ruta_en_la_validacion()
    {
        var v = Manual(instanceId: Guid.NewGuid());

        var outcome = await Service().EnsureForValidationAsync(v.Id, Ct);

        outcome.Should().Be(IdentityLetterOutcome.Generated);
        v.CartaPath.Should().Be($"carta/{v.Id:N}");
        v.CartaSerie.Should().MatchRegex("^FL-[0-9A-F]{8}-[0-9A-F]{6}$");
        v.CartaSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        v.CartaPdfSha256.Should().Be(Convert.ToHexString(SHA256.HashData(_pdf)).ToLowerInvariant());
        v.CartaGeneratedAt.Should().Be(Now);
        v.TieneCartaIdentidad.Should().BeTrue();
        await _storage.Received(1).SaveAsync(
            v.Id, IdentityLetterService.TipoCarta, "carta-identidad.pdf", Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_Los_datos_de_la_carta_salen_de_la_validacion()
    {
        var instance = Guid.NewGuid();
        var v = Manual(instanceId: instance);

        await Service().EnsureForValidationAsync(v.Id, Ct);

        _lastData.Should().NotBeNull();
        _lastData!.Radicado.Should().Be(IdentityLetterService.Radicado(v.Id));
        _lastData.CompanyName.Should().Be("Compañía de prueba SAS");
        _lastData.Reference.Should().Be(instance.ToString("D"));
        _lastData.RoleLabel.Should().Be("COMPRADOR");
        _lastData.RegisteredName.Should().Be("PERSONA DE PRUEBA UNO");
        _lastData.ConsentIp.Should().Be("203.0.113.7");
        _lastData.ConsentTextVersion.Should().Be("manual-ley1581-v2");
        _lastData.Texts.Should().NotBeNull();
        _lastData.Selfie.Should().Equal(1, 1, 1);
        _lastData.Signature.Should().Equal(4, 4, 4);
        _lastData.Serie.Should().Be(v.CartaSerie);
        _lastData.EvidenceSha256.Should().Be(v.CartaSha256);
    }

    [Fact]
    public async Task La_carta_pertenece_a_la_validacion_tambien_sin_tramite_y_la_referencia_es_la_validacion()
    {
        var v = Manual(instanceId: null);
        v.PartyRole = null;

        var outcome = await Service().EnsureForValidationAsync(v.Id, Ct);

        outcome.Should().Be(IdentityLetterOutcome.Generated);
        v.ProcedureInstanceId.Should().BeNull();
        v.CartaPath.Should().NotBeNullOrEmpty();
        _lastData!.Reference.Should().Be(v.Id.ToString("D"));
        _lastData.RoleLabel.Should().Be("TITULAR");
    }

    [Fact]
    public async Task AC3_Si_la_generacion_corre_de_nuevo_la_carta_la_serie_y_el_hash_no_cambian()
    {
        var v = Manual();
        var service = Service();
        await service.EnsureForValidationAsync(v.Id, Ct);
        var (serie, sha, pdfSha, path, at) = (v.CartaSerie, v.CartaSha256, v.CartaPdfSha256, v.CartaPath, v.CartaGeneratedAt);

        var segunda = await Service(Now.AddHours(5)).EnsureForValidationAsync(v.Id, Ct);

        segunda.Should().Be(IdentityLetterOutcome.AlreadyPresent);
        _generaciones.Should().Be(1, "una sola generación");
        (v.CartaSerie, v.CartaSha256, v.CartaPdfSha256, v.CartaPath, v.CartaGeneratedAt).Should().Be((serie, sha, pdfSha, path, at));
        await _storage.Received(1).SaveAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AC3_La_serie_y_el_hash_son_estables_con_la_misma_evidencia_y_cambian_si_cambia_una_imagen()
    {
        var v = Manual();
        byte[] a = [1], b = [2], c = [3], d = [4];

        var h1 = IdentityLetterService.EvidenceSha256(v, a, b, c, d);
        var h2 = IdentityLetterService.EvidenceSha256(v, a, b, c, d);
        var h3 = IdentityLetterService.EvidenceSha256(v, [9], b, c, d);

        h1.Should().Be(h2);
        h3.Should().NotBe(h1);
        IdentityLetterService.Serie(h1).Should().Be(IdentityLetterService.Serie(h2));
    }

    [Fact]
    public void El_radicado_es_CERT_mas_12_hex_en_mayusculas_y_estable()
    {
        var id = Guid.NewGuid();

        var r = IdentityLetterService.Radicado(id);

        r.Should().MatchRegex("^CERT-[0-9A-F]{12}$");
        IdentityLetterService.Radicado(id).Should().Be(r);
        IdentityLetterService.Radicado(Guid.NewGuid()).Should().NotBe(r);
    }

    [Fact]
    public async Task AC4_Si_el_storage_falla_la_validacion_sigue_aprobada_sin_carta_y_el_reintento_la_genera()
    {
        var v = Manual();
        _storage.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StoredFile>(new IOException("storage caído")));

        var act = () => Service().EnsureForValidationAsync(v.Id, Ct);

        await act.Should().ThrowAsync<IdentityLetterGenerationException>();
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ApprovalOrigin.Should().Be(BiometricApprovalOrigins.Manual);
        v.TieneCartaIdentidad.Should().BeFalse();

        _storage.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("carta/ok", new string('a', 64), 11));
        var reintento = await Service().EnsureForValidationAsync(v.Id, Ct);

        reintento.Should().Be(IdentityLetterOutcome.Generated);
        v.CartaPath.Should().Be("carta/ok");
        v.Status.Should().Be(BiometricEstados.Aprobado);
    }

    [Fact]
    public async Task AC4_Si_falta_una_imagen_en_el_storage_falla_para_reintentar_y_no_guarda_nada()
    {
        var v = Manual();
        _files.Remove("ruta/reverso");

        var act = () => Service().EnsureForValidationAsync(v.Id, Ct);

        await act.Should().ThrowAsync<IdentityLetterGenerationException>();
        v.TieneCartaIdentidad.Should().BeFalse();
        _generaciones.Should().Be(0);
        await _storage.DidNotReceive().SaveAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_Si_falla_el_guardado_de_la_fila_se_borra_el_pdf_huerfano_y_se_limpian_los_campos()
    {
        var v = Manual();
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(new InvalidOperationException("bd")));

        var act = () => Service().EnsureForValidationAsync(v.Id, Ct);

        await act.Should().ThrowAsync<IdentityLetterGenerationException>();
        _storage.Received(1).Delete($"carta/{v.Id:N}");
        v.TieneCartaIdentidad.Should().BeFalse();
        v.CartaSerie.Should().BeNull();
        v.CartaSha256.Should().BeNull();
        v.CartaGeneratedAt.Should().BeNull();
    }

    [Fact]
    public async Task AC5_Una_validacion_aprobada_por_Kyverum_no_genera_carta_de_FLIT()
    {
        var v = Manual(provider: BiometricProviders.Kyverum, origin: BiometricApprovalOrigins.Automatica);
        v.CertificateHash = "KV-SERIE-DE-KYVERUM";

        var outcome = await Service().EnsureForValidationAsync(v.Id, Ct);

        outcome.Should().Be(IdentityLetterOutcome.Skipped);
        _generaciones.Should().Be(0);
        v.TieneCartaIdentidad.Should().BeFalse();
        v.CertificateHash.Should().Be("KV-SERIE-DE-KYVERUM", "la serie de Kyverum que lee el sello del FUR no se toca");
        await _storage.DidNotReceive().SaveAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BiometricEstados.PendienteRevisionManual)]
    [InlineData(BiometricEstados.Rechazado)]
    [InlineData(BiometricEstados.ManualActivo)]
    public async Task Una_validacion_manual_no_aprobada_no_genera_carta(string estado)
    {
        var v = Manual();
        v.Status = estado;

        var outcome = await Service().EnsureForValidationAsync(v.Id, Ct);

        outcome.Should().Be(IdentityLetterOutcome.Skipped);
        _generaciones.Should().Be(0);
    }

    [Fact]
    public async Task Una_validacion_inexistente_se_omite()
    {
        var outcome = await Service().EnsureForValidationAsync(Guid.NewGuid(), Ct);

        outcome.Should().Be(IdentityLetterOutcome.Skipped);
    }

    [Fact]
    public void RegistrarCartaIdentidad_rechaza_una_validacion_de_Kyverum()
    {
        var kyverum = Manual(provider: BiometricProviders.Kyverum, origin: BiometricApprovalOrigins.Automatica);

        var act = () => kyverum.RegistrarCartaIdentidad("FL-X", new string('a', 64), new string('b', 64), "ruta", Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RegistrarCartaIdentidad_no_pisa_una_carta_existente()
    {
        var v = Manual();
        v.RegistrarCartaIdentidad("FL-1", new string('a', 64), new string('b', 64), "ruta-1", Now).Should().BeTrue();

        var otra = v.RegistrarCartaIdentidad("FL-2", new string('c', 64), new string('d', 64), "ruta-2", Now.AddDays(1));

        otra.Should().BeFalse();
        v.CartaSerie.Should().Be("FL-1");
        v.CartaPath.Should().Be("ruta-1");
        v.CartaSha256.Should().Be(new string('a', 64));
    }

    [Fact]
    public async Task AC2_Los_datos_de_la_carta_no_incluyen_revisor_score_QR_nacimiento_ni_sexo()
    {
        var v = Manual();

        await Service().EnsureForValidationAsync(v.Id, Ct);

        // El revisor (Super Admin) no viaja en los datos de la carta: ni su id ni su nombre pueden imprimirse.
        var todo = string.Join('|', typeof(IdentityLetterData).GetProperties().Select(p => p.Name));
        todo.Should().NotContainEquivalentOf("review").And.NotContainEquivalentOf("admin");
        Regex.IsMatch(todo, "(?i)score|qr|birth|nacimiento|sex").Should().BeFalse();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
