using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Security;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// Épica #13216 (HU #13379, ADR-0070 D4/D7/D8) — contrato HTTP de las rutas neutras del lote: <c>GET actual</c>,
/// <c>GET {loteId}</c> y <c>GET {loteId}/partes/{numero}</c>. Host real sin PostgreSQL: la lectura y el almacenamiento son
/// dobles; el cifrador es el REAL (FLZ1 con Data Protection efímera), así que la descarga descifra de verdad y las
/// pruebas de corrupción alteran bloques del formato.
/// <list type="bullet">
///   <item>I1: dueño = <c>sub</c>; <c>X-Tenant-Id</c> se ignora; otro usuario de la misma compañía y el Super Admin
///   reciben 404; el <c>ot_admin</c> (tenant OT) consulta y descarga su lote OT.</item>
///   <item>M2: streaming con <c>Content-Length</c>; parte corrupta en el primer bloque ⇒ 500 sin bytes; en un bloque
///   posterior ⇒ conexión abortada (el cliente nunca recibe un ZIP truncado como válido).</item>
/// </list>
/// <para>Uso de ejemplo: <c>GET /api/v1/consolidados/lotes/{id}/partes/1</c> con el token del dueño ⇒ 200
/// <c>application/zip</c>.</para>
/// </summary>
public sealed class ConsolidadoLoteConsultaDescargaEndpointTests : IClassFixture<ConsolidadoLoteConsultaDescargaEndpointTests.Factory>
{
    private const string Rutas = "/api/v1/consolidados/lotes";
    private const string Permiso = "consolidado-masivo.download";
    private const int MiB = 1024 * 1024;

    private static readonly Guid TenantC = Guid.Parse("c0000000-0000-4000-8000-0000000013c3");
    private static readonly Guid TenantB = Guid.Parse("c0000000-0000-4000-8000-0000000013b2");
    private static readonly Guid TenantOt = Guid.Parse("e1337900-0000-4000-8000-0000000000a1");
    private static readonly Guid Dueno = Guid.Parse("13379000-0000-4000-8000-000000000001");
    private static readonly Guid Companero = Guid.Parse("13379000-0000-4000-8000-000000000002");
    private static readonly Guid SuperAdminId = Guid.Parse("13379000-0000-4000-8000-000000000003");
    private static readonly Guid OtAdminId = Guid.Parse("13379000-0000-4000-8000-000000000004");

    /// <summary>2026-10-06 19:30 UTC ⇒ 14:30 en Colombia.</summary>
    private static readonly DateTimeOffset Creado = new(2026, 10, 6, 19, 30, 0, TimeSpan.Zero);

    private readonly Factory _factory;

    public ConsolidadoLoteConsultaDescargaEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reiniciar();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── AC1 — lote actual ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_LoteActivo_200_ConEstadoTotalesYPartesVacias()
    {
        var lote = Lote(ConsolidadoExportStatus.EnProceso);
        _factory.Lectura.ObtenerActualDelDuenoAsync(Dueno, Arg.Any<CancellationToken>()).Returns(lote);

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).GetAsync($"{Rutas}/actual", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var raiz = await Raiz(response);
        raiz.GetProperty("id").GetGuid().Should().Be(lote.Id);
        raiz.GetProperty("estado").GetString().Should().Be("en_proceso");
        raiz.GetProperty("total").GetInt32().Should().Be(10);
        raiz.GetProperty("procesados").GetInt32().Should().Be(9);
        raiz.GetProperty("incluidos").GetInt32().Should().Be(7);
        raiz.GetProperty("omitidos").GetInt32().Should().Be(2);
        raiz.GetProperty("generados").GetInt32().Should().Be(3);
        raiz.GetProperty("partes").GetArrayLength().Should().Be(0);
        raiz.GetProperty("nombreBase").GetString().Should().Be("consolidados_20261006_1430");
    }

