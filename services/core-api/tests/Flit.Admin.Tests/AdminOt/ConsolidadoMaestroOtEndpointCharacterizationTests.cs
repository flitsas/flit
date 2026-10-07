using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtProfile;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Admin.Tests.AdminOt;

/// <summary>
/// HU #13389 (Épica #13216, AC1) — caracterización HTTP del
/// <c>POST /api/v1/admin/ot/client-procedures/{id}/consolidado-maestro</c> ANTES de extraer el acceso del
/// OT, el scope de la compañía cliente y la precedencia de la matriz a un servicio compartido. Fija el
/// comportamiento observable de hoy: 404 fuera de alcance, 403 <c>QUIPUX_READONLY</c> sin generar, 200
/// con <c>force</c> y la precedencia de la matriz, <c>sin_adjuntos</c> → 409 y el override de organismo del
/// Super Admin. Host real sin PostgreSQL: los puertos del endpoint son sustitutos y el generador es el
/// handler real sobre un repositorio sustituto (patrón <c>EntregarConsolidadoEndpointTests</c> /
/// <c>RepoSubstituteFactory</c>).
/// <para>Uso de ejemplo: <c>POST /api/v1/admin/ot/client-procedures/{id}/consolidado-maestro?force=true</c>
/// con un JWT <c>ot_admin</c> ⇒ <c>200 { document: { attachmentId, … }, regenerado }</c>.</para>
/// </summary>
public sealed class ConsolidadoMaestroOtEndpointCharacterizationTests
    : IClassFixture<ConsolidadoMaestroOtEndpointCharacterizationTests.OtConsolidadoFactory>
{
    private static readonly Guid TenantOt = Guid.Parse("d0000000-0000-4000-8000-0000000000a1");
    private static readonly Guid TenantSuperAdmin = Guid.Parse("d0000000-0000-4000-8000-0000000000c3");
    private static readonly Guid ClientTenant = Guid.Parse("d0000000-0000-4000-8000-0000000000b2");
    private static readonly Guid Organismo = Guid.Parse("d0000000-0000-4000-8000-0000000000e5");

    private readonly OtConsolidadoFactory _factory;

    public ConsolidadoMaestroOtEndpointCharacterizationTests(OtConsolidadoFactory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task FueraDelAlcanceDelOt_404_SinGuardNiScopeCliente()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        _factory.OtRepo.GetByIdAsync(TenantOt, id, null, Arg.Any<CancellationToken>()).Returns((OtClientProcedure?)null);

        var response = await ClientFor(OtAdminToken()).PostAsync(Url(id), null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("Trámite no encontrado");
        await _factory.Guard.DidNotReceiveWithAnyArgs().ValidateActionAsync(default, default!, default);
        await _factory.OtRepo.DidNotReceiveWithAnyArgs()
            .ExecuteInClientTenantScopeAsync<(GenerarConsolidadoResult?, string?)>(default, default!, default);
        await _factory.TramitesRepo.DidNotReceiveWithAnyArgs().GetByIdWithChecklistGraphAsync(default, default, default);
    }

    [Fact]
    public async Task GuardQuipuxQueNiega_403_QuipuxReadonly_YNuncaLlamaAlGenerador()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        Accesible(id, TenantOt, null);
        _factory.Guard.ValidateActionAsync(TenantOt, "generar_consolidado_maestro", Arg.Any<CancellationToken>())
            .Returns(QuipuxReadOnlyResult.Forbidden());

        var response = await ClientFor(OtAdminToken()).PostAsync(Url(id), null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("QUIPUX_READONLY");
        await _factory.OtRepo.DidNotReceiveWithAnyArgs()
            .ExecuteInClientTenantScopeAsync<(GenerarConsolidadoResult?, string?)>(default, default!, default);
        await _factory.Matrix.DidNotReceiveWithAnyArgs().ResolveAsync(default, default, default);
        await _factory.TramitesRepo.DidNotReceiveWithAnyArgs().GetByIdWithChecklistGraphAsync(default, default, default);
    }

    [Fact]
    public async Task Accesible_200_ConLaPrecedenciaDeLaMatriz_ResueltaDentroDelScopeCliente_YEnOrden()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var access = Accesible(id, TenantOt, null);
        var instance = _factory.Instance(id, ClientTenant, ("factura", "factura.pdf"), ("aduana", "aduana.pdf"), ("soat", "soat.pdf"));
        _factory.TramitesRepo.GetByIdWithChecklistGraphAsync(id, ClientTenant, Arg.Any<CancellationToken>())
            .Returns(_ => { _factory.Log.Add($"generador:{(_factory.EnScopeCliente ? "en-scope" : "fuera")}"); return instance; });
        _factory.Matrix.ResolveAsync(access.ProcedureTypeId, Organismo, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _factory.Log.Add($"matriz:{(_factory.EnScopeCliente ? "en-scope" : "fuera")}");
                return Matriz("soat", "factura");
            });

        var response = await ClientFor(OtAdminToken()).PostAsync(Url(id), null, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using (var json = JsonDocument.Parse(body))
        {
            json.RootElement.GetProperty("regenerado").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("document").GetProperty("tipo").GetString().Should().Be("consolidado_maestro");
        }

        // Orden acceso → guard Quipux → scope cliente → matriz → generador.
        _factory.Log.Should().Equal(
            "acceso",
            "guard:generar_consolidado_maestro",
            $"scope:{ClientTenant}",
            "matriz:en-scope",
            "generador:en-scope");

        // La precedencia de la matriz llega al generador: soat → factura → aduana (anexo, al final).
        var payload = instance.Events.Should().ContainSingle(e => e.Tipo == "consolidado_maestro_generado").Which.Payload;
        payload.IndexOf("soat", StringComparison.Ordinal).Should().BeLessThan(payload.IndexOf("factura", StringComparison.Ordinal));
        payload.IndexOf("factura", StringComparison.Ordinal).Should().BeLessThan(payload.IndexOf("aduana", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Accesible_SiLaMatrizFalla_200_ConElOrdenDeRespaldo()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        Accesible(id, TenantOt, null);
        var instance = _factory.Instance(id, ClientTenant, ("soat", "soat.pdf"));
        _factory.TramitesRepo.GetByIdWithChecklistGraphAsync(id, ClientTenant, Arg.Any<CancellationToken>()).Returns(instance);
        _factory.Matrix.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("matriz caída"));

        var response = await ClientFor(OtAdminToken()).PostAsync(Url(id), null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
    }

    [Theory]
    [InlineData("", HttpStatusCode.OK)]
    [InlineData("?force=false", HttpStatusCode.OK)]
    [InlineData("?force=true", HttpStatusCode.Conflict)]
    public async Task Force_LlegaAlGenerador(string query, HttpStatusCode esperado)
    {
        // Solo hay un maestro vigente: sin force se reutiliza (200, regenerado=false); con force el
        // generador se salta la caché y, sin fuentes que fusionar, responde sin_adjuntos (409).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        Accesible(id, TenantOt, null);
        var instance = _factory.Instance(id, ClientTenant, ("consolidado_maestro", "maestro.pdf"));
        instance.ConsolidadoMaestroVigente = true;
        _factory.TramitesRepo.GetByIdWithChecklistGraphAsync(id, ClientTenant, Arg.Any<CancellationToken>()).Returns(instance);

        var response = await ClientFor(OtAdminToken()).PostAsync(Url(id) + query, null, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(esperado, body);
        if (esperado == HttpStatusCode.OK)
        {
            using var json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("regenerado").GetBoolean().Should().BeFalse();
        }
        else
        {
            body.Should().Contain("sin_adjuntos");
        }
    }

    [Fact]
    public async Task SinAdjuntos_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        Accesible(id, TenantOt, null);
        var instance = _factory.Instance(id, ClientTenant);
        _factory.TramitesRepo.GetByIdWithChecklistGraphAsync(id, ClientTenant, Arg.Any<CancellationToken>()).Returns(instance);

        var response = await ClientFor(OtAdminToken()).PostAsync(Url(id), null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("\"error\":\"sin_adjuntos\"");
    }

    [Fact]
    public async Task SuperAdminConTransitOfficeId_LlegaAlTramiteDelOrganismoIndicado()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        Accesible(id, TenantSuperAdmin, Organismo);
        _factory.Catalog.Exists(Organismo).Returns(true);
        var instance = _factory.Instance(id, ClientTenant, ("soat", "soat.pdf"));
        _factory.TramitesRepo.GetByIdWithChecklistGraphAsync(id, ClientTenant, Arg.Any<CancellationToken>()).Returns(instance);

        var response = await ClientFor(SuperAdminToken()).PostAsync($"{Url(id)}?transitOfficeId={Organismo}", null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        await _factory.OtRepo.Received(1).GetByIdAsync(TenantSuperAdmin, id, Organismo, Arg.Any<CancellationToken>());
        await _factory.Guard.Received(1).ValidateActionAsync(TenantSuperAdmin, "generar_consolidado_maestro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuperAdminConTransitOfficeIdFueraDelCatalogo_400()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.Catalog.Exists(Arg.Any<Guid>()).Returns(false);

        var response = await ClientFor(SuperAdminToken())
            .PostAsync($"{Url(Guid.NewGuid())}?transitOfficeId={Organismo}", null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await _factory.OtRepo.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default, default(Guid?), default);
    }

    [Fact]
    public async Task OtAdminConTransitOfficeId_IgnoraElOverride()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        _factory.OtRepo.GetByIdAsync(TenantOt, id, null, Arg.Any<CancellationToken>()).Returns((OtClientProcedure?)null);

        var response = await ClientFor(OtAdminToken()).PostAsync($"{Url(id)}?transitOfficeId={Organismo}", null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _factory.OtRepo.Received(1).GetByIdAsync(TenantOt, id, null, Arg.Any<CancellationToken>());
        _factory.Catalog.DidNotReceiveWithAnyArgs().Exists(default);
    }

    private OtClientProcedure Accesible(Guid id, Guid otTenant, Guid? overrideOffice)
    {
        var access = new OtClientProcedure
        {
            Id = id,
            ClientTenantId = ClientTenant,
            ProcedureTypeId = Guid.NewGuid(),
            TransitOfficeId = Organismo,
        };
        _factory.OtRepo.GetByIdAsync(otTenant, id, overrideOffice, Arg.Any<CancellationToken>())
            .Returns(_ => { _factory.Log.Add("acceso"); return access; });
        return access;
    }

    private static IReadOnlyList<ResolvedDocumentMatrixItem> Matriz(params string[] codigos) =>
        codigos.Select(c => new ResolvedDocumentMatrixItem { Codigo = c }).ToList();

    private static string Url(Guid id) => $"/api/v1/admin/ot/client-procedures/{id}/consolidado-maestro";

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string OtAdminToken() => Token("ot_admin", TenantOt);

    private static string SuperAdminToken() => Token("SuperAdmin", TenantSuperAdmin);

    private static string Token(string role, Guid tenantId) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "33333333-3333-3333-3333-333333333333"),
                new Claim("role", role),
                new Claim("tenant_id", tenantId.ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });

    /// <summary>
    /// Host real con los puertos del endpoint sustituidos. Los sustitutos se recrean por test
    /// (<see cref="Reset"/>); el registro de DI los lee en cada scope.
    /// </summary>
    public sealed class OtConsolidadoFactory : WebApplicationFactory<Program>
    {
        public IOtClientProcedureRepository OtRepo { get; private set; } = null!;
        public IQuipuxReadOnlyGuard Guard { get; private set; } = null!;
        public IResolvedDocumentMatrixResolver Matrix { get; private set; } = null!;
        public ITransitOfficeCatalog Catalog { get; private set; } = null!;
        public IProcedureInstanceRepository TramitesRepo { get; private set; } = null!;
        public FakeStorage Storage { get; private set; } = null!;
        public List<string> Log { get; private set; } = [];
        public bool EnScopeCliente { get; private set; }

        public OtConsolidadoFactory() => Reset();

        public void Reset()
        {
            Log = [];
            EnScopeCliente = false;
            OtRepo = Substitute.For<IOtClientProcedureRepository>();
            Guard = Substitute.For<IQuipuxReadOnlyGuard>();
            Matrix = Substitute.For<IResolvedDocumentMatrixResolver>();
            Catalog = Substitute.For<ITransitOfficeCatalog>();
            TramitesRepo = Substitute.For<IProcedureInstanceRepository>();
            Storage = new FakeStorage();

            Guard.ValidateActionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    Log.Add($"guard:{ci.ArgAt<string>(1)}");
                    return QuipuxReadOnlyResult.Allowed();
                });
            Matrix.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<ResolvedDocumentMatrixItem>>([]));
            OtRepo.ExecuteInClientTenantScopeAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Func<Task<(GenerarConsolidadoResult?, string?)>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(async ci =>
                {
                    Log.Add($"scope:{ci.ArgAt<Guid>(0)}");
                    EnScopeCliente = true;
                    try
                    {
                        return await ci.ArgAt<Func<Task<(GenerarConsolidadoResult?, string?)>>>(1)();
                    }
                    finally
                    {
                        EnScopeCliente = false;
                    }
                });
        }

        public ProcedureInstance Instance(Guid id, Guid tenantId, params (string Tipo, string Filename)[] adjuntos)
        {
            var instance = new ProcedureInstance
            {
                ProcedureType = ProcedureTypeFixture.Matricula,
                Id = id,
                TenantId = tenantId,
                ProcedureTypeId = Guid.NewGuid(),
                ReferenceNumber = "TRM-2026-013389",
                Status = TramiteEstado.Entregado,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            foreach (var (tipo, filename) in adjuntos)
            {
                var path = $"{id:D}/{tipo}";
                var content = Encoding.UTF8.GetBytes($"%PDF-{filename}");
                Storage.Files[path] = content;
                instance.Attachments.Add(new ProcedureInstanceAttachment
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProcedureInstanceId = id,
                    Tipo = tipo,
                    Filename = filename,
                    Mimetype = "application/pdf",
                    SizeBytes = content.Length,
                    Sha256 = $"sha-{tipo}",
                    StoragePath = path,
                    Source = "user",
                    UploadedAt = DateTimeOffset.UtcNow,
                });
            }

            return instance;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOtClientProcedureRepository>();
                services.AddScoped(_ => OtRepo);
                services.RemoveAll<IQuipuxReadOnlyGuard>();
                services.AddScoped(_ => Guard);
                services.RemoveAll<IResolvedDocumentMatrixResolver>();
                services.AddScoped(_ => Matrix);
                services.RemoveAll<ITransitOfficeCatalog>();
                services.AddScoped(_ => Catalog);
                services.RemoveAll<GenerarConsolidadoMaestroHandler>();
                services.AddScoped(_ => new GenerarConsolidadoMaestroHandler(TramitesRepo, new FakeMerger(), Storage));
                services.AddScoped<ITenantScopeResolver>(_ => new FixedSingleScopeResolver());
            });
        }
    }

    private sealed class FixedSingleScopeResolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantScope.Single(tenantId));
    }

    private sealed class FakeMerger : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

        public byte[] Compose(MergeRequest request) => Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    /// <summary>Almacenamiento en memoria (mismo contrato que el de <c>ConsolidadoMaestroHandlerTests</c>).</summary>
    public sealed class FakeStorage : IAttachmentStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_{Files.Count}";
            Files[path] = ms.ToArray();
            return new StoredFile(path, $"sha-{tipo}", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Files.Remove(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }
}
