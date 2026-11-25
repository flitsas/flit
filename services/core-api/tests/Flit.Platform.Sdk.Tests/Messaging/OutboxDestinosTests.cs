using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Flit.Platform.Sdk.Tests.Messaging;

/// <summary>
/// HU #14694 (BLOQ-1 c) — el publicador de la outbox contra Postgres real y un publicador de prueba: un mensaje cuyo destino
/// (exchange + llave de ruteo) falla no detiene a los de otros destinos; los siguientes de ese mismo destino esperan, en orden, al
/// siguiente ciclo. Si el broker no se alcanza, el lote se corta como antes.
/// </summary>
/// <example>
/// <code>
/// var falla = new PublicadorDePrueba { DestinoQueFalla = ("flit.notificaciones", "notificaciones.email.send") };
/// var ciclo = await Publisher(falla).PublicarAsync(ct); // los de flit.prueba salen; los de notificaciones esperan
/// </code>
/// </example>
public sealed class OutboxDestinosTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>, IAsyncLifetime
{
    private static readonly (string Exchange, string RoutingKey) X = ("flit.notificaciones", "notificaciones.email.send");
    private static readonly (string Exchange, string RoutingKey) Y = ("flit.prueba", "prueba.pedido.creado");

    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        if (fixture.SkipReason is not null)
            return;

