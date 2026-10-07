using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #13289 y #13290 (Feature #13281 B, Épica #13202) — <c>/api/v1/public/manual-capture/{token}</c> con el pipeline HTTP real
/// y PostgreSQL real (la base local migrada). Todo ANÓNIMO y sin <c>X-Tenant-Id</c>: GET 200/404/410/409, consent 204/400 con
/// IP del servidor (nunca la del cuerpo) y constancia sobrescrita, y el envío de la captura (HU #13290) con un storage en memoria:
/// 200, 409 (consentimiento requerido / enlace usado), 410, 413, 415, 422, no sobrescritura entre intentos.
/// <para>Uso: <c>var (id, token) = await SeedAsync(); var r = await _client.GetAsync($"/api/v1/public/manual-capture/{token}")</c>.</para>
/// </summary>
public sealed class ManualCaptureEndpointTests : IClassFixture<ManualCaptureEndpointTests.Factory>, IDisposable
{
    private readonly Factory _factory;
    private readonly HttpClient _client;

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly List<Guid> _validaciones = [];
    private readonly List<Guid> _personas = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ManualCaptureEndpointTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        using var db = NewDb();
        db.Tenants.Add(new Tenant
        {
            Id = _tenant, Code = $"H13289-{Guid.NewGuid():N}"[..20], LegalName = "Compania HU13289", TaxId = TestNit.Unique(),
            TenantType = "RENTING", IsGroupParent = false, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    private static string Url(string token, string suffix = "") => $"/api/v1/public/manual-capture/{token}{suffix}";

    // ── GET: AC1, AC2, AC3 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_anonimo_sin_header_de_tenant_devuelve_la_vista_del_contrato_y_nada_mas()
    {
        var (_, token) = await SeedAsync();

        var response = await _client.GetAsync(Url(token), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = json.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["fullName", "documentType", "documentNumber", "productName", "expiresAt", "consentTextVersion"]);
        root.GetProperty("fullName").GetString().Should().Be("Persona HU13289");
        root.GetProperty("documentType").GetString().Should().Be("CC");
        root.GetProperty("consentTextVersion").GetString().Should().Be(ManualCaptureConsent.TextVersion);
        root.GetProperty("productName").ValueKind.Should().Be(JsonValueKind.Null, "una prevalidación no tiene producto");
        root.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(20), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Get_token_inexistente_o_vacio_responde_404_con_codigo()
    {
        var response = await _client.GetAsync(Url(BiometricToken.Generate()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("code").GetString().Should().Be("not_found");
        json.RootElement.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Get_enlace_vencido_responde_410_expirada()
    {
        var (_, token) = await SeedAsync(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var response = await _client.GetAsync(Url(token), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("expirada");
    }

    [Theory]
    [InlineData(BiometricEstados.PendienteRevisionManual)]
    [InlineData(BiometricEstados.Aprobado)]
    [InlineData(BiometricEstados.Rechazado)]
    public async Task Get_enlace_ya_usado_responde_409_estado_invalido(string estado)
    {
        var (_, token) = await SeedAsync(status: estado);

        var response = await _client.GetAsync(Url(token), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("estado_invalido");
    }

    [Fact]
    public async Task Get_token_de_un_enlace_regenerado_ya_no_existe()
    {
        var (id, viejo) = await SeedAsync();
        var nuevo = BiometricToken.Generate();
        await using (var db = NewDb())
        {
            await db.ProcedureInstanceBiometricValidations.Where(v => v.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.TokenHash, BiometricToken.Hash(nuevo)), Ct);
        }

        (await _client.GetAsync(Url(viejo), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync(Url(nuevo), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_token_de_otro_proveedor_responde_404()
    {
        var (_, token) = await SeedAsync(provider: BiometricProviders.Mock, status: BiometricEstados.Enviado);

        (await _client.GetAsync(Url(token), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST consent: AC4 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Consent_204_guarda_la_IP_del_servidor_y_no_la_del_cuerpo_y_audita_sin_IP()
    {
        var (id, token) = await SeedAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(token, "/consent"))
        {
            Content = JsonContent.Create(new
            {
                accepted = true,
                textVersion = ManualCaptureConsent.TextVersion,
                ip = "10.9.9.9",
                consentAt = "2001-01-01T00:00:00Z",
            }),
        };
        request.Headers.Add("X-Forwarded-For", "203.0.113.7, 10.0.0.1");

        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        await using var db = NewDb();
        var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.ConsentIp.Should().Be("203.0.113.7");
        v.ConsentAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        v.ConsentTextVersion.Should().Be(ManualCaptureConsent.TextVersion);
        v.Status.Should().Be(BiometricEstados.ManualActivo);

        var audit = await db.IdentityValidationAudits.AsNoTracking().Where(a => a.ValidationId == id).ToListAsync(Ct);
        var evento = audit.Should().ContainSingle().Which;
        evento.Stage.Should().Be(IdentityValidationAuditStages.ManualConsentimiento);
        $"{evento.Message}{evento.Detail}".Should().NotContain("203.0.113.7").And.NotContain("10.9.9.9");
    }

    [Fact]
    public async Task Consent_sin_IP_valida_no_inventa_una_y_sobrescribe_la_constancia_del_ciclo_previo()
    {
        // El TestServer no trae IP de conexión: sin cabecera X-Forwarded-For válida la columna queda nula (en un servidor
        // real cae a la IP de la conexión). Lo que importa aquí: nada de texto libre y la IP vieja no sobrevive.
        var (id, token) = await SeedAsync();
        await using (var db = NewDb())
        {
            await db.ProcedureInstanceBiometricValidations.Where(v => v.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(v => v.ConsentAt, DateTimeOffset.UtcNow.AddDays(-9))
                .SetProperty(v => v.ConsentIp, "198.51.100.1")
                .SetProperty(v => v.ConsentTextVersion, "version-vieja"), Ct);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Url(token, "/consent"))
        {
            Content = JsonContent.Create(new { accepted = true, textVersion = ManualCaptureConsent.TextVersion }),
        };
        request.Headers.Add("X-Forwarded-For", "<script>no-es-una-ip</script>");
        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var check = NewDb();
        var v = await check.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.ConsentIp.Should().BeNull();
        v.ConsentAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        v.ConsentTextVersion.Should().Be(ManualCaptureConsent.TextVersion);
    }

    [Fact]
    public async Task Consent_con_accepted_false_responde_400_y_no_guarda()
    {
        var (id, token) = await SeedAsync();

        var response = await _client.PostAsJsonAsync(
            Url(token, "/consent"), new { accepted = false, textVersion = ManualCaptureConsent.TextVersion }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("consentimiento_no_aceptado");
        await using var db = NewDb();
        (await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct)).ConsentAt.Should().BeNull();
        (await db.IdentityValidationAudits.AsNoTracking().AnyAsync(a => a.ValidationId == id, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Consent_con_version_incorrecta_responde_400()
    {
        var (_, token) = await SeedAsync();

        var response = await _client.PostAsJsonAsync(Url(token, "/consent"), new { accepted = true, textVersion = "x" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("version_texto_invalida");
    }

    [Fact]
    public async Task Consent_404_410_409_segun_el_estado_del_enlace()
    {
        (await Consent(BiometricToken.Generate())).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (_, vencido) = await SeedAsync(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        (await Consent(vencido)).StatusCode.Should().Be(HttpStatusCode.Gone);

        var (_, usado) = await SeedAsync(status: BiometricEstados.PendienteRevisionManual);
        (await Consent(usado)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── POST submit (HU #13290) ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_200_guarda_las_4_imagenes_pasa_a_revision_y_consume_el_enlace()
    {
        var (id, token) = await SeedAsync(consentAt: DateTimeOffset.UtcNow.AddHours(-1));
        var firma = Png(120);

        var response = await Submit(token, Form(firma: firma));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)))
            json.RootElement.GetProperty("status").GetString().Should().Be("pendiente_revision_manual");

        await using var db = NewDb();
        var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        v.FacePhotoPath.Should().NotBeNullOrWhiteSpace();
        v.IdFrontPhotoPath.Should().NotBeNullOrWhiteSpace();
        v.IdBackPhotoPath.Should().NotBeNullOrWhiteSpace();
        v.SignatureImagePath.Should().NotBeNullOrWhiteSpace();
        v.SignatureImageSha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(firma)));
        _factory.Storage.Saved.Where(s => s.Key == id).Should().HaveCount(4, "standalone: la clave es el id de la validación, no Guid.Empty");

        var audit = await db.IdentityValidationAudits.AsNoTracking().Where(a => a.ValidationId == id).ToListAsync(Ct);
        var evento = audit.Should().ContainSingle(a => a.Stage == IdentityValidationAuditStages.ManualCapturaRecibida).Which;
        evento.Detail.Should().Contain(v.SignatureImageSha256);

        // Enlace consumido: el segundo envío y la consulta ya responden 409 y no guardan nada.
        var segundo = await Submit(token, Form());
        segundo.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await segundo.Content.ReadAsStringAsync(Ct)).Should().Contain("estado_invalido");
        (await _client.GetAsync(Url(token), Ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        _factory.Storage.Saved.Where(s => s.Key == id).Should().HaveCount(4);
    }

    [Fact]
    public async Task Submit_simultaneo_con_el_mismo_token_solo_consume_el_enlace_una_vez()
    {
        var (id, token) = await SeedAsync(consentAt: DateTimeOffset.UtcNow.AddHours(-1));

        var respuestas = await Task.WhenAll(Submit(token, Form()), Submit(token, Form()), Submit(token, Form()));

        respuestas.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(2);
        await using var db = NewDb();
        var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        // Las rutas de la fila son las del envío ganador (4 de las subidas); ningún perdedor las pisó.
        _factory.Storage.Saved.Where(s => s.Key == id).Select(s => s.Path)
            .Should().Contain([v.FacePhotoPath!, v.IdFrontPhotoPath!, v.IdBackPhotoPath!, v.SignatureImagePath!]);
        (await db.IdentityValidationAudits.AsNoTracking()
            .CountAsync(a => a.ValidationId == id && a.Stage == IdentityValidationAuditStages.ManualCapturaRecibida, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Submit_sin_consentimiento_o_con_el_de_un_ciclo_anterior_409_y_no_guarda()
    {
        var (sinId, sin) = await SeedAsync();
        var (viejoId, viejo) = await SeedAsync(consentAt: DateTimeOffset.UtcNow.AddHours(-9)); // activada hace 4 h

        foreach (var (id, token) in new[] { (sinId, sin), (viejoId, viejo) })
        {
            var response = await Submit(token, Form());

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("consentimiento_requerido");
            _factory.Storage.Saved.Should().NotContain(s => s.Key == id);
            await using var db = NewDb();
            (await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct)).Status
                .Should().Be(BiometricEstados.ManualActivo);
        }
    }

    [Fact]
    public async Task Submit_sin_firma_422_y_el_estado_no_cambia()
    {
        var (id, token) = await SeedAsync(consentAt: DateTimeOffset.UtcNow.AddHours(-1));

        var response = await Submit(token, Form(omitir: "firma"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("firma_requerida");
        _factory.Storage.Saved.Should().NotContain(s => s.Key == id);
        await using var db = NewDb();
        (await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct)).Status
            .Should().Be(BiometricEstados.ManualActivo);
    }

    [Fact]
    public async Task Submit_sin_una_foto_422_archivo_requerido()
    {
        var (_, token) = await SeedAsync(consentAt: DateTimeOffset.UtcNow.AddHours(-1));

        var response = await Submit(token, Form(omitir: "reverso"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("archivo_requerido");
    }

    [Fact]
    public async Task Submit_con_foto_mayor_al_maximo_413_y_no_guarda()
    {
        var (id, token) = await SeedAsync(consentAt: DateTimeOffset.UtcNow.AddHours(-1));

        var response = await Submit(token, Form(rostro: Jpeg((int)ManualCaptureImages.MaxImageBytes + 1)));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("archivo_demasiado_grande");
        _factory.Storage.Saved.Should().NotContain(s => s.Key == id);
    }

    [Fact]
    public async Task Submit_con_contenido_que_no_es_imagen_415_aunque_diga_image_png()
    {
        var (id, token) = await SeedAsync(consentAt: DateTimeOffset.UtcNow.AddHours(-1));
        var falso = "MZ esto es un ejecutable disfrazado de foto"u8.ToArray();

        var response = await Submit(token, Form(anverso: falso));

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("tipo_no_soportado");
        _factory.Storage.Saved.Should().NotContain(s => s.Key == id);
    }

    [Fact]
    public async Task Submit_con_firma_jpeg_415_porque_la_firma_es_png()
    {
        var (_, token) = await SeedAsync(consentAt: DateTimeOffset.UtcNow.AddHours(-1));

        var response = await Submit(token, Form(firma: Jpeg(64)));

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Submit_404_410_409_segun_el_estado_del_enlace()
    {
        (await Submit(BiometricToken.Generate(), Form())).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (_, vencido) = await SeedAsync(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1), consentAt: DateTimeOffset.UtcNow.AddHours(-1));
        (await Submit(vencido, Form())).StatusCode.Should().Be(HttpStatusCode.Gone);

        var (_, aprobado) = await SeedAsync(status: BiometricEstados.Aprobado);
        (await Submit(aprobado, Form())).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Submit_tras_un_rechazo_y_reactivacion_no_sobrescribe_las_imagenes_del_intento_anterior()
    {
        var (id, token) = await SeedAsync(consentAt: DateTimeOffset.UtcNow.AddHours(-1));
        (await Submit(token, Form())).StatusCode.Should().Be(HttpStatusCode.OK);
        string[] primeras;
        await using (var db = NewDb())
        {
            var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
            primeras = [v.FacePhotoPath!, v.IdFrontPhotoPath!, v.IdBackPhotoPath!, v.SignatureImagePath!];

            // Rechazado (C4) y reactivado con enlace nuevo (A5/A2): activación y consentimiento nuevos.
            var nuevo = BiometricToken.Generate();
            var ahora = DateTimeOffset.UtcNow;
            await db.ProcedureInstanceBiometricValidations.Where(x => x.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, BiometricEstados.ManualActivo)
                .SetProperty(x => x.TokenHash, BiometricToken.Hash(nuevo))
                .SetProperty(x => x.ExpiresAt, ahora.AddHours(24))
                .SetProperty(x => x.ManualActivatedAt, ahora.AddSeconds(-30))
                .SetProperty(x => x.ConsentAt, ahora.AddSeconds(-20)), Ct);
            token = nuevo;
        }

        (await Submit(token, Form())).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var check = NewDb();
        var despues = await check.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        string[] segundas = [despues.FacePhotoPath!, despues.IdFrontPhotoPath!, despues.IdBackPhotoPath!, despues.SignatureImagePath!];
        segundas.Should().NotIntersectWith(primeras);
        _factory.Storage.Deleted.Should().NotIntersectWith(primeras, "las imágenes del intento previo se conservan");
        _factory.Storage.Saved.Where(s => s.Key == id).Should().HaveCount(8);
        var eventos = await check.IdentityValidationAudits.AsNoTracking()
            .Where(a => a.ValidationId == id && a.Stage == IdentityValidationAuditStages.ManualCapturaRecibida)
            .OrderBy(a => a.OccurredAt).ToListAsync(Ct);
        eventos.Should().HaveCount(2);
        foreach (var previa in primeras)
            eventos[1].Detail.Should().Contain(previa, "las rutas del intento anterior quedan en la auditoría");
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> Submit(string token, MultipartFormDataContent form) =>
        _client.PostAsync(Url(token, "/submit"), form, Ct);

    /// <summary>Multipart con los 4 campos por defecto (bytes sintéticos); <paramref name="omitir"/> quita un campo.</summary>
    private static MultipartFormDataContent Form(
        byte[]? rostro = null, byte[]? anverso = null, byte[]? reverso = null, byte[]? firma = null, string? omitir = null)
    {
        var form = new MultipartFormDataContent();
        void Add(string campo, byte[] bytes, string tipo)
        {
            if (campo == omitir)
                return;
            var contenido = new ByteArrayContent(bytes);
            contenido.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(tipo);
            form.Add(contenido, campo, $"{campo}.bin");
        }

        Add("rostro", rostro ?? Jpeg(64), "image/jpeg");
        Add("anverso", anverso ?? Png(64), "image/png");
        Add("reverso", reverso ?? Webp(64), "image/webp");
        Add("firma", firma ?? Png(48), "image/png");
        return form;
    }

    // Imágenes sintéticas de bytes mínimos válidos (cabecera + relleno): nunca fotos reales.
    private static byte[] Jpeg(int length) => Pad([0xFF, 0xD8, 0xFF, 0xE0], length);

    private static byte[] Png(int length) => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], length);

    private static byte[] Webp(int length) => Pad("RIFF\0\0\0\0WEBP"u8.ToArray(), length);

    private static byte[] Pad(byte[] head, int length)
    {
        var bytes = new byte[Math.Max(length, head.Length)];
        head.CopyTo(bytes, 0);
        // Relleno variable para que cada imagen tenga un hash distinto.
        Random.Shared.NextBytes(bytes.AsSpan(head.Length));
        return bytes;
    }

    private Task<HttpResponseMessage> Consent(string token) =>
        _client.PostAsJsonAsync(Url(token, "/consent"), new { accepted = true, textVersion = ManualCaptureConsent.TextVersion }, Ct);

    /// <summary>Siembra una validación manual standalone (por persona) y devuelve su id y el token en claro.</summary>
    private async Task<(Guid Id, string Token)> SeedAsync(
        string status = BiometricEstados.ManualActivo,
        string provider = BiometricProviders.Manual,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? consentAt = null)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var documento = $"9{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var persona = new Person
        {
            Id = Guid.NewGuid(), TenantId = _tenant, DocumentType = "CC", DocumentNumber = documento,
            FullName = "Persona HU13289", Email = $"p{documento}@example.test", PersonType = PersonTypes.Natural, CreatedAt = now,
        };
        db.Persons.Add(persona);
        await db.SaveChangesAsync(Ct);
        _personas.Add(persona.Id);

        var token = BiometricToken.Generate();
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = _tenant, ProcedureInstanceId = null, PersonId = persona.Id,
            Name = persona.FullName, DocumentType = "CC", DocumentNumber = documento, Email = persona.Email,
            RegisteredEmail = persona.Email, Status = status, Provider = provider,
            TokenHash = BiometricToken.Hash(token), ExpiresAt = expiresAt ?? now.AddHours(20),
            ManualActivatedAt = provider == BiometricProviders.Manual ? now.AddHours(-4) : null,
            ManualActivatedBy = provider == BiometricProviders.Manual ? Guid.NewGuid() : null,
            ConsentAt = consentAt, CreatedAt = now.AddDays(-1),
        };
        db.ProcedureInstanceBiometricValidations.Add(v);
        await db.SaveChangesAsync(Ct);
        _validaciones.Add(v.Id);
        return (v.Id, token);
    }

    private FlitDbContext NewDb() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    public void Dispose()
    {
        using var db = NewDb();
        db.IdentityValidationAudits.Where(a => a.TenantId == _tenant).ExecuteDelete();
        db.ProcedureInstanceBiometricValidations.Where(v => _validaciones.Contains(v.Id)).ExecuteDelete();
        db.Persons.Where(p => _personas.Contains(p.Id)).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _tenant).ExecuteDelete();
    }

    /// <summary>Host de pruebas con el storage de adjuntos en memoria (sin file-manager ni red).</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        public InMemoryStorage Storage { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAttachmentStorage>();
                services.AddSingleton<IAttachmentStorage>(Storage);
            });
    }

    public sealed record SavedFile(Guid Key, string Tipo, string Filename, string Path);

    public sealed class InMemoryStorage : IAttachmentStorage
    {
        private readonly object _gate = new();
        private readonly List<SavedFile> _saved = [];
        private readonly List<string> _deleted = [];

        public IReadOnlyList<SavedFile> Saved { get { lock (_gate) { return [.. _saved]; } } }

        public IReadOnlyList<string> Deleted { get { lock (_gate) { return [.. _deleted]; } } }

        public Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            var bytes = ms.ToArray();
            var path = $"fm-{Guid.NewGuid():N}";
            lock (_gate)
            {
                _saved.Add(new SavedFile(procedureInstanceId, tipo, originalFilename, path));
            }

            return Task.FromResult(new StoredFile(path, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength));
        }

        public void Delete(string storagePath)
        {
            lock (_gate)
            {
                _deleted.Add(storagePath);
            }
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
