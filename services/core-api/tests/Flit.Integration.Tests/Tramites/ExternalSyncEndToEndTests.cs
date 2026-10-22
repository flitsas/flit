using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flit.Admin.Domain.Integrations;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.MarcaBlanca;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13081 de punta a punta: host HTTP real (<see cref="MarcaBlancaApiFactory"/>) contra Postgres con
/// todas las migraciones, pase externo real y el feed recorrido siguiendo <c>nextCursor</c> hasta el final,
/// como lo hará Flito. La ventana de estabilidad se pone en 0 para no esperar 5 s por página.
/// </summary>
public sealed class ExternalSyncEndToEndTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Url = "/api/v1/external/tramites/sync";
    private static readonly Guid CompaniaA = new("5a5a5a5a-0001-4000-8000-000000013081");
    private static readonly Guid CompaniaB = new("5a5a5a5a-0002-4000-8000-000000013081");
    private static readonly Guid Gestor = new("5a5a5a5a-0003-4000-8000-000000013081");

    [PostgresFact]
    public async Task AC1_ElFeedSeRecorrePorPaginasSinPerderNiRepetirYLuegoSoloTraeLoNuevo()
    {
        await SembrarAsync();
        var esperados = new List<Guid>();
        for (var n = 1; n <= 5; n++)
        {
            esperados.Add(await RadicadoAsync(n % 2 == 0 ? CompaniaB : CompaniaA, n));
        }

        await using var factory = Host();
        var cliente = Cliente(factory, ExternalScopes.Todos);

        var recibidos = new List<Guid>();
        var paginas = new List<int>();
        string? cursor = null;
        bool hayMas;
        do
        {
            var body = await GetAsync(cliente, cursor is null ? $"{Url}?pageSize=2" : $"{Url}?pageSize=2&cursor={cursor}");
            var items = body.GetProperty("items").EnumerateArray().ToList();
            paginas.Add(items.Count);
            recibidos.AddRange(items.Select(i => i.GetProperty("id").GetGuid()));
            cursor = body.GetProperty("nextCursor").GetString();
            hayMas = body.GetProperty("hasMore").GetBoolean();
        }
        while (hayMas);

        paginas.Should().Equal(2, 2, 1);
        recibidos.Should().Equal(esperados, "cada trámite una vez y en orden");

        (await GetAsync(cliente, $"{Url}?cursor={cursor}")).GetProperty("nextCursor").GetString()
            .Should().Be(cursor, "sin cambios el cursor no se mueve (AC2)");

        await EjecutarAsync("UPDATE tramites.procedure_instances SET status = 'aprobado' WHERE id = @id", esperados[1]);

        var siguiente = await GetAsync(cliente, $"{Url}?cursor={cursor}");
        siguiente.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(esperados[1]);
        siguiente.GetProperty("items")[0].GetProperty("estado").GetString().Should().Be("aprobado");
    }

    [PostgresFact]
    public async Task AC3_SinPermisoDeDatosPersonalesElFeedRealLlegaEnmascarado()
    {
        await SembrarAsync();
        var id = await RadicadoAsync(CompaniaA, 1);
        await EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_actors (tenant_id, procedure_instance_id, procedure_entity_id, actor_type, document_type, document_number, full_name, email) "
            + "SELECT tenant_id, id, (SELECT id FROM tramites.procedure_entities ORDER BY id LIMIT 1), 'comprador', 'CC', '900000000', 'PERSONA EJEMPLO', 'persona@ejemplo.test' "
            + "FROM tramites.procedure_instances WHERE id = @id",
            id);

        await using var factory = Host();
        var comprador = (await GetAsync(Cliente(factory, [ExternalScopes.TramitesRead]), Url))
            .GetProperty("items")[0].GetProperty("compradores")[0];

        comprador.GetProperty("numeroDocumento").GetString().Should().Be("9****0000");
        comprador.GetProperty("correo").GetString().Should().Be("p***@ejemplo.test");
        comprador.GetProperty("nombreCompleto").GetString().Should().Be("P*** E***");
    }

    [PostgresFact]
    public async Task ElArranquePorFechaSoloEntregaLoCambiadoDesdeEntoncesYSigueConElCursor()
    {
        await SembrarAsync();
        await RadicadoAsync(CompaniaA, 1);
        var desde = await AhoraAsync();
        var nuevo = await RadicadoAsync(CompaniaB, 2);

        await using var factory = Host();
        var cliente = Cliente(factory, ExternalScopes.Todos);
        var body = await GetAsync(cliente, $"{Url}?since={Uri.EscapeDataString(desde.ToString("O"))}");

        body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(nuevo);
        var cursor = body.GetProperty("nextCursor").GetString();
        (await GetAsync(cliente, $"{Url}?cursor={cursor}")).GetProperty("items").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// HU #13077 — paso 7 de la guía de consumo: la factura que anuncia el feed se descarga con la URL
    /// firmada de su <c>adjuntoId</c>. El file-manager es un doble que firma lo que le pidan.
    /// </summary>
    [PostgresFact]
    public async Task HU13077_LaFacturaQueAnunciaElFeedSeDescargaPorSuAdjuntoId()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync(CompaniaA, 1);
        await EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_attachments (tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path, uploaded_at) "
            + "SELECT tenant_id, id, 'factura', 'factura.pdf', 'application/pdf', 10, repeat('a', 64), 'fm-13077', now() "
            + "FROM tramites.procedure_instances WHERE id = @id",
            tramite);

        var almacen = new FirmaFija();
        await using var factory = Host().WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IAttachmentStorage>();
            s.AddSingleton<IAttachmentStorage>(almacen);
        }));
        var cliente = Cliente(factory, [ExternalScopes.TramitesRead]);

        var factura = (await GetAsync(cliente, Url)).GetProperty("items")[0].GetProperty("factura");
        var adjuntoId = factura.GetProperty("adjuntoId").GetGuid();
        var url = await GetAsync(cliente, $"/api/v1/external/tramites/{tramite}/adjuntos/{adjuntoId}/url");

        url.GetProperty("url").GetString().Should().Be("https://almacen.ejemplo.test/fm-13077?sig=x");
        url.GetProperty("nombreArchivo").GetString().Should().Be("factura.pdf");
        url.GetProperty("contentType").GetString().Should().Be("application/pdf");
        almacen.Pedidos.Should().Equal("fm-13077");
    }

    /// <summary>HU #13086 — la llamada real deja su fila en <c>integrations.external_access_log</c>.</summary>
    [PostgresFact]
    public async Task HU13086_CadaPaginaQuedaEnLaBitacoraConLasCompaniasTocadas()
    {
        await SembrarAsync();
        await RadicadoAsync(CompaniaA, 1);
        await RadicadoAsync(CompaniaB, 2);

        await using var factory = Host();
        var cliente = Cliente(factory, [ExternalScopes.TramitesRead]);
        cliente.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.86");
        await GetAsync(cliente, Url);

        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT client_id, endpoint, items_count, tenant_ids, pii_unmasked, http_status "
            + "FROM integrations.external_access_log WHERE ip = '203.0.113.86'::inet",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue("la solicitud quedó registrada");
        reader.GetString(0).Should().Be("flito-it");
        reader.GetString(1).Should().Be("tramites.sync");
        reader.GetInt32(2).Should().Be(2);
        reader.GetFieldValue<Guid[]>(3).Should().BeEquivalentTo([CompaniaA, CompaniaB]);
        reader.GetBoolean(4).Should().BeFalse("sin external.tramites.pii.read llegó enmascarado");
        reader.GetInt32(5).Should().Be(200);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeFalse("una fila por solicitud");
    }

    // ── Host, pase y siembra ────────────────────────────────────────────────

    private sealed class FirmaFija : IAttachmentStorage
    {
        public List<string> Pedidos { get; } = [];

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default)
        {
            Pedidos.Add(storagePath);
            return Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(
                ($"https://almacen.ejemplo.test/{storagePath}?sig=x", DateTimeOffset.UtcNow.AddMinutes(10)));
        }

        public Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private WebApplicationFactory<Program> Host() =>
        new MarcaBlancaApiFactory(Fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalClients:StabilityLagSeconds"] = "0" })));

    private static HttpClient Cliente(WebApplicationFactory<Program> factory, IReadOnlyList<string> scopes)
    {
        using var scope = factory.Services.CreateScope();
        var pase = scope.ServiceProvider.GetRequiredService<IExternalClientTokenIssuer>().Issue("flito-it", scopes).Token;
        var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        return cliente;
    }

    private static async Task<JsonElement> GetAsync(HttpClient cliente, string url)
    {
        var response = await cliente.GetAsync(url, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
    }

    private async Task SembrarAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(CompaniaA, "IT-13081-A", isGroupParent: false, parentId: null));
        ctx.Tenants.Add(TenantSeed.New(CompaniaB, "IT-13081-B", isGroupParent: false, parentId: null));
        await ctx.SaveChangesAsync();
        ctx.Users.Add(new User
        {
            Id = Gestor,
            Email = "it-13081@flit.test",
            DisplayName = "Gestor 13081",
            Status = "active",
            HomeTenantId = CompaniaA,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> RadicadoAsync(Guid tenant, int n)
    {
        var id = Guid.CreateVersion7();
        await using (var ctx = NewContext())
        {
            var tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();
            ctx.ProcedureInstances.Add(new ProcedureInstance
            {
                Id = id,
                TenantId = tenant,
                ProcedureTypeId = tipo,
                ReferenceNumber = $"IT13081-{n}",
                Status = TramiteEstado.Borrador,
                Vin = $"VIN13081{n:D9}",
                CreatedByUserId = Gestor,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) "
            + "SELECT tenant_id, id, 'entregado' FROM tramites.procedure_instances WHERE id = @id",
            id);
        return id;
    }

    private async Task<DateTimeOffset> AhoraAsync()
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT clock_timestamp()", conn);
        return new DateTimeOffset((DateTime)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!, TimeSpan.Zero);
    }

    private async Task EjecutarAsync(string sql, Guid id)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