        var services = new ServiceCollection();
        services.AddDbContext<PruebaDb>(o => o.UseNpgsql(fixture.Postgres));
        services.AddSingleton(new PlatformMessagingOptions { Producer = MessagingFixture.Producer, ConnectionString = fixture.RabbitMq });
        _services = services.BuildServiceProvider();
        await using var db = fixture.NewDb();
        await db.Set<OutboxMessage>().ExecuteDeleteAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
    }

    [Fact]
    public async Task Un_fallo_en_el_destino_X_no_bloquea_los_mensajes_del_destino_Y()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var x1 = await EncolarAsync(X, 0, ct);
        var y1 = await EncolarAsync(Y, 1, ct);
        var x2 = await EncolarAsync(X, 2, ct);
        var y2 = await EncolarAsync(Y, 3, ct);
        var publicador = new PublicadorDePrueba { DestinoQueFalla = X };

        var ciclo = await Publisher(publicador).PublicarAsync(ct);

        ciclo.Should().Be(new CicloOutbox(2, Fallo: true), "salen los dos de Y y el ciclo avisa el fallo de X");
        publicador.Intentos.Should().Equal([x1, y1, y2], "tras el fallo de X1 no se intenta X2, pero Y sigue");
        await using var check = fixture.NewDb();
        var filas = await check.Set<OutboxMessage>().AsNoTracking().ToDictionaryAsync(m => m.Id, ct);
        filas[y1].PublishedAt.Should().NotBeNull();
        filas[y2].PublishedAt.Should().NotBeNull();
        filas[x1].PublishedAt.Should().BeNull();
        filas[x1].Attempts.Should().Be(1);
        filas[x1].LastError.Should().NotBeNullOrEmpty();
        filas[x2].PublishedAt.Should().BeNull();
        filas[x2].Attempts.Should().Be(0, "no se intentó: espera detrás de X1");
    }

    [Fact]
    public async Task Dentro_del_destino_X_se_conserva_el_orden_cuando_vuelve()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var x1 = await EncolarAsync(X, 0, ct);
        var x2 = await EncolarAsync(X, 1, ct);
        var y1 = await EncolarAsync(Y, 2, ct);
        var x3 = await EncolarAsync(X, 3, ct);

        await Publisher(new PublicadorDePrueba { DestinoQueFalla = X }).PublicarAsync(ct);
        var sano = new PublicadorDePrueba();
        var ciclo = await Publisher(sano).PublicarAsync(ct);

        ciclo.Should().Be(new CicloOutbox(3, Fallo: false));
        sano.Intentos.Should().Equal([x1, x2, x3], "X sale en su orden; Y ya había salido en el ciclo anterior");
        await using var check = fixture.NewDb();
        (await check.Set<OutboxMessage>().CountAsync(m => m.PublishedAt == null, ct)).Should().Be(0);
        (await check.Set<OutboxMessage>().SingleAsync(m => m.Id == y1, ct)).Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Con_el_broker_inalcanzable_el_lote_se_corta_sin_intentar_los_demas_destinos()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var x1 = await EncolarAsync(X, 0, ct);
        await EncolarAsync(Y, 1, ct);
        var caido = new PublicadorDePrueba { BrokerCaido = true };

        var ciclo = await Publisher(caido).PublicarAsync(ct);

        ciclo.Should().Be(new CicloOutbox(0, Fallo: true));
        caido.Intentos.Should().Equal([x1], "sin broker no tiene sentido intentar cada destino");
    }

    [Fact]
    public async Task Sesenta_mensajes_viejos_de_un_destino_caido_no_impiden_publicar_otro_destino_en_el_primer_ciclo()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var viejosDeX = new List<Guid>();
        for (var i = 0; i < 60; i++)
            viejosDeX.Add(await EncolarAsync(X, i, ct));
        var y1 = await EncolarAsync(Y, 100, ct);
        var publicador = new PublicadorDePrueba { DestinoQueFalla = X };

        var ciclo = await Publisher(publicador).PublicarAsync(ct);

        ciclo.Should().Be(new CicloOutbox(1, Fallo: true), "Y sale aunque los 50 más viejos del lote sean de X");
        publicador.Intentos.Should().Equal([viejosDeX[0], y1], "de X solo se intenta el primero; los demás esperan en orden");
        await using var check = fixture.NewDb();
        (await check.Set<OutboxMessage>().SingleAsync(m => m.Id == y1, ct)).PublishedAt.Should().NotBeNull();
        (await check.Set<OutboxMessage>().CountAsync(m => m.Exchange == X.Exchange && m.Attempts > 0, ct))
            .Should().Be(1, "solo X1 cuenta un intento");
    }

    [Fact]
    public async Task Un_destino_que_alcanza_el_umbral_de_intentos_deja_una_advertencia_sin_el_payload()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var umbral = new PlatformMessagingOptions().AttemptsWarningThreshold;
        await EncolarAsync(X, 0, ct, intentosPrevios: umbral - 1, payload: "{\"secreto\":\"no-loguear\"}");
        var log = new LogDePrueba();

        await Publisher(new PublicadorDePrueba { DestinoQueFalla = X }, log).PublicarAsync(ct);

        var aviso = log.Entradas.Should().ContainSingle(e => e.EventId == 7303).Subject;
        aviso.Level.Should().Be(LogLevel.Warning);
        aviso.Mensaje.Should().Contain(X.Exchange).And.Contain(X.RoutingKey).And.Contain(umbral.ToString(System.Globalization.CultureInfo.InvariantCulture));
        log.Entradas.Should().NotContain(e => e.Mensaje.Contains("no-loguear"), "el payload no va al log");
    }

    [Fact]
    public async Task Por_debajo_del_umbral_de_intentos_no_hay_advertencia_de_destino()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var umbral = new PlatformMessagingOptions().AttemptsWarningThreshold;
        await EncolarAsync(X, 0, ct, intentosPrevios: umbral - 2);
        var log = new LogDePrueba();

        await Publisher(new PublicadorDePrueba { DestinoQueFalla = X }, log).PublicarAsync(ct);

        log.Entradas.Should().NotContain(e => e.EventId == 7303);
    }

    private async Task<Guid> EncolarAsync((string Exchange, string RoutingKey) destino, int segundo, CancellationToken ct,
        int intentosPrevios = 0, string payload = "{}")
    {
        await using var db = fixture.NewDb();
        var id = Guid.CreateVersion7();
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = id,
            Exchange = destino.Exchange,
            RoutingKey = destino.RoutingKey,
            Payload = payload,
            OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(segundo),
            Attempts = intentosPrevios,
        });
        await db.SaveChangesAsync(ct);
        return id;
    }

    private OutboxPublisherService<PruebaDb> Publisher(IEventPublisher publisher, ILogger<OutboxPublisherService<PruebaDb>>? log = null) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            _services.GetRequiredService<PlatformMessagingOptions>(),
            TimeProvider.System,
            log ?? NullLogger<OutboxPublisherService<PruebaDb>>.Instance);

    /// <summary>Guarda nivel, id y mensaje formateado de cada entrada.</summary>
    private sealed class LogDePrueba : ILogger<OutboxPublisherService<PruebaDb>>
    {
        public List<(LogLevel Level, int EventId, string Mensaje)> Entradas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entradas.Add((logLevel, eventId.Id, formatter(state, exception)));
    }

    /// <summary>Registra el orden de los intentos y falla en <see cref="DestinoQueFalla"/> (o en todo, si el broker está caído).</summary>
    private sealed class PublicadorDePrueba : IEventPublisher
    {
        public (string Exchange, string RoutingKey)? DestinoQueFalla { get; init; }

        public bool BrokerCaido { get; init; }

        public List<Guid> Intentos { get; } = [];

        public Task PublishAsync(OutboxMessage message, CancellationToken ct)
        {
            Intentos.Add(message.Id);
            if (BrokerCaido)
                throw new BrokerUnreachableException(new InvalidOperationException("broker de prueba caído"));
            if (DestinoQueFalla is { } d && d.Exchange == message.Exchange && d.RoutingKey == message.RoutingKey)
                throw new InvalidOperationException("ACCESS_REFUSED de prueba");
            return Task.CompletedTask;
        }
    }
}
