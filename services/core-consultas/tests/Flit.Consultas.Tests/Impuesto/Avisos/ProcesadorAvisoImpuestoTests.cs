using Flit.Consultas.Api.Impuesto;
using Flit.Consultas.Api.Impuesto.Avisos;
using FluentAssertions;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto.Avisos;

/// <summary>
/// HU #14711 (plan §3.3, guía §6) — receptor del aviso: token → empresa o 401; firma con el secreto actual y luego el anterior vigente
/// (relee el secreto una vez si no coincide); deduplicación por <c>X-Flit-Delivery</c> (o <c>sin-delivery:{consulta}:{attempt}</c>);
/// consulta de otra empresa o ya cerrada se confirma con 200 y se descarta; el buzón se queda con el mayor <c>attempt</c>. Nunca
/// registra secreto, firma ni token.
/// <para>Uso de ejemplo: <c>await procesador.ProcesarAsync(token, new CabecerasAvisoImpuesto(ts, firma, delivery, evento), cuerpo, ct)</c>.</para>
/// </summary>
public sealed class ProcesadorAvisoImpuestoTests
{
    private static readonly Guid Empresa = Guid.Parse("0194aaaa-0000-7000-8000-000000000001");
    private static readonly Guid Otra = Guid.Parse("0194bbbb-0000-7000-8000-000000000002");
    private static readonly Guid Trabajo = Guid.Parse("0194cccc-0000-7000-8000-000000000003");