    [Fact]
    public async Task AC1_SinLoteActivoNiRetenido_204()
    {
        _factory.Lectura.ObtenerActualDelDuenoAsync(Dueno, Arg.Any<CancellationToken>()).Returns((ConsolidadoExportBatch?)null);

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).GetAsync($"{Rutas}/actual", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AC1_TerminadoRetenido_200_ConExpiraEnYPartesConNumeroNombreYBytes_IgnorandoXTenantId()
    {
        var lote = Lote(ConsolidadoExportStatus.CompletadoConOmitidos, partes: 2);
        _factory.Lectura.ObtenerActualDelDuenoAsync(Dueno, Arg.Any<CancellationToken>()).Returns(lote);
        _factory.Lectura.ObtenerPartesAsync(lote.Id, Arg.Any<CancellationToken>())
            .Returns([Parte(lote, 1, 4096), Parte(lote, 2, 1024)]);
        var client = Cliente(Token(Dueno, TenantC, "Radicador"));
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantB.ToString());

        var response = await client.GetAsync($"{Rutas}/actual", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raiz = await Raiz(response);
        raiz.GetProperty("expiraEn").GetDateTimeOffset().Should().Be(lote.ExpiresAt!.Value);
        var partes = raiz.GetProperty("partes").EnumerateArray().ToList();
        partes.Select(p => p.GetProperty("numero").GetInt32()).Should().Equal(1, 2);
        partes[0].GetProperty("nombreArchivo").GetString().Should().Be("consolidados_20261006_1430_parte-01-de-02.zip");
        partes[0].GetProperty("bytes").GetInt64().Should().Be(4096);
        partes[1].GetProperty("pdfs").GetInt32().Should().Be(4);
        await _factory.Lectura.Received(1).ObtenerActualDelDuenoAsync(Dueno, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_PorId_DelDueno_200()
    {
        var lote = Lote(ConsolidadoExportStatus.Completado);
        _factory.Lectura.ObtenerDelDuenoAsync(lote.Id, Dueno, Arg.Any<CancellationToken>()).Returns(lote);

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).GetAsync($"{Rutas}/{lote.Id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Raiz(response)).GetProperty("estado").GetString().Should().Be("completado");
    }

    // ── AC2 — descarga de la parte por el dueño ───────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_Dueno_200_ZipDescifradoEnStreaming_ConContentLengthYContentDisposition_AuditadoAntesDelPrimerByte()
    {
        var claro = Datos(2 * MiB + 12345);
        var (lote, _) = LoteDescargable(claro, partes: 2);

        var response = await Cliente(Token(Dueno, TenantC, "Radicador"))
            .GetAsync($"{Rutas}/{lote.Id}/partes/1", HttpCompletionOption.ResponseHeadersRead, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentLength.Should().Be(claro.Length);
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName!.Trim('"')
            .Should().Be("consolidados_20261006_1430_parte-01-de-02.zip");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(claro);

        _factory.Orden.Should().StartWith(["auditoria", "primer_byte_leido"]);
        await _factory.Lectura.Received(1).RegistrarDescargaAsync(
            Arg.Is<ParteDescargadaRegistro>(r => r.Lote.Id == lote.Id && r.PartNumber == 1 && r.RolCodigo == "Radicador"
                && r.UserAgent == "flit-tests/13379"),
            Arg.Any<CancellationToken>());
    }

    // ── AC3 — otro usuario (IDOR) ─────────────────────────────────────────────────────────────────────

    public static TheoryData<string> OtrosUsuarios => ["companero", "superadmin"];

    [Theory]
    [MemberData(nameof(OtrosUsuarios))]
    public async Task AC3_OtroUsuarioDeLaMismaCompaniaOSuperAdmin_404_EnElLoteYEnLaParte_SinAuditarNiLeer(string quien)
    {
        var (lote, _) = LoteDescargable(Datos(100));
        var token = quien == "companero"
            ? Token(Companero, TenantC, "Radicador")
            : Token(SuperAdminId, TenantC, "SuperAdmin", permisos: []);
        var client = Cliente(token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantC.ToString());

        var porId = await client.GetAsync($"{Rutas}/{lote.Id}", Ct);
        var parte = await client.GetAsync($"{Rutas}/{lote.Id}/partes/1", Ct);

        porId.StatusCode.Should().Be(HttpStatusCode.NotFound);
        parte.StatusCode.Should().Be(HttpStatusCode.NotFound);
        parte.Content.Headers.ContentType?.MediaType.Should().NotBe("application/zip");
        await _factory.Storage.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, default);
        await _factory.Lectura.DidNotReceiveWithAnyArgs().RegistrarDescargaAsync(default!, default);
    }

    [Fact]
    public async Task AC3_I1_OtAdminConTenantOt_ConsultaYDescargaSuLoteOt()
    {
        var claro = Datos(5000);
        var (lote, _) = LoteDescargable(claro, dueno: OtAdminId, origen: ConsolidadoExportOrigin.OtBandeja, tenant: TenantOt);
        _factory.Lectura.ObtenerActualDelDuenoAsync(OtAdminId, Arg.Any<CancellationToken>()).Returns(lote);
        var client = Cliente(Token(OtAdminId, TenantOt, "ot_admin"));

        var actual = await client.GetAsync($"{Rutas}/actual", Ct);
        var parte = await client.GetAsync($"{Rutas}/{lote.Id}/partes/1", Ct);

        actual.StatusCode.Should().Be(HttpStatusCode.OK, await actual.Content.ReadAsStringAsync(Ct));
        parte.StatusCode.Should().Be(HttpStatusCode.OK, await parte.Content.ReadAsStringAsync(Ct));
        (await parte.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(claro);
    }

    // ── AC4 — estados que impiden descargar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_LoteNoTerminado_409_LoteNoTerminado()
    {
        var lote = Lote(ConsolidadoExportStatus.Empaquetando);
        _factory.Lectura.ObtenerDelDuenoAsync(lote.Id, Dueno, Arg.Any<CancellationToken>()).Returns(lote);

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).GetAsync($"{Rutas}/{lote.Id}/partes/1", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("lote_no_terminado");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AC4_LotePurgadoOVencido_410_DescargaExpirada(bool purgado)
    {
        var lote = Lote(purgado ? ConsolidadoExportStatus.Expirado : ConsolidadoExportStatus.Completado);
        if (purgado)
        {
            lote.PurgedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            lote.DekWrapped = null;
        }
        else
        {
            lote.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        }

        _factory.Lectura.ObtenerDelDuenoAsync(lote.Id, Dueno, Arg.Any<CancellationToken>()).Returns(lote);

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).GetAsync($"{Rutas}/{lote.Id}/partes/1", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("descarga_expirada");
        await _factory.Storage.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, default);
    }

    [Fact]
    public async Task AC4_AuditoriaNoRegistrada_503_SinBytesDelZip()
    {
        var (lote, _) = LoteDescargable(Datos(100));
        _factory.AuditoriaOk = false;

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).GetAsync($"{Rutas}/{lote.Id}/partes/1", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("auditoria_no_registrada");
        _factory.Orden.Should().NotContain("primer_byte_leido");
    }

    public static TheoryData<string> RutasDelLote => ["actual", "{id}", "{id}/partes/1"];

    [Theory]
    [MemberData(nameof(RutasDelLote))]
    public async Task AC4_SinPermiso_403_EnLasTresRutas(string ruta)
    {
        var response = await Cliente(Token(Dueno, TenantC, "Radicador", permisos: []))
            .GetAsync($"{Rutas}/{ruta.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal)}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Lectura.ReceivedWithAnyArgs(0).ObtenerActualDelDuenoAsync(default, default);
        await _factory.Lectura.ReceivedWithAnyArgs(0).ObtenerDelDuenoAsync(default, default, default);
    }

    [Fact]
    public async Task AC4_SinToken_401()
    {
        var response = await _factory.CreateClient().GetAsync($"{Rutas}/actual", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── M2 — streaming y fallos sin bytes parciales ───────────────────────────────────────────────────

    [Fact]
    public async Task M2_ParteCorruptaEnElPrimerBloque_500_SinBytesDelZip()
    {
        var (lote, cifrado) = LoteDescargable(Datos(2 * MiB + 10));
        cifrado[ConsolidadoLoteCipher.TamanoCabecera + ConsolidadoLoteCipher.TamanoLongitud + 10] ^= 0xFF;

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).GetAsync($"{Rutas}/{lote.Id}/partes/1", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("parte_no_disponible");
    }

    [Fact]
    public async Task M2_ParteCorruptaEnUnBloquePosterior_ConexionAbortada_NuncaUnZipCompleto()
    {
        var claro = Datos(2 * MiB + 10);
        var (lote, cifrado) = LoteDescargable(claro);
        var bloque = ConsolidadoLoteCipher.TamanoLongitud + ConsolidadoLoteCipher.TamanoBloque + ConsolidadoLoteCipher.TamanoTag;
        cifrado[ConsolidadoLoteCipher.TamanoCabecera + bloque + ConsolidadoLoteCipher.TamanoLongitud + 10] ^= 0xFF;

        var act = async () =>
        {
            var response = await Cliente(Token(Dueno, TenantC, "Radicador"))
                .GetAsync($"{Rutas}/{lote.Id}/partes/1", HttpCompletionOption.ResponseHeadersRead, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK, "las cabeceras ya habían salido");
            return await response.Content.ReadAsByteArrayAsync(Ct);
        };

        await act.Should().ThrowAsync<Exception>("el servidor aborta la conexión: el cliente no recibe un ZIP truncado como válido");
    }

    [Fact]
    public async Task M2_ObjetoAusenteEnElAlmacenamiento_500_SinAuditar()
    {
        var (lote, _) = LoteDescargable(Datos(100));
        _factory.Storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).GetAsync($"{Rutas}/{lote.Id}/partes/1", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("parte_no_disponible");
        await _factory.Lectura.DidNotReceiveWithAnyArgs().RegistrarDescargaAsync(default!, default);
    }

    // ── HU #13386 AC6 — lote cancelado ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU13386_AC6_Actual_LoteCancelado_200_CanceladoConTerminadoEnContadoresYPartesVacio()
    {
        var lote = LoteCancelado();
        _factory.Lectura.ObtenerActualDelDuenoAsync(Dueno, Arg.Any<CancellationToken>()).Returns(lote);

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).GetAsync($"{Rutas}/actual", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "el último lote cancelado se informa; ya no es 204");
        var raiz = await Raiz(response);
        raiz.GetProperty("id").GetGuid().Should().Be(lote.Id);
        raiz.GetProperty("estado").GetString().Should().Be("cancelado");
        raiz.GetProperty("terminadoEn").GetDateTimeOffset().Should().Be(lote.FinishedAt!.Value);
        raiz.GetProperty("total").GetInt32().Should().Be(10);
        raiz.GetProperty("incluidos").GetInt32().Should().Be(7);
        raiz.GetProperty("omitidos").GetInt32().Should().Be(2);
        raiz.GetProperty("generados").GetInt32().Should().Be(3);
        raiz.GetProperty("partes").GetArrayLength().Should().Be(0);
        await _factory.Lectura.DidNotReceiveWithAnyArgs().ObtenerPartesAsync(default, Ct);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task HU13386_AC6_DescargaDeCualquierParteDeUnLoteCancelado_410_DescargaExpirada(int numero)
    {
        var lote = LoteCancelado();
        _factory.Lectura.ObtenerDelDuenoAsync(lote.Id, Dueno, Arg.Any<CancellationToken>()).Returns(lote);

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).GetAsync($"{Rutas}/{lote.Id}/partes/{numero}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("descarga_expirada");
        await _factory.Storage.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, Ct);
        await _factory.Lectura.DidNotReceiveWithAnyArgs().RegistrarDescargaAsync(default!, Ct);
    }

    /// <summary>Lote como lo deja la cancelación (#13385): DEK NULL y finished/expires/purged = instante de la cancelación.</summary>
    private static ConsolidadoExportBatch LoteCancelado()
    {
        var lote = Lote(ConsolidadoExportStatus.Cancelado, partes: 2);
        var instante = new DateTimeOffset(2026, 10, 7, 21, 15, 0, TimeSpan.Zero);
        lote.FinishedAt = instante;
        lote.ExpiresAt = instante;
        lote.PurgedAt = instante;
        lote.DekWrapped = null;
        return lote;
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

    private (ConsolidadoExportBatch Lote, byte[] Cifrado) LoteDescargable(
        byte[] claro, short partes = 1, Guid? dueno = null, string origen = ConsolidadoExportOrigin.Tramites, Guid? tenant = null)
    {
        var lote = Lote(ConsolidadoExportStatus.Completado, partes, dueno ?? Dueno, origen, tenant ?? TenantC);
        lote.DekWrapped = _factory.Cifrador.GenerarDekEnvuelta();
        using var origenClaro = new MemoryStream(claro);
        using var destino = new MemoryStream();
        _factory.Cifrador.CifrarAsync(lote.DekWrapped, lote.Id, 1, origenClaro, destino, Ct).GetAwaiter().GetResult();
        var cifrado = destino.ToArray();

        _factory.Lectura.ObtenerDelDuenoAsync(lote.Id, lote.RequestedByUserId, Arg.Any<CancellationToken>()).Returns(lote);
        _factory.Lectura.ObtenerPartesAsync(lote.Id, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(1, partes).Select(n => Parte(lote, (short)n, claro.Length)).ToList());
        _factory.Storage.OpenReadAsync("fm/parte-1", Arg.Any<CancellationToken>())
            .Returns(_ => new StreamQueAvisa(cifrado, () => _factory.Orden.Add("primer_byte_leido")));
        return (lote, cifrado);
    }

    private static ConsolidadoExportBatch Lote(
        string estado, short partes = 1, Guid? dueno = null, string origen = ConsolidadoExportOrigin.Tramites, Guid? tenant = null)
    {
        var terminal = !ConsolidadoExportStatus.EsActivo(estado);
        return new ConsolidadoExportBatch
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant ?? TenantC,
            RequestedByUserId = dueno ?? Dueno,
            RequestedRoleCode = "Radicador",
            Origin = origen,
            OtTransitOfficeId = origen == ConsolidadoExportOrigin.OtBandeja ? Guid.NewGuid() : null,
            DocumentType = origen == ConsolidadoExportOrigin.OtBandeja
                ? ConsolidadoExportDocumentType.ConsolidadoMaestro
                : ConsolidadoExportDocumentType.Consolidado,
            Status = estado,
            TotalItems = 10,
            IncludedCount = 7,
            OmittedCount = 2,
            GeneratedCount = 3,
            PartsCount = partes,
            DekWrapped = [1, 2, 3],
            CreatedAt = Creado,
            FinishedAt = terminal ? DateTimeOffset.UtcNow.AddHours(-1) : null,
            ExpiresAt = terminal ? DateTimeOffset.UtcNow.AddHours(23) : null,
        };
    }

    private static ConsolidadoExportBatchPart Parte(ConsolidadoExportBatch lote, short numero, long bytes) => new()
    {
        Id = Guid.NewGuid(),
        BatchId = lote.Id,
        PartNumber = numero,
        Status = ConsolidadoExportPartStatus.Cerrada,
        PdfCount = 4,
        OmittedCount = 1,
        PlainSizeBytes = bytes,
        StoredSizeBytes = bytes + 100,
        StoredSha256 = new string('a', 64),
        StoragePath = $"fm/parte-{numero}",
        ClosedAt = DateTimeOffset.UtcNow.AddHours(-1),
    };

    private static byte[] Datos(int n)
    {
        var datos = new byte[n];
        new Random(13379).NextBytes(datos);
        datos[0] = (byte)'P';
        datos[1] = (byte)'K';
        return datos;
    }

    private HttpClient Cliente(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("flit-tests/13379");
        return client;
    }

    private static async Task<JsonElement> Raiz(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private static string Token(Guid sub, Guid tenantId, string role, string[]? permisos = null)
    {
        var claims = new List<Claim>
        {
            new("sub", sub.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };
        claims.AddRange((permisos ?? [Permiso]).Select(p => new Claim("permissions", p)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Host con la lectura y el almacenamiento sustituidos y el cifrador real sobre Data Protection efímera.</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        public IConsolidadoLoteLectura Lectura { get; } = Substitute.For<IConsolidadoLoteLectura>();
        public IConsolidadoLoteParteStorage Storage { get; } = Substitute.For<IConsolidadoLoteParteStorage>();
        public IConsolidadoLoteCipher Cifrador { get; } = new ConsolidadoLoteCipher(new EphemeralDataProtectionProvider());
        public List<string> Orden { get; } = [];
        public bool AuditoriaOk { get; set; } = true;

        public Factory() => Reiniciar();

        public void Reiniciar()
        {
            Lectura.ClearSubstitute();
            Storage.ClearSubstitute();
            lock (Orden)
                Orden.Clear();
            AuditoriaOk = true;
            Lectura.RegistrarDescargaAsync(Arg.Any<ParteDescargadaRegistro>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    lock (Orden)
                        Orden.Add("auditoria");
                    return AuditoriaOk;
                });
            Lectura.ObtenerPartesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<ConsolidadoExportBatchPart>());
            Lectura.ObtenerVencidosAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<Guid>());
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConsolidadoLoteLectura>();
                services.RemoveAll<IConsolidadoLoteParteStorage>();
                services.RemoveAll<IConsolidadoLoteCipher>();
                services.AddScoped(_ => Lectura);
                services.AddScoped(_ => Storage);
                services.AddSingleton(Cifrador);
            });
        }
    }

    /// <summary>Stream de lectura que avisa al leer su primer byte (orden auditoría → primer byte).</summary>
    private sealed class StreamQueAvisa(byte[] datos, Action alLeer) : MemoryStream(datos, writable: false)
    {
        private bool _avisado;

        public override int Read(byte[] buffer, int offset, int count)
        {
            Avisar();
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            Avisar();
            return base.Read(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Avisar();
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Avisar();
            return base.ReadAsync(buffer, cancellationToken);
        }

        private void Avisar()
        {
            if (_avisado)
                return;
            _avisado = true;
            lock (this)
                alLeer();
        }
    }
}
