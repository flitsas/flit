using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flit.Platform.Sdk.Tests.Messaging;

/// <summary>
/// HU #13727 (ADR-0072 R-2, Habeas Data) — retención opt-in de la outbox: con <c>Platform:Messaging:RetencionPublicados</c>
/// el SDK borra por lotes las filas YA publicadas antes de ahora − retención y nunca las pendientes; sin la opción no se
/// registra la purga y nada se borra (los demás servicios no cambian); un valor no positivo impide arrancar. Postgres real
/// (base efímera de <see cref="MessagingFixture"/>).
/// <para>Uso de ejemplo: <c>"Platform": { "Messaging": { "RetencionPublicados": "1.00:00:00" } }</c>.</para>
/// </summary>
public sealed class OutboxRetencionTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Retencion = TimeSpan.FromHours(24);

    public async ValueTask InitializeAsync()
    {
        if (fixture.SkipReason is not null)
            return;
        await using var db = fixture.NewDb();
        await db.Set<OutboxMessage>().ExecuteDeleteAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ConRetencion_BorraSoloLasPublicadasVencidas_PorLotes_YConservaRecientesYPendientes()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var ahora = TimeProvider.System.GetUtcNow();

        var vencidas = Enumerable.Range(0, 5).Select(i => Fila(ahora.AddDays(-3), publicada: ahora.AddHours(-25 - i))).ToList();
        var reciente = Fila(ahora.AddDays(-3), publicada: ahora.AddHours(-23));
        var pendienteVieja = Fila(ahora.AddDays(-30), publicada: null);
        var pendienteNueva = Fila(ahora, publicada: null);
        await using (var db = fixture.NewDb())
        {
            db.Set<OutboxMessage>().AddRange([.. vencidas, reciente, pendienteVieja, pendienteNueva]);
            await db.SaveChangesAsync(ct);
        }

        await using var services = Services(new() { ["Platform:Messaging:RetencionPublicados"] = "1.00:00:00" });
        services.GetRequiredService<PlatformMessagingOptions>().RetencionPublicados.Should().Be(Retencion);
        var purga = services.GetServices<IHostedService>().OfType<OutboxPurgeService<PruebaDb>>().Should().ContainSingle().Subject;

        (await purga.PurgarAsync(ct, lote: 2)).Should().Be(5, "cinco vencidas en lotes de dos: 2 + 2 + 1");

        await using var check = fixture.NewDb();
        (await check.Set<OutboxMessage>().Select(m => m.Id).ToListAsync(ct))
            .Should().BeEquivalentTo([reciente.Id, pendienteVieja.Id, pendienteNueva.Id]);
    }

    [Fact]
    public async Task SinRetencion_PorDefecto_NoSeRegistraLaPurga_YElPublicadorNoBorraNada()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var ahora = TimeProvider.System.GetUtcNow();
        var vieja = Fila(ahora.AddDays(-90), publicada: ahora.AddDays(-60));
        await using (var db = fixture.NewDb())
        {
            db.Set<OutboxMessage>().Add(vieja);
            await db.SaveChangesAsync(ct);
        }

        await using var services = Services([]);
        services.GetRequiredService<PlatformMessagingOptions>().RetencionPublicados.Should().BeNull("la retención es opt-in");
        services.GetServices<IHostedService>().OfType<OutboxPurgeService<PruebaDb>>().Should().BeEmpty();

        var publicador = services.GetServices<IHostedService>().OfType<OutboxPublisherService<PruebaDb>>().Single();
        (await publicador.PublishPendingAsync(ct)).Should().Be(0);

        await using var check = fixture.NewDb();
        (await check.Set<OutboxMessage>().Select(m => m.Id).ToListAsync(ct)).Should().Equal(vieja.Id);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-01:00:00")]
    public void RetencionNoPositiva_NoArranca(string valor)
    {
        var act = () => new ServiceCollection().AddFlitOutbox<PruebaDb>(Config(new() { ["Platform:Messaging:RetencionPublicados"] = valor }));
        act.Should().Throw<InvalidOperationException>().WithMessage("*RetencionPublicados*");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(60, 60)]
    [InlineData(24 * 60, 60)]
    public void LaPurgaCorreCadaHora_OCadaRetencionSiEsMenor(int retencionMinutos, int intervaloMinutos) =>
        OutboxPurgeService<PruebaDb>.Intervalo(TimeSpan.FromMinutes(retencionMinutos)).Should().Be(TimeSpan.FromMinutes(intervaloMinutos));

    private ServiceProvider Services(Dictionary<string, string?> extra)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PruebaDb>(o => o.UseNpgsql(fixture.Postgres));
        services.AddFlitOutbox<PruebaDb>(Config(extra));
        return services.BuildServiceProvider();
    }

    private IConfiguration Config(Dictionary<string, string?> extra)
    {
        var valores = new Dictionary<string, string?>
        {
            ["Platform:Messaging:Producer"] = MessagingFixture.Producer,
            ["Platform:Messaging:ConnectionString"] = fixture.RabbitMq,
        };
        foreach (var (clave, valor) in extra)
            valores[clave] = valor;
        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    private static OutboxMessage Fila(DateTimeOffset ocurrida, DateTimeOffset? publicada) => new()
    {
        Id = Guid.NewGuid(),
        Exchange = "flit.prueba",
        RoutingKey = "prueba.pedido.creado",
        Payload = "{}",
        OccurredAt = ocurrida,
        PublishedAt = publicada,
    };
}