    private readonly RelojFalso _reloj = new();
    private readonly SeguimientoEnMemoria _seguimiento = new();
    private readonly FuenteSecretoFalsa _secretos = new();
    private readonly ListaLogger<ProcesadorAvisoImpuesto> _log = new();
    private readonly RegistroEnMemoria _registro;
    private readonly BuzonAvisosImpuesto _buzon;
    private readonly string _token = TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave);

    public ProcesadorAvisoImpuestoTests()
    {
        _registro = new RegistroEnMemoria(_seguimiento);
        _buzon = new BuzonAvisosImpuesto(_reloj);
        _secretos.PorEmpresa[Empresa] = new SecretoWebhookImpuesto(true, AvisosDePrueba.Secreto, null, null, 1);
        _seguimiento.Sembrar(new SeguimientoImpuesto(Trabajo, "ext-1", _reloj.GetUtcNow(), "queued"));
        _registro.Empresas[Trabajo] = Empresa;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ProcesadorAvisoImpuesto Procesador(OpcionesAvisoImpuesto? opciones = null) =>
        new(opciones ?? OpcionesAvisoImpuesto.Crear("https://consultas.example", AvisosDePrueba.Llave), _secretos, _registro, _buzon, _reloj, _log);

    private Task<int> Enviar(byte[] cuerpo, string? secreto = AvisosDePrueba.Secreto, string? delivery = "d-1", string? token = null,
        string evento = "consulta.finalizada", int desfase = 0, ProcesadorAvisoImpuesto? procesador = null)
    {
        var ts = AvisosDePrueba.Ts(_reloj.GetUtcNow().AddSeconds(desfase));
        var firma = secreto is null ? null : AvisosDePrueba.Firmar(secreto, ts, cuerpo);
        return (procesador ?? Procesador()).ProcesarAsync(token ?? _token, new CabecerasAvisoImpuesto(ts, firma, delivery, evento), cuerpo, Ct);
    }

    private async Task<AvisoImpuestoVerificado?> Recogido(string idExterno = "ext-1") =>
        await _buzon.EsperarAsync(Empresa, idExterno, TimeSpan.Zero, Ct);

    [Fact]
    public async Task AvisoValido_200_YQuedaEnElBuzonConSuAttemptYLaConsultaCruda()
    {
        var status = await Enviar(AvisosDePrueba.Cuerpo("ext-1", 2));

        status.Should().Be(200);
        var aviso = await Recogido();
        aviso.Should().NotBeNull();
        aviso!.Attempt.Should().Be(2);
        aviso.Estado.Should().Be(EstadoTrabajoExterno.Exitoso);
        aviso.CuerpoConsulta.Should().Be(AvisosDePrueba.Consulta("ext-1", 2));
        _registro.Registradas.Should().Equal((Empresa, "d-1", "ext-1", 2));
    }

    [Theory]
    [InlineData("corto")]
    [InlineData("")]
    public async Task TokenSinForma_401_SinLeerNadaMas(string token)
    {
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1), token: token)).Should().Be(401);
        _secretos.Pedidos.Should().BeEmpty();
        _registro.Registradas.Should().BeEmpty();
    }

    [Fact]
    public async Task TokenAlteradoODeOtraLlave_401()
    {
        var alterado = _token[..10] + (_token[10] == 'A' ? 'B' : 'A') + _token[11..];

        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1), token: alterado)).Should().Be(401);
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1), procesador: Procesador(OpcionesAvisoImpuesto.Crear("https://consultas.example",
            System.Text.Encoding.UTF8.GetBytes("otra-llave-de-prueba-0123456789-abcdefgh"))))).Should().Be(401);
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1), procesador: Procesador(OpcionesAvisoImpuesto.Crear("https://consultas.example", null))))
            .Should().Be(401, "sin llave del token no se reconoce ninguno");
        _secretos.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task EmpresaSinSecreto_401()
    {
        _secretos.PorEmpresa.Clear();

        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1))).Should().Be(401);
        _registro.Registradas.Should().BeEmpty();
    }

    [Fact]
    public async Task FirmaInvalida_401_SinRegistrar_YReleeElSecretoUnaVez()
    {
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1), secreto: "whsec_otro_secreto_000000000000000000")).Should().Be(401);

        _secretos.Pedidos.Should().Equal((Empresa, false), (Empresa, true));
        _registro.Registradas.Should().BeEmpty();
        (await Recogido()).Should().BeNull();
    }

    [Fact]
    public async Task FirmaConSecretoRotado_ReleeYAcepta()
    {
        const string Nuevo = "whsec_prueba_ROTADO_00000000000000000000";
        _secretos.Siguiente = new SecretoWebhookImpuesto(true, Nuevo, AvisosDePrueba.Secreto, _reloj.GetUtcNow().AddHours(24), 2);

        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1), secreto: Nuevo)).Should().Be(200);
        _secretos.Pedidos.Should().Equal((Empresa, false), (Empresa, true));
    }

    [Fact]
    public async Task SecretoAnteriorVigente_200_YVencido_401()
    {
        _secretos.PorEmpresa[Empresa] = new SecretoWebhookImpuesto(true, AvisosDePrueba.Secreto, AvisosDePrueba.SecretoAnterior, _reloj.GetUtcNow().AddHours(1), 3);
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1), secreto: AvisosDePrueba.SecretoAnterior)).Should().Be(200);

        _secretos.PorEmpresa[Empresa] = new SecretoWebhookImpuesto(true, AvisosDePrueba.Secreto, AvisosDePrueba.SecretoAnterior, _reloj.GetUtcNow().AddSeconds(-1), 3);
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 2), secreto: AvisosDePrueba.SecretoAnterior, delivery: "d-2")).Should().Be(401);
    }

    [Fact]
    public async Task TimestampFueraDeTolerancia_401()
    {
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1), desfase: -301)).Should().Be(401);
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1), desfase: 301)).Should().Be(401);
        _registro.Registradas.Should().BeEmpty();
    }

    [Fact]
    public async Task SinFirma_401()
    {
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1), secreto: null)).Should().Be(401);
    }

    [Fact]
    public async Task IdentidadCaida_503_ParaQueElProveedorReintente()
    {
        _secretos.Lanzar = new FalloSecretoWebhookImpuestoException("caida");

        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1))).Should().Be(503);
        _registro.Registradas.Should().BeEmpty();
    }

    [Fact]
    public async Task MismoDelivery_DosVeces_SegundoEs200SinRepetir()
    {
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1))).Should().Be(200);
        (await Recogido())!.Attempt.Should().Be(1);

        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1))).Should().Be(200);

        (await Recogido()).Should().BeNull("el duplicado no vuelve al buzón");
        _registro.Registradas.Should().HaveCount(1);
    }

    [Fact]
    public async Task SinDelivery_DeduplicaPorConsultaYAttempt()
    {
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 3), delivery: null)).Should().Be(200);
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 3), delivery: "  ")).Should().Be(200);

        _registro.Registradas.Should().Equal((Empresa, "sin-delivery:ext-1:3", "ext-1", 3));
    }

    [Fact]
    public async Task VariosAttempts_SeQuedaConElMayor_SinImportarElOrden()
    {
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 2), delivery: "d-2")).Should().Be(200);
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1), delivery: "d-1")).Should().Be(200);

        (await Recogido())!.Attempt.Should().Be(2);
    }

    [Fact]
    public async Task ConsultaDeOtraEmpresa_200_SeDescarta_YElLogNoLlevaLaConsulta()
    {
        _registro.Empresas[Trabajo] = Otra;

        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1))).Should().Be(200);

        (await Recogido()).Should().BeNull();
        _log.Eventos.Select(e => e.Id).Should().Contain(7453);
        string.Join("|", _log.Textos).Should().NotContain("ext-1");
    }

    [Fact]
    public async Task ConsultaYaCerrada_200_SeDescarta()
    {
        await _seguimiento.CerrarAsync(Trabajo, "succeeded", Ct);

        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 5))).Should().Be(200);

        (await Recogido()).Should().BeNull();
    }

    [Fact]
    public async Task OtroEvento_200_SinTocarNada()
    {
        (await Enviar(AvisosDePrueba.Cuerpo("ext-1", 1, evento: "consulta.otra"), evento: "consulta.otra")).Should().Be(200);

        _registro.Registradas.Should().BeEmpty();
        (await Recogido()).Should().BeNull();
    }

    [Theory]
    [InlineData("{\"consulta\":{\"status\":\"succeeded\",\"attempt\":1}}")]
    [InlineData("{\"consulta\":{\"id\":\"ext 1\",\"status\":\"succeeded\",\"attempt\":1}}")]
    [InlineData("{\"consulta\":{\"id\":\"ext-1\",\"status\":\"running\",\"attempt\":1}}")]
    [InlineData("no es json")]
    public async Task CuerpoFirmadoSinLaForma_200_SeDescarta(string json)
    {
        (await Enviar(System.Text.Encoding.UTF8.GetBytes(json))).Should().Be(200);

        (await Recogido()).Should().BeNull();
    }

    [Fact]
    public async Task NingunLogLlevaSecretoFirmaNiToken()
    {
        var cuerpo = AvisosDePrueba.Cuerpo("ext-1", 1);
        var ts = AvisosDePrueba.Ts(_reloj.GetUtcNow());
        var firma = AvisosDePrueba.Firmar(AvisosDePrueba.Secreto, ts, cuerpo);
        await Procesador().ProcesarAsync(_token, new CabecerasAvisoImpuesto(ts, firma, "d-1", "consulta.finalizada"), cuerpo, Ct);
        await Procesador().ProcesarAsync(_token, new CabecerasAvisoImpuesto(ts, "v1=00", "d-9", "consulta.finalizada"), cuerpo, Ct);
        _registro.Empresas[Trabajo] = Otra;
        await Procesador().ProcesarAsync(_token, new CabecerasAvisoImpuesto(ts, firma, "d-2", "consulta.finalizada"), cuerpo, Ct);

        var todo = string.Join("|", _log.Textos);
        todo.Should().NotContain(AvisosDePrueba.Secreto).And.NotContain(firma[3..]).And.NotContain(_token).And.NotContain("paz_y_salvo");
        _log.Eventos.Should().NotBeEmpty();
    }
}
