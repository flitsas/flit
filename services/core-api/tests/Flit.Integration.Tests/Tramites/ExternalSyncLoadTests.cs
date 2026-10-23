using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flit.Admin.Domain.Integrations;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.MarcaBlanca;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13083 (Feature #13066, Épica #12737) — carga del feed: 50.000 trámites SINTÉTICOS de 5 compañías, con
/// comprador, datos del vehículo y factura, recorridos por el endpoint HTTP real en páginas de 1.000 (la
/// consulta completa del ítem, el enmascarado, la serialización y la bitácora). El p95 por página debe quedar
/// por debajo de 1,5 s (AC2). Los datos no se parecen a datos reales (AC3): documentos del rango 8xxxxxxxxx,
/// nombres «PERSONA SINTETICA n» y correos en ejemplo.test. La de 50.000 es opt-in (<see cref="CargaFactAttribute"/>);
/// la de datos sintéticos corre siempre con 2.000. Se marcan por el VIN (<c>SINTVIN…</c>): el radicado
/// lo reescribe un trigger.
/// </summary>
public sealed class ExternalSyncLoadTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const int Total = 50_000;
    private const int PageSize = 1_000;
    private static readonly TimeSpan P95Maximo = TimeSpan.FromSeconds(1.5);
    private static readonly Guid Gestor = new("5a5a5a5a-00ff-4000-8000-000000013083");
    private static readonly Guid[] Companias = Enumerable.Range(1, 5)
        .Select(n => new Guid($"5a5a5a5a-00{n:D2}-4000-8000-0000000c3083"))
        .ToArray();

    [CargaFact]
    public async Task AC2_ConCincuentaMilTramitesElP95DeUnaPaginaDeMilQuedaPorDebajoDeUnSegundoYMedio()
    {
        var siembra = Stopwatch.StartNew();
        await SembrarAsync();
        siembra.Stop();
        await using var factory = Host();
        var cliente = Cliente(factory);
        await cliente.GetAsync("/api/v1/external/tramites/sync?pageSize=1", TestContext.Current.CancellationToken); // calentamiento (JIT y pool)

        var tiempos = new List<TimeSpan>();
        var recibidos = 0;
        string? cursor = null;
        bool hayMas;
        do
        {
            var url = "/api/v1/external/tramites/sync?pageSize=" + PageSize + (cursor is null ? string.Empty : "&cursor=" + cursor);
            var reloj = Stopwatch.StartNew();
            var response = await cliente.GetAsync(url, TestContext.Current.CancellationToken);
            var cuerpo = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            reloj.Stop();
            response.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
            tiempos.Add(reloj.Elapsed);

            var pagina = JsonDocument.Parse(cuerpo).RootElement;
            recibidos += pagina.GetProperty("items").GetArrayLength();
            cursor = pagina.GetProperty("nextCursor").GetString();
            hayMas = pagina.GetProperty("hasMore").GetBoolean();
        }
        while (hayMas);

        recibidos.Should().Be(Total, "el recorrido entrega los 50.000 trámites");
        var ordenados = tiempos.OrderBy(t => t).ToList();
        var p95 = ordenados[(int)Math.Ceiling(0.95 * ordenados.Count) - 1];
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"HU13083 carga: siembra={siembra.Elapsed.TotalSeconds:F0}s paginas={tiempos.Count} de {PageSize} "
            + $"p50={ordenados[ordenados.Count / 2].TotalMilliseconds:F0}ms p95={p95.TotalMilliseconds:F0}ms max={ordenados[^1].TotalMilliseconds:F0}ms");
        p95.Should().BeLessThan(P95Maximo);
    }

    [PostgresFact]
    public async Task AC3_LosDatosDePruebaSonSinteticosYNoContienenDatosPersonalesReales()
    {
        await SembrarAsync(cuantos: 2_000);

        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT count(*) FILTER (WHERE a.document_number !~ '^8[0-9]{9}$'),
                   count(*) FILTER (WHERE a.full_name !~ '^PERSONA SINTETICA [0-9]+$'),
                   count(*) FILTER (WHERE a.email !~ '@ejemplo\.test$'),
                   count(*) FILTER (WHERE a.phone !~ '^399[0-9]{7}$'),
                   count(*) FILTER (WHERE a.metadata->>'direccion' !~ '^CALLE SINTETICA [0-9]+$'),
                   count(*)
              FROM tramites.procedure_instance_actors a
              JOIN tramites.procedure_instances p ON p.id = a.procedure_instance_id
             WHERE p.vin LIKE 'SINTVIN%'
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        reader.GetInt64(5).Should().Be(2_000, "un comprador sintético por trámite");
        for (var i = 0; i < 5; i++)
        {
            reader.GetInt64(i).Should().Be(0, "todo dato personal sigue el patrón sintético");
        }
    }

    // ── Siembra sintética por SQL (segundos, no minutos) ────────────────────────

    private async Task SembrarAsync(int cuantos = Total)
    {
        await using (var ctx = NewContext())
        {
            for (var i = 0; i < Companias.Length; i++)
            {
                ctx.Tenants.Add(TenantSeed.New(Companias[i], $"IT-13083-C{i + 1}", isGroupParent: false, parentId: null));
            }

            await ctx.SaveChangesAsync();
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13083-carga@flit.test",
                DisplayName = "Gestor sintético 13083",
                Status = "active",
                HomeTenantId = Companias[0],
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
            await ctx.SaveChangesAsync();
        }

        await using var conn = await Fixture.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        foreach (var sql in new[]
                 {
                     """
                     INSERT INTO tramites.procedure_instances
                         (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
                     SELECT uuidv7(), (@companias)[1 + n % 5],
                            (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1),
                            'SINT-' || n, 'borrador', 'SINTVIN' || lpad(n::text, 10, '0'), @gestor, now()
                       FROM generate_series(1, @cuantos) n
                     """,
                     """
                     INSERT INTO tramites.procedure_instance_actors
                         (tenant_id, procedure_instance_id, procedure_entity_id, actor_type, document_type, document_number,
                          full_name, email, phone, ordinal, person_type, metadata)
                     SELECT p.tenant_id, p.id, (SELECT id FROM tramites.procedure_entities ORDER BY id LIMIT 1), 'comprador', 'CC',
                            '8' || lpad(s.n::text, 9, '0'), 'PERSONA SINTETICA ' || s.n, 'sintetica' || s.n || '@ejemplo.test',
                            '399' || lpad((s.n % 10000000)::text, 7, '0'), 1, 'natural',
                            jsonb_build_object('direccion', 'CALLE SINTETICA ' || s.n, 'ciudad', 'CIUDAD SINTETICA')
                       FROM tramites.procedure_instances p
                       JOIN LATERAL (SELECT substr(p.vin, 8)::int AS n) s ON true
                      WHERE p.vin LIKE 'SINTVIN%'
                     """,
                     """
                     INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text)
                     SELECT p.tenant_id, p.id, k.clave, k.valor
                       FROM tramites.procedure_instances p
                      CROSS JOIN (VALUES ('vehicle_brand', 'MARCA SINTETICA'), ('vehicle_line', 'LINEA SINTETICA'),
                                         ('vehicle_year', '2026'), ('vehicle_class', 'AUTOMOVIL'),
                                         ('vehicle_engine_displacement', '1600'), ('vehicle_service', 'PARTICULAR')) k(clave, valor)
                      WHERE p.vin LIKE 'SINTVIN%'
                     """,
                     """
                     INSERT INTO tramites.procedure_instance_attachments
                         (tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path, uploaded_at)
                     SELECT tenant_id, id, 'factura', 'factura.pdf', 'application/pdf', 10, repeat('a', 64), 'sintetico/' || id, now()
                       FROM tramites.procedure_instances
                      WHERE vin LIKE 'SINTVIN%' AND substr(vin, 8)::int % 3 = 0
                     """,
                     // Como en el flujo real: los datos se cargan en borrador y luego el trámite se radica.
                     """
                     INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status)
                     SELECT tenant_id, id, 'entregado' FROM tramites.procedure_instances WHERE vin LIKE 'SINTVIN%'
                     """,
                     "UPDATE tramites.procedure_instances SET status = 'entregado' WHERE vin LIKE 'SINTVIN%'",
                 })
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("companias", Companias);
            cmd.Parameters.AddWithValue("gestor", Gestor);
            cmd.Parameters.AddWithValue("cuantos", cuantos);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await tx.CommitAsync(TestContext.Current.CancellationToken);

        // Como lo haría autovacuum en un ambiente real: estadísticas al día para el planificador.
        await using var analyze = new NpgsqlCommand(
            "ANALYZE tramites.procedure_instances, tramites.procedure_instance_status_history, tramites.procedure_instance_actors, "
            + "tramites.procedure_instance_field_values, tramites.procedure_instance_attachments", conn) { CommandTimeout = 600 };
        await analyze.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private WebApplicationFactory<Program> Host() =>
        new MarcaBlancaApiFactory(Fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalClients:StabilityLagSeconds"] = "0" })));

    private static HttpClient Cliente(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var pase = scope.ServiceProvider.GetRequiredService<IExternalClientTokenIssuer>().Issue("flito-carga", ExternalScopes.Todos).Token;
        var cliente = factory.CreateClient();
        cliente.Timeout = TimeSpan.FromMinutes(2);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        return cliente;
    }
}
