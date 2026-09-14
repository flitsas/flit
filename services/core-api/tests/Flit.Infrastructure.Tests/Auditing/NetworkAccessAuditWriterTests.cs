using Flit.Infrastructure.Auditing;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Auditing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Infrastructure.Tests.Auditing;

/// <summary>
/// HU #12361 (Feature #12257) — <see cref="NetworkAccessAuditWriter"/>: escritura con scope DI propio
/// sobre <c>tramites.network_access_audit</c> (AC1/AC5/AC7) que nunca propaga y devuelve si la fila
/// quedó persistida (fail-closed en descargas, PR #370). Sin PostgreSQL (InMemory).
/// Uso de ejemplo: <c>var ok = await writer.WriteAsync(new NetworkAccessAuditEntry(user, P, [C1], "network.attachments.download", null, tramite, C1, anexo, "forbidden"))</c>.
/// </summary>
public sealed class NetworkAccessAuditWriterTests
{
    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid C2 = Guid.Parse("a0000000-0000-4000-8000-000000001200");
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ServiceProvider BuildProvider(string dbName, bool withDbContext = true)
    {
        var services = new ServiceCollection();
        if (withDbContext)
            services.AddDbContext<FlitDbContext>(options => options.UseInMemoryDatabase(dbName));
        return services.BuildServiceProvider();
    }

    private static NetworkAccessAuditWriter Writer(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<NetworkAccessAuditWriter>.Instance);

    [Fact]
    public async Task WriteAsync_persiste_una_fila_con_los_hijos_distintos_y_sin_la_cabeza()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider(nameof(WriteAsync_persiste_una_fila_con_los_hijos_distintos_y_sin_la_cabeza));

        var written = await Writer(provider).WriteAsync(new NetworkAccessAuditEntry(
            User, P, [P, C1, C1, C2, Guid.Empty], NetworkAccessVocabulary.Resources.InstancesSearch,
            "{\"take\":50}", null, null, null, NetworkAccessVocabulary.Results.Ok), ct);

        written.Should().BeTrue();
        await using var verify = provider.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();
        var row = await verify.NetworkAccessAuditEntries.SingleAsync(ct);
        row.Id.Should().NotBeEmpty("la clave la genera el proveedor (DEFAULT uuidv7() en PostgreSQL), no el writer");
        row.ActorUserId.Should().Be(User);
        row.ActorTenantId.Should().Be(P);
        row.ReachedTenantIds.Should().BeEquivalentTo([C1, C2]);
        row.Resource.Should().Be(NetworkAccessVocabulary.Resources.InstancesSearch);
        row.Filters.Should().Be("{\"take\":50}");
        row.Result.Should().Be(NetworkAccessVocabulary.Results.Ok);
        row.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task WriteAsync_sin_hijos_alcanzados_no_escribe_nada_AC5()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider(nameof(WriteAsync_sin_hijos_alcanzados_no_escribe_nada_AC5));

        (await Writer(provider).WriteAsync(new NetworkAccessAuditEntry(
            User, P, [P], NetworkAccessVocabulary.Resources.InstancesSearch, null, null, null, null, NetworkAccessVocabulary.Results.Ok), ct))
            .Should().BeTrue("nada que registrar no es un fallo");
        (await Writer(provider).WriteAsync(new NetworkAccessAuditEntry(
            User, P, [], NetworkAccessVocabulary.Resources.StatsOverview, null, null, null, null, NetworkAccessVocabulary.Results.Ok), ct))
            .Should().BeTrue();

        await using var verify = provider.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();
        (await verify.NetworkAccessAuditEntries.CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_una_excepcion_del_DbContext_no_se_propaga_y_devuelve_false()
    {
        var ct = TestContext.Current.CancellationToken;
        // Sin FlitDbContext registrado: GetRequiredService lanza dentro del writer.
        await using var provider = BuildProvider("sin-contexto", withDbContext: false);

        var act = () => Writer(provider).WriteAsync(new NetworkAccessAuditEntry(
            User, P, [C1], NetworkAccessVocabulary.Resources.InstancesDetail, null, Guid.NewGuid(), C1, null, NetworkAccessVocabulary.Results.Ok), ct);

        var written = await act.Should().NotThrowAsync("auditar nunca lanza; el llamante decide con el bool");
        written.Which.Should().BeFalse("la descarga de documentos se retiene (fail-closed) cuando no hay rastro");
    }

    [Fact]
    public async Task WriteAsync_registra_el_intento_rechazado_de_descarga_con_tramite_documento_y_hijo_AC7()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider(nameof(WriteAsync_registra_el_intento_rechazado_de_descarga_con_tramite_documento_y_hijo_AC7));
        var procedure = Guid.NewGuid();
        var attachment = Guid.NewGuid();

        // Misma entrada que arma NetworkAccessAuditFilter para la ruta de descarga (HU #12410).
        var written = await Writer(provider).WriteAsync(new NetworkAccessAuditEntry(
            User, P, [C1], NetworkAccessVocabulary.Resources.AttachmentsDownload, null,
            procedure, C1, attachment, NetworkAccessVocabulary.Results.Forbidden), ct);

        written.Should().BeTrue();
        await using var verify = provider.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();
        var row = await verify.NetworkAccessAuditEntries.SingleAsync(ct);
        row.ActorUserId.Should().Be(User);
        row.ActorTenantId.Should().Be(P);
        row.ProcedureTenantId.Should().Be(C1);
        row.ReachedTenantIds.Should().Equal(C1);
        row.ProcedureId.Should().Be(procedure);
        row.AttachmentId.Should().Be(attachment);
        row.Resource.Should().Be(NetworkAccessVocabulary.Resources.AttachmentsDownload);
        row.Result.Should().Be(NetworkAccessVocabulary.Results.Forbidden);
        row.Filters.Should().BeNull();
    }
}
