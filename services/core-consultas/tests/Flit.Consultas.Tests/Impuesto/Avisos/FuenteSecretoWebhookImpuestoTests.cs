using Flit.Consultas.Api.Impuesto;
using Flit.Consultas.Api.Impuesto.Avisos;
using Flit.Identidad.Grpc.V1;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto.Avisos;

/// <summary>
/// HU #14711 — secreto del webhook por gRPC con caché por empresa: una llamada por empresa mientras dure la caché; <c>forzar</c>
/// relee (firma que no coincide con la versión cacheada) pero no más de una vez cada 10 s; anterior vacío → null; Identidad caída
/// o denegando → <see cref="FalloSecretoWebhookImpuestoException"/>; los secretos no se registran.
/// <para>Uso de ejemplo: <c>await fuente.ObtenerAsync(empresa, forzar: false, ct)</c>.</para>
/// </summary>
public sealed class FuenteSecretoWebhookImpuestoTests : IDisposable
{
    private static readonly Guid Empresa = Guid.Parse("0194aaaa-0000-7000-8000-000000000001");
    private readonly RelojFalso _reloj = new();
    private readonly ClienteFalso _cliente = new();
    private readonly ListaLogger<FuenteSecretoWebhookImpuesto> _log = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FuenteSecretoWebhookImpuesto Fuente() => new(_cliente, _cache, _reloj, _log);

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task MapeaActualAnteriorVencimientoYVersion_YEnviaLaEmpresaEnLaMetadata()
    {
        var vence = _reloj.GetUtcNow().AddHours(3);
        _cliente.Respuestas.Enqueue(new ObtenerSecretoWebhookImpuestoResponse
        {
            Configurado = true, Actual = AvisosDePrueba.Secreto, Anterior = AvisosDePrueba.SecretoAnterior,
            AnteriorVenceEn = Timestamp.FromDateTimeOffset(vence), Version = 7,
        });

        var s = await Fuente().ObtenerAsync(Empresa, forzar: false, Ct);

        s.Should().Be(new SecretoWebhookImpuesto(true, AvisosDePrueba.Secreto, AvisosDePrueba.SecretoAnterior, vence, 7));
        _cliente.Empresas.Should().Equal(Empresa.ToString());
        s.ToString().Should().NotContain(AvisosDePrueba.Secreto);
    }

    [Fact]
    public async Task AnteriorVacio_EsNull_YSinSecreto_NoConfigurado()
    {
        _cliente.Respuestas.Enqueue(new ObtenerSecretoWebhookImpuestoResponse { Configurado = true, Actual = AvisosDePrueba.Secreto, Version = 2 });
        var otra = Guid.NewGuid();
        _cliente.Respuestas.Enqueue(new ObtenerSecretoWebhookImpuestoResponse { Configurado = false });

        var con = await Fuente().ObtenerAsync(Empresa, false, Ct);
        var sin = await Fuente().ObtenerAsync(otra, false, Ct);

        con.Anterior.Should().BeNull();
        con.AnteriorVenceEn.Should().BeNull();
        sin.Configurado.Should().BeFalse();
        sin.Actual.Should().BeNull();
    }

    [Fact]
    public async Task Cache_UnaLlamadaPorEmpresa_YForzarRelee()
    {
        _cliente.Respuestas.Enqueue(new ObtenerSecretoWebhookImpuestoResponse { Configurado = true, Actual = AvisosDePrueba.Secreto, Version = 1 });
        _cliente.Respuestas.Enqueue(new ObtenerSecretoWebhookImpuestoResponse { Configurado = true, Actual = "whsec_rotado_000000000000000000000000", Version = 2 });
        var fuente = Fuente();

        (await fuente.ObtenerAsync(Empresa, false, Ct)).Version.Should().Be(1);
        (await fuente.ObtenerAsync(Empresa, false, Ct)).Version.Should().Be(1);
        _cliente.Llamadas.Should().Be(1);

        _reloj.Avanzar(TimeSpan.FromSeconds(11));
        (await fuente.ObtenerAsync(Empresa, true, Ct)).Version.Should().Be(2);
        _cliente.Llamadas.Should().Be(2);
    }

