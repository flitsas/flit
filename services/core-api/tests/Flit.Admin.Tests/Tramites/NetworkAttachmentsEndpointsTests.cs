using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #12410 (Feature #12257) — capa HTTP de los documentos de la red, sin PostgreSQL:
/// <c>GET /api/v1/tramites/network/instances/{id}/attachments</c> y
/// <c>GET …/attachments/{attachmentId}/download</c> en un host real (<see cref="WebApplicationFactory{TEntryPoint}"/>)
/// con dobles para el alcance (P = MARCA_BLANCA con C1/C2; Q = CONCESION con C3), el dueño de cada
/// trámite, el repositorio de anexos, el almacenamiento, los interruptores y el auditor.
/// <list type="bullet">
///   <item>AC1 — metadatos (<c>AttachmentDto</c>) sin <c>url</c>, <c>storagePath</c> ni previsualización.</item>
///   <item>AC2 — descarga por transmisión con <c>Content-Disposition: attachment</c>; no existe <c>preview-url</c> de red.</item>
///   <item>AC4/AC9 — exactamente UN registro de auditoría por acceso (ok, forbidden, not_found); ninguno sin hijo.</item>
///   <item>AC5 — CONCESION 403 <c>network_documents_disabled</c> apagado / 200 encendido; MARCA_BLANCA no consulta el interruptor.</item>
///   <item>AC6 — 404 escueto idéntico: trámite ajeno, inexistente, documento inexistente, binario perdido.</item>
///   <item>AC7 — interruptor de grupo apagado ⇒ 403; SuperAdmin conserva las rutas de documentos existentes (D7).</item>
///   <item>AC8 — la ruta de descarga propia sigue igual para un cliente sin jerarquía.</item>
/// </list>
/// Uso de ejemplo: <c>GET /api/v1/tramites/network/instances/{idDeC1}/attachments/{aid}/download</c> con token de P ⇒
/// <c>200</c>, <c>Content-Disposition: attachment; filename=factura.pdf</c>.
/// </summary>
public sealed class NetworkAttachmentsEndpointsTests : IClassFixture<NetworkAttachmentsEndpointsTests.DocumentsFactory>
{
    private const string NetworkBase = "/api/v1/tramites/network/instances";
    private const string OwnBase = "/api/v1/tramites/instances";

    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid C2 = Guid.Parse("a0000000-0000-4000-8000-000000001200");
    private static readonly Guid Q = Guid.Parse("a0000000-0000-4000-8000-000000000200");
    private static readonly Guid C3 = Guid.Parse("a0000000-0000-4000-8000-000000002100");
    private static readonly Guid X = Guid.Parse("a0000000-0000-4000-8000-000000009900");
    private static readonly Guid S = Guid.Parse("a0000000-0000-4000-8000-000000005500");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid ChildProcedure = Guid.Parse("a0000000-0003-4000-8000-000000001101");
    private static readonly Guid SiblingProcedure = Guid.Parse("a0000000-0003-4000-8000-000000001201");
    private static readonly Guid OwnProcedure = Guid.Parse("a0000000-0003-4000-8000-000000000101");
    private static readonly Guid ForeignProcedure = Guid.Parse("a0000000-0003-4000-8000-000000009901");
    private static readonly Guid ConcesionChildProcedure = Guid.Parse("a0000000-0003-4000-8000-000000002101");
    private static readonly Guid SingleProcedure = Guid.Parse("a0000000-0003-4000-8000-000000005501");
    private static readonly Guid MissingProcedure = Guid.Parse("a0000000-0003-4000-8000-00000000ffff");

    private static readonly Guid AttOk = Guid.Parse("a0000000-0004-4000-8000-000000000001");
    private static readonly Guid AttLost = Guid.Parse("a0000000-0004-4000-8000-000000000002");
    private static readonly Guid AttUnknown = Guid.Parse("a0000000-0004-4000-8000-00000000eeee");

    private static readonly byte[] Content = Encoding.UTF8.GetBytes("%PDF-1.4 contenido de prueba");

    private readonly DocumentsFactory _factory;

    public NetworkAttachmentsEndpointsTests(DocumentsFactory factory)
    {
        _factory = factory;
        _factory.AuditWriter.ClearReceivedCalls();
        _factory.Storage.ClearReceivedCalls();
        _factory.Repo.ClearReceivedCalls();
        _factory.GroupReadScope = true;
        _factory.NetworkDocumentsConcesion = false;
    }

    // ── Autenticación / policy de cabeza ──────────────────────────────────────────────────────

