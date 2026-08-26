using Flit.Infrastructure.Notifications.Renting;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Renting;

/// <summary>
/// HU #11360 AC1/AC2/AC3/AC6 — caché de token del canal Renting.
/// <para>
/// Uso de ejemplo del componente bajo prueba:
/// <code>
/// var cache = new RentingTokenCache(loginClient, Options.Create(channelOptions), timeProvider);
/// var token = await cache.GetTokenAsync(cancellationToken);
/// </code>
/// </para>
/// <para>
/// Reloj: <see cref="ManualTimeProvider"/> (subclase local de <see cref="TimeProvider"/>) en vez
/// del reloj del sistema — permite adelantar el TTL de AC2/AC3 sin dormir 600s reales. No se usó
/// el paquete <c>Microsoft.Extensions.TimeProvider.Testing</c> porque no está en
/// <c>Directory.Packages.props</c> del repo y una subclase de 6 líneas cubre exactamente lo que
/// necesitan estas pruebas (override de <see cref="TimeProvider.GetUtcNow"/>).
/// </para>
/// </summary>
public sealed class RentingTokenCacheTests
{
    // ── AC1 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTokenAsync_DiezConcurrentesConCacheFria_ProducenExactamenteUnLoginYElMismoToken()
    {
        var loginClient = new GatedLoginClient("token-ac1-compartido");
        using var cache = NewCache(loginClient, ttlSeconds: 600);

        // Diez peticiones concurrentes de token con la caché fría (sin ningún GetTokenAsync previo).
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => cache.GetTokenAsync(TestContext.Current.CancellationToken))
            .ToArray();

        // Da tiempo a que las diez lleguen al gate del login (SemaphoreSlim) ANTES de liberarlo:
        // si la anti-estampida fallara, esto haría evidente más de un login en paralelo.
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        loginClient.Release();

        var tokens = await Task.WhenAll(tasks);

        loginClient.CallCount.Should().Be(1, "diez llamadas concurrentes con caché fría deben producir un único login");
        tokens.Should().OnlyContain(t => t == "token-ac1-compartido", "las diez deben recibir el MISMO token");
    }

    // ── AC2 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTokenAsync_DentroDelTtlConfigurado_ReutilizaSinLlamarAlProveedor()
    {
        var loginClient = new SequentialLoginClient("token-v1", "token-v2");
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var cache = NewCache(loginClient, ttlSeconds: 600, time);

        var first = await cache.GetTokenAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(599)); // dentro del TTL de 600s configurado

        var second = await cache.GetTokenAsync(TestContext.Current.CancellationToken);

        second.Should().Be(first);
        loginClient.CallCount.Should().Be(1, "dentro del TTL no debe volver a llamar al proveedor");
    }

    // ── AC3 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTokenAsync_TrasVencerElTtlConfigurado_EjecutaLoginNuevoYLoAlmacena()
    {
        var loginClient = new SequentialLoginClient("token-v1", "token-v2");
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var cache = NewCache(loginClient, ttlSeconds: 600, time);

        var first = await cache.GetTokenAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(601)); // supera el TTL de 600s configurado

        var second = await cache.GetTokenAsync(TestContext.Current.CancellationToken);
        var third = await cache.GetTokenAsync(TestContext.Current.CancellationToken); // reutiliza el nuevo, sin tercer login

        first.Should().Be("token-v1");
        second.Should().Be("token-v2");
        third.Should().Be("token-v2");
        loginClient.CallCount.Should().Be(2, "vencido el TTL se loguea una vez y el resultado se cachea de nuevo");
    }

    // ── AC4 (apoyo — Invalidate es lo que el ejecutor usa antes de reintentar) ──────

    [Fact]
    public async Task Invalidate_ConElTokenVigente_ForzaUnLoginNuevoEnLaSiguienteLlamada()
    {
        var loginClient = new SequentialLoginClient("token-v1", "token-v2");
        using var cache = NewCache(loginClient, ttlSeconds: 600);

        var first = await cache.GetTokenAsync(TestContext.Current.CancellationToken);
        cache.Invalidate(first);
        var second = await cache.GetTokenAsync(TestContext.Current.CancellationToken);

        second.Should().Be("token-v2");
        loginClient.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Invalidate_ConUnTokenQueYaNoEsElCacheado_NoFuerzaUnLoginNuevo()
    {
        var loginClient = new SequentialLoginClient("token-v1", "token-v2");
        using var cache = NewCache(loginClient, ttlSeconds: 600);

        var first = await cache.GetTokenAsync(TestContext.Current.CancellationToken);
        cache.Invalidate("token-que-ya-no-esta-cacheado"); // p. ej. dos 401 concurrentes, otro ya refrescó
        var second = await cache.GetTokenAsync(TestContext.Current.CancellationToken);

        second.Should().Be(first);
        loginClient.CallCount.Should().Be(1, "invalidar un token que ya no es el cacheado no debe forzar un login");
    }

    // ── AC6 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTokenAsync_CuandoElLoginFalla_PropagaRentingAuthenticationExceptionSinDevolverNull()
    {
        var loginClient = new ThrowingLoginClient(new RentingAuthenticationException("Renting: login rechazado (HTTP 401)."));
        using var cache = NewCache(loginClient, ttlSeconds: 600);

        var act = async () => await cache.GetTokenAsync(TestContext.Current.CancellationToken);

        // AC6 — el fallo de login NUNCA se traduce en un token nulo devuelto como "éxito parcial"
        // (ese es exactamente el defecto de FasecoldaTokenCache que esta HU corrige).
        await act.Should().ThrowAsync<RentingAuthenticationException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static RentingTokenCache NewCache(IRentingLoginClient loginClient, int ttlSeconds, TimeProvider? time = null) =>
        new(loginClient, Options.Create(new RentingChannelOptions { LoginCacheSecondsTtl = ttlSeconds }),
            time ?? TimeProvider.System);

    /// <summary>Reloj manual: <see cref="Advance"/> mueve el "ahora" sin dormir tiempo real (AC2/AC3).</summary>
    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    /// <summary>Login controlado por un gate manual — para forzar solapamiento real en AC1.</summary>
    private sealed class GatedLoginClient(string token) : IRentingLoginClient
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public int CallCount => _callCount;

        public void Release() => _gate.TrySetResult();

        public async Task<RentingLoginResult> LoginAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            await _gate.Task;
            return new RentingLoginResult(token, Refresh: null);
        }
    }

    /// <summary>Devuelve los tokens de <paramref name="tokens"/> en orden, uno por cada login.</summary>
    private sealed class SequentialLoginClient(params string[] tokens) : IRentingLoginClient
    {
        private readonly Queue<string> _tokens = new(tokens);
        private int _callCount;

        public int CallCount => _callCount;

        public Task<RentingLoginResult> LoginAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            var token = _tokens.Count > 0 ? _tokens.Dequeue() : tokens[^1];
            return Task.FromResult(new RentingLoginResult(token, Refresh: null));
        }
    }

    private sealed class ThrowingLoginClient(RentingAuthenticationException exception) : IRentingLoginClient
    {
        public Task<RentingLoginResult> LoginAsync(CancellationToken cancellationToken) => throw exception;
    }
}