    [Fact]
    public async Task ForzarSeguido_NoVuelveAIdentidadAntesDe10Segundos()
    {
        _cliente.Respuestas.Enqueue(new ObtenerSecretoWebhookImpuestoResponse { Configurado = true, Actual = AvisosDePrueba.Secreto, Version = 1 });
        var fuente = Fuente();

        await fuente.ObtenerAsync(Empresa, true, Ct);
        await fuente.ObtenerAsync(Empresa, true, Ct);
        await fuente.ObtenerAsync(Empresa, true, Ct);

        _cliente.Llamadas.Should().Be(1);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.Unauthenticated)]
    public async Task IdentidadCaidaODenegando_Fallo(StatusCode codigo)
    {
        _cliente.Lanzar = new RpcException(new Status(codigo, "detalle"));

        var act = () => Fuente().ObtenerAsync(Empresa, false, Ct);

        await act.Should().ThrowAsync<FalloSecretoWebhookImpuestoException>();
    }

    [Fact]
    public async Task NingunLogLlevaLosSecretos()
    {
        _cliente.Respuestas.Enqueue(new ObtenerSecretoWebhookImpuestoResponse { Configurado = true, Actual = AvisosDePrueba.Secreto, Anterior = AvisosDePrueba.SecretoAnterior, Version = 1 });
        await Fuente().ObtenerAsync(Empresa, false, Ct);
        _cliente.Lanzar = new RpcException(new Status(StatusCode.PermissionDenied, "x"));
        try
        {
            await Fuente().ObtenerAsync(Guid.NewGuid(), false, Ct);
        }
        catch (FalloSecretoWebhookImpuestoException)
        {
        }

        string.Join("|", _log.Textos).Should().NotContain(AvisosDePrueba.Secreto).And.NotContain(AvisosDePrueba.SecretoAnterior);
    }

    private sealed class ClienteFalso : ClienteSecretoWebhookImpuesto
    {
        public Queue<ObtenerSecretoWebhookImpuestoResponse> Respuestas { get; } = new();

        public RpcException? Lanzar { get; set; }

        public int Llamadas { get; private set; }

        public List<string?> Empresas { get; } = [];

        public override AsyncUnaryCall<ObtenerSecretoWebhookImpuestoResponse> ObtenerSecretoWebhookImpuestoAsync(
            ObtenerSecretoWebhookImpuestoRequest request, CallOptions options)
        {
            Llamadas++;
            Empresas.Add(options.Headers?.GetValue("x-flit-tenant-id"));
            var respuesta = Lanzar is not null
                ? Task.FromException<ObtenerSecretoWebhookImpuestoResponse>(Lanzar)
                : Task.FromResult(Respuestas.Count > 0 ? Respuestas.Dequeue() : new ObtenerSecretoWebhookImpuestoResponse());
            return new AsyncUnaryCall<ObtenerSecretoWebhookImpuestoResponse>(
                respuesta, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        }
    }
}

/// <summary>
/// HU #14711 — buzón aviso → sondeo: lo entregado se recoge una vez; gana el mayor attempt; quien espera despierta al llegar; sin
/// aviso vuelve null al terminar la espera; lo olvidado no se recoge.
/// <para>Uso de ejemplo: <c>buzon.Entregar(e, "ext-1", aviso); await buzon.EsperarAsync(e, "ext-1", espera, ct)</c>.</para>
/// </summary>
public sealed class BuzonAvisosImpuestoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AvisoImpuestoVerificado Aviso(int attempt) =>
        new(attempt, EstadoTrabajoExterno.Exitoso, AvisosDePrueba.Consulta("ext-1", attempt), DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Entregado_SeRecogeUnaSolaVez_YGanaElMayorAttempt()
    {
        var buzon = new BuzonAvisosImpuesto(TimeProvider.System);
        buzon.Entregar(Empresa, "ext-1", Aviso(2));
        buzon.Entregar(Empresa, "ext-1", Aviso(1));

        (await buzon.EsperarAsync(Empresa, "ext-1", TimeSpan.Zero, Ct))!.Attempt.Should().Be(2);
        (await buzon.EsperarAsync(Empresa, "ext-1", TimeSpan.Zero, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task QuienEspera_DespiertaAlLlegarElAviso()
    {
        var buzon = new BuzonAvisosImpuesto(TimeProvider.System);
        var espera = buzon.EsperarAsync(Empresa, "ext-1", TimeSpan.FromSeconds(30), Ct);
        espera.IsCompleted.Should().BeFalse();

        buzon.Entregar(Empresa, "ext-1", Aviso(1));

        var aviso = await espera.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        aviso!.Attempt.Should().Be(1);
    }

    [Fact]
    public async Task SinAviso_NullAlTerminarLaEspera_YOtraEmpresaNoLoRecoge()
    {
        var buzon = new BuzonAvisosImpuesto(TimeProvider.System);
        buzon.Entregar(Guid.NewGuid(), "ext-1", Aviso(1));

        (await buzon.EsperarAsync(Empresa, "ext-1", TimeSpan.FromMilliseconds(20), Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Olvidado_NoSeRecoge_YVencido_Tampoco()
    {
        var reloj = new RelojFalso();
        var buzon = new BuzonAvisosImpuesto(reloj);
        buzon.Entregar(Empresa, "ext-1", Aviso(1));
        buzon.Olvidar(Empresa, "ext-1");
        (await buzon.EsperarAsync(Empresa, "ext-1", TimeSpan.Zero, Ct)).Should().BeNull();

        buzon.Entregar(Empresa, "ext-2", Aviso(1));
        reloj.Avanzar(BuzonAvisosImpuesto.Retencion + TimeSpan.FromSeconds(1));
        (await buzon.EsperarAsync(Empresa, "ext-2", TimeSpan.Zero, Ct)).Should().BeNull();
    }
}