    [Fact]
    public async Task Sin_token_401_en_ambas_rutas()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync($"{NetworkBase}/{ChildProcedure}/attachments", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"{NetworkBase}/{ChildProcedure}/attachments/{AttOk}/download", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cliente_sin_jerarquia_y_SuperAdmin_403_network_scope_required_sin_auditar()
    {
        foreach (var client in new[] { ClientFor(S, "AdminCompany"), ClientFor(P, "SuperAdmin") })
        {
            var list = await client.GetAsync($"{NetworkBase}/{ChildProcedure}/attachments", TestContext.Current.CancellationToken);
            var download = await client.GetAsync($"{NetworkBase}/{ChildProcedure}/attachments/{AttOk}/download", TestContext.Current.CancellationToken);

            list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            download.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ScopeRequired);
            (await download.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ScopeRequired);
        }

        await _factory.AuditWriter.DidNotReceive().WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>());
    }

    // ── AC1 — metadatos sin URLs ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_Cabeza_MarcaBlanca_lista_los_metadatos_del_tramite_del_hijo_sin_ninguna_direccion()
    {
        var response = await ClientFor(P).GetAsync($"{NetworkBase}/{ChildProcedure}/attachments", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(json);
        var items = body.RootElement.GetProperty("attachments").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        var first = items.Single(i => i.GetProperty("id").GetGuid() == AttOk);
        first.GetProperty("tipo").GetString().Should().Be("factura");
        first.GetProperty("filename").GetString().Should().Be("factura.pdf");
        first.GetProperty("mimetype").GetString().Should().Be("application/pdf");
        first.GetProperty("sizeBytes").GetInt64().Should().Be(Content.Length);
        first.TryGetProperty("uploadedAt", out _).Should().BeTrue();
        first.GetProperty("digitallySigned").GetBoolean().Should().BeFalse();
        // Ni dirección prefirmada, ni previsualización, ni ruta de almacenamiento (AC1).
        json.Should().NotContainAny("storagePath", "storage_path", "url", "preview", "http://", "https://", "X-Amz");
        await _factory.AuditWriter.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Resource == NetworkAccessVocabulary.Resources.AttachmentsList
                && e.Result == NetworkAccessVocabulary.Results.Ok && e.ProcedureId == ChildProcedure
                && e.ProcedureTenantId == C1 && e.AttachmentId == null && e.ActorTenantId == P && e.ActorUserId == UserId),
            Arg.Any<CancellationToken>());
    }

    // ── AC2 — descarga proxeada ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_Cabeza_MarcaBlanca_descarga_por_transmision_con_Content_Disposition_attachment()
    {
        var response = await ClientFor(P).GetAsync($"{NetworkBase}/{ChildProcedure}/attachments/{AttOk}/download", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName!.Trim('"').Should().Be("factura.pdf");
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(Content);
        await _factory.Storage.Received(1).OpenReadAsync("ok", Arg.Any<CancellationToken>());
        await _factory.AuditWriter.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Resource == NetworkAccessVocabulary.Resources.AttachmentsDownload
                && e.Result == NetworkAccessVocabulary.Results.Ok && e.ProcedureId == ChildProcedure
                && e.ProcedureTenantId == C1 && e.AttachmentId == AttOk && e.ReachedTenantIds.Single() == C1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AC2_No_existe_preview_url_bajo_las_rutas_de_red()
    {
        var networkRoutes = _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .Where(r => r.StartsWith("/api/v1/tramites/network/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        networkRoutes.Should().Contain("/api/v1/tramites/network/instances/{id:guid}/attachments");
        networkRoutes.Should().Contain("/api/v1/tramites/network/instances/{id:guid}/attachments/{attachmentId:guid}/download");
        networkRoutes.Should().NotContain(r => r.Contains("preview", StringComparison.OrdinalIgnoreCase),
            "la URL prefirmada es un portador anónimo: la red solo tiene descarga proxeada");
    }

    [Fact]
    public async Task AC2_La_ruta_preview_url_de_red_responde_404_de_enrutamiento()
    {
        var response = await ClientFor(P).GetAsync(
            $"{NetworkBase}/{ChildProcedure}/attachments/{AttOk}/preview-url", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── AC5 — clase CONCESION cerrada por interruptor ─────────────────────────────────────────

    [Fact]
    public async Task AC5_Cabeza_Concesion_403_network_documents_disabled_con_el_interruptor_apagado_y_audita_forbidden()
    {
        _factory.NetworkDocumentsConcesion = false;
        var client = ClientFor(Q);

        var list = await client.GetAsync($"{NetworkBase}/{ConcesionChildProcedure}/attachments", TestContext.Current.CancellationToken);
        var download = await client.GetAsync($"{NetworkBase}/{ConcesionChildProcedure}/attachments/{AttOk}/download", TestContext.Current.CancellationToken);

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        download.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("{\"error\":\"network_documents_disabled\"}");
        (await download.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("{\"error\":\"network_documents_disabled\"}");
        await _factory.Storage.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _factory.AuditWriter.Received(2).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Result == NetworkAccessVocabulary.Results.Forbidden
                && e.ActorTenantId == Q && e.ProcedureTenantId == C3 && e.ProcedureId == ConcesionChildProcedure),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC5_Cabeza_Concesion_con_el_interruptor_encendido_se_comporta_como_MarcaBlanca()
    {
        _factory.NetworkDocumentsConcesion = true;
        var client = ClientFor(Q);

        var list = await client.GetAsync($"{NetworkBase}/{ConcesionChildProcedure}/attachments", TestContext.Current.CancellationToken);
        var download = await client.GetAsync($"{NetworkBase}/{ConcesionChildProcedure}/attachments/{AttOk}/download", TestContext.Current.CancellationToken);

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(Content);
        await _factory.AuditWriter.Received(2).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Result == NetworkAccessVocabulary.Results.Ok && e.ActorTenantId == Q && e.ProcedureTenantId == C3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC5_MarcaBlanca_no_consulta_el_interruptor_de_Concesion()
    {
        _factory.Switches.ClearReceivedCalls();

        var response = await ClientFor(P).GetAsync($"{NetworkBase}/{ChildProcedure}/attachments", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Switches.DidNotReceive().IsNetworkDocumentsConcesionEnabledAsync(Arg.Any<CancellationToken>());
    }

    // ── AC6 — aislamiento y anti-enumeración ──────────────────────────────────────────────────

    [Fact]
    public async Task AC6_Tramite_ajeno_inexistente_documento_inexistente_y_binario_perdido_devuelven_el_mismo_404()
    {
        var client = ClientFor(P);
        var cases = new (string Name, string Url)[]
        {
            ("listado de trámite ajeno", $"{NetworkBase}/{ForeignProcedure}/attachments"),
            ("listado de trámite inexistente", $"{NetworkBase}/{MissingProcedure}/attachments"),
            ("descarga de trámite ajeno", $"{NetworkBase}/{ForeignProcedure}/attachments/{AttOk}/download"),
            ("descarga de trámite inexistente", $"{NetworkBase}/{MissingProcedure}/attachments/{AttOk}/download"),
            ("documento inexistente en trámite del hijo", $"{NetworkBase}/{ChildProcedure}/attachments/{AttUnknown}/download"),
            ("binario perdido en trámite del hijo", $"{NetworkBase}/{ChildProcedure}/attachments/{AttLost}/download"),
        };

        var bodies = new List<string>();
        foreach (var (name, url) in cases)
        {
            var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, name);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            bodies.Add(body);
        }

        bodies.Distinct().Should().ContainSingle("todos los casos comparten el mismo cuerpo escueto").Which.Should().Be("{\"error\":\"not_found\"}");
        // Nada del ajeno llegó al almacenamiento ni al repositorio con su tenant.
        await _factory.Storage.DidNotReceive().OpenReadAsync("foreign", Arg.Any<CancellationToken>());
        await _factory.Repo.DidNotReceive().GetByIdWithAttachmentsAsync(ForeignProcedure, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC6_Usuario_del_hijo_no_alcanza_los_documentos_del_hermano_ni_de_la_cabeza_por_la_red()
    {
        // C1 es Single: la policy de cabeza lo detiene antes de cualquier consulta (mismo 403 que hoy).
        var client = ClientFor(C1);

        foreach (var id in new[] { SiblingProcedure, OwnProcedure, ForeignProcedure })
        {
            var response = await client.GetAsync($"{NetworkBase}/{id}/attachments", TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ScopeRequired);
        }

        // Y por la ruta propia el tenant del token manda: el trámite del hermano no existe para C1.
        var own = await client.GetAsync($"{OwnBase}/{SiblingProcedure}/attachments", TestContext.Current.CancellationToken);
        own.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _factory.AuditWriter.DidNotReceive().WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>());
    }

    // ── AC7 — interruptor de emergencia y SuperAdmin ──────────────────────────────────────────

    [Fact]
    public async Task AC7_Interruptor_de_alcance_de_grupo_apagado_403_en_ambas_rutas()
    {
        _factory.GroupReadScope = false;
        var client = ClientFor(P);

        var list = await client.GetAsync($"{NetworkBase}/{ChildProcedure}/attachments", TestContext.Current.CancellationToken);
        var download = await client.GetAsync($"{NetworkBase}/{ChildProcedure}/attachments/{AttOk}/download", TestContext.Current.CancellationToken);

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        download.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ScopeRequired);
        await _factory.AuditWriter.DidNotReceive().WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC7_SuperAdmin_conserva_las_rutas_de_documentos_existentes_incluida_la_negativa_a_contenido_ajeno_D7()
    {
        var client = ClientFor(P, "SuperAdmin");

        // Con el tenant dueño seleccionado: igual que hoy.
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", C1.ToString());
        var ok = await client.GetAsync($"{OwnBase}/{ChildProcedure}/attachments/{AttOk}/download", TestContext.Current.CancellationToken);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        ok.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");

        // D7: el trámite de C1 no se sirve bajo otro tenant seleccionado, ni siquiera al SuperAdmin.
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", P.ToString());
        var ajeno = await client.GetAsync($"{OwnBase}/{ChildProcedure}/attachments/{AttOk}/download", TestContext.Current.CancellationToken);
        ajeno.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _factory.AuditWriter.DidNotReceive().WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>());
    }

    // ── AC8 — paridad de un cliente sin jerarquía ─────────────────────────────────────────────

    [Fact]
    public async Task AC8_Cliente_sin_jerarquia_lista_y_descarga_sus_documentos_por_las_rutas_de_hoy_sin_auditoria_de_red()
    {
        var client = ClientFor(S);

        var list = await client.GetAsync($"{OwnBase}/{SingleProcedure}/attachments", TestContext.Current.CancellationToken);
        var download = await client.GetAsync($"{OwnBase}/{SingleProcedure}/attachments/{AttOk}/download", TestContext.Current.CancellationToken);

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("attachments").GetArrayLength().Should().Be(2);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        (await download.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(Content);
        await _factory.AuditWriter.DidNotReceive().WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>());
    }

    // ── AC4 / AC9 — exactamente un registro por acceso ───────────────────────────────────────

    [Fact]
    public async Task AC9_Cada_acceso_deja_exactamente_un_registro_y_los_accesos_sin_hijo_ninguno()
    {
        var client = ClientFor(P);

        await client.GetAsync($"{NetworkBase}/{ChildProcedure}/attachments", TestContext.Current.CancellationToken);                       // ok
        await client.GetAsync($"{NetworkBase}/{ChildProcedure}/attachments/{AttOk}/download", TestContext.Current.CancellationToken);     // ok
        await client.GetAsync($"{NetworkBase}/{ChildProcedure}/attachments/{AttUnknown}/download", TestContext.Current.CancellationToken); // not_found (hijo)
        await client.GetAsync($"{NetworkBase}/{ForeignProcedure}/attachments", TestContext.Current.CancellationToken);                    // not_found (ajeno: intento imputado a X)
        await client.GetAsync($"{NetworkBase}/{OwnProcedure}/attachments", TestContext.Current.CancellationToken);                         // propio: sin registro
        await client.GetAsync($"{NetworkBase}/{MissingProcedure}/attachments", TestContext.Current.CancellationToken);                     // inexistente: sin hijo, sin registro

        await _factory.AuditWriter.Received(4).WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>());
        await _factory.AuditWriter.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Result == NetworkAccessVocabulary.Results.NotFound && e.AttachmentId == AttUnknown && e.ProcedureTenantId == C1),
            Arg.Any<CancellationToken>());
        await _factory.AuditWriter.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Result == NetworkAccessVocabulary.Results.NotFound && e.ProcedureId == ForeignProcedure && e.ReachedTenantIds.Single() == X),
            Arg.Any<CancellationToken>());
        await _factory.AuditWriter.DidNotReceive().WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.ProcedureId == OwnProcedure || e.ProcedureId == MissingProcedure),
            Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private HttpClient ClientFor(Guid tenantId, string role = "AdminCompany")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantId, role));
        return client;
    }

    private static string Token(Guid tenantId, string role)
    {
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };

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

    private static ProcedureInstance InstanceWithAttachments(Guid id, Guid tenant) => new()
    {
        Id = id,
        TenantId = tenant,
        Attachments =
        [
            new ProcedureInstanceAttachment
            {
                Id = AttOk, TenantId = tenant, ProcedureInstanceId = id, Tipo = "factura", Filename = "factura.pdf",
                Mimetype = "application/pdf", SizeBytes = Content.Length, Sha256 = "abc", StoragePath = "ok",
                UploadedAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
            },
            new ProcedureInstanceAttachment
            {
                Id = AttLost, TenantId = tenant, ProcedureInstanceId = id, Tipo = "soat", Filename = "soat.pdf",
                Mimetype = "application/pdf", SizeBytes = 10, Sha256 = "def", StoragePath = "lost",
                UploadedAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero),
            },
        ],
    };

    /// <summary>Host sin PostgreSQL: alcance, dueños, repositorio de anexos, almacenamiento, interruptores y auditor sustituidos.</summary>
    public sealed class DocumentsFactory : WebApplicationFactory<Program>
    {
        private static readonly Dictionary<Guid, Guid> Owners = new()
        {
            [ChildProcedure] = C1,
            [SiblingProcedure] = C2,
            [OwnProcedure] = P,
            [ForeignProcedure] = X,
            [ConcesionChildProcedure] = C3,
            [SingleProcedure] = S,
        };

        public bool GroupReadScope { get; set; } = true;

        public bool NetworkDocumentsConcesion { get; set; }

        public INetworkAccessAuditWriter AuditWriter { get; } = Substitute.For<INetworkAccessAuditWriter>();

        public IProcedureInstanceRepository Repo { get; } = Substitute.For<IProcedureInstanceRepository>();

        public IAttachmentStorage Storage { get; } = Substitute.For<IAttachmentStorage>();

        public IHierarchySwitches Switches { get; } = Substitute.For<IHierarchySwitches>();

        public DocumentsFactory()
        {
            Repo.GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var id = call.ArgAt<Guid>(0);
                    var tenant = call.ArgAt<Guid>(1);
                    return Owners.TryGetValue(id, out var owner) && owner == tenant ? InstanceWithAttachments(id, tenant) : null;
                });
            Storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<string>(0) == "ok" ? new MemoryStream(Content, writable: false) : (Stream?)null);
            Switches.IsGroupReadScopeEnabledAsync(Arg.Any<CancellationToken>()).Returns(_ => GroupReadScope);
            Switches.IsNetworkDocumentsConcesionEnabledAsync(Arg.Any<CancellationToken>()).Returns(_ => NetworkDocumentsConcesion);
            Switches.IsInheritedConfigurationEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ITenantScopeResolver>(_ => new FakeScopeResolver(this));
                services.AddScoped<IProcedureInstanceOwnerLookup>(_ => new FakeOwnerLookup());
                services.AddScoped(_ => Repo);
                services.AddScoped(_ => Storage);
                services.AddScoped(_ => Switches);
                services.AddScoped(_ => AuditWriter);
                var imprints = Substitute.For<IVehicleSignatureImprintRepository>();
                imprints.ListSignedAttachmentIdsForInstanceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(new HashSet<Guid>());
                services.AddScoped(_ => imprints);
            });
        }

        /// <summary>Lo que hace <c>DbTenantScopeResolver</c>: interruptor de grupo apagado ⇒ Single para todos.</summary>
        private sealed class FakeScopeResolver(DocumentsFactory factory) : ITenantScopeResolver
        {
            public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default)
            {
                if (!factory.GroupReadScope)
                    return Task.FromResult(TenantScope.Single(tenantId));
                if (tenantId == P)
                    return Task.FromResult(TenantScope.Group(P, [C1, C2], GroupKind.MarcaBlanca));
                if (tenantId == Q)
                    return Task.FromResult(TenantScope.Group(Q, [C3], GroupKind.Concesion));
                return Task.FromResult(TenantScope.Single(tenantId));
            }
        }

        private sealed class FakeOwnerLookup : IProcedureInstanceOwnerLookup
        {
            public Task<Guid?> GetOwnerTenantIdAsync(Guid procedureInstanceId, CancellationToken ct = default) =>
                Task.FromResult(Owners.TryGetValue(procedureInstanceId, out var owner) ? owner : (Guid?)null);

            public Task<IReadOnlyDictionary<Guid, Guid>> GetOwnerTenantIdsAsync(IReadOnlyCollection<Guid> procedureInstanceIds, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(
                    procedureInstanceIds.Where(Owners.ContainsKey).Distinct().ToDictionary(id => id, id => Owners[id]));
        }
    }
}
