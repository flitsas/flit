using System.Text.Json;
using Flit.Consultas.Api.Impuesto;
using Flit.Consultas.Api.Impuesto.Avisos;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto.Avisos;

/// <summary>
/// HU #14711 — el aviso dentro de la consulta del impuesto:
/// <list type="bullet">
/// <item>AC-DC-07: el <c>callbackUrl</c> va en el cuerpo solo con base, llave y secreto de la empresa; si no, se omite.</item>
/// <item>Cierre único: el aviso cierra la consulta por el mismo camino del polling (sin GET); el polling que ya cerró descarta el
/// aviso tardío y el aviso que ya cerró deja al polling sin nada que publicar (AC-DC-10).</item>
/// </list>
/// Sin red ni base: transporte, seguimiento, secretos y registro falsos; el reloj avanza solo.
/// <para>Uso de ejemplo: <c>await Proveedor(t, callback, buzon).ConsultarAsync(pedido, llamador, trabajo, ct)</c>.</para>
/// </summary>
public sealed class AvisoEnProveedorImpuestoTests
{
    private static readonly Guid Empresa = Guid.Parse("0194aaaa-0000-7000-8000-000000000001");
    private static readonly PlatformServiceCaller Llamador = new("svc-diagnostico", Empresa);

    private readonly RelojFalso _reloj = new();
    private readonly FuenteCredencialFalsa _credenciales = new();
    private readonly SeguimientoEnMemoria _seguimiento = new();
    private readonly FuenteSecretoFalsa _secretos = new();
    private readonly ListaLogger<ProveedorImpuesto> _log = new();
    private readonly BuzonAvisosImpuesto _buzon;
    private readonly RegistroEnMemoria _registro;

    public AvisoEnProveedorImpuestoTests()
    {
        _buzon = new BuzonAvisosImpuesto(_reloj);
        _registro = new RegistroEnMemoria(_seguimiento);
        _secretos.PorEmpresa[Empresa] = new SecretoWebhookImpuesto(true, AvisosDePrueba.Secreto, null, null, 1);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OpcionesAvisoImpuesto ConAviso() => OpcionesAvisoImpuesto.Crear("https://consultas.example/", AvisosDePrueba.Llave);

    private ProveedorImpuesto Proveedor(ITransporteImpuesto transporte, OpcionesAvisoImpuesto? avisos = null) =>
        new(transporte, Opciones(), new CatalogoImpuestoFijo(["05"]), _credenciales, _seguimiento, PlanSondeoImpuesto.Real(280), _reloj, _log,
            new CallbackAvisoImpuesto(avisos ?? ConAviso(), _secretos), _buzon);

    private static string? CallbackDe(string cuerpo)
    {
        using var doc = JsonDocument.Parse(cuerpo);
        return doc.RootElement.TryGetProperty("callbackUrl", out var c) ? c.GetString() : null;
    }

    // ── AC-DC-07: envío del callbackUrl ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConBaseLlaveYSecreto_MandaElCallbackConElTokenDeLaEmpresa()
    {
        var transporte = new TransporteGuionado { Estados = { Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo) } };

        await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), Ct);

        var url = CallbackDe(transporte.CuerposEnviados.Single());
        url.Should().Be("https://consultas.example/api/v1/impuesto/avisos/" + TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave));
        string.Join("|", _log.Textos).Should().NotContain(url![^43..], "el token no se registra");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("http://consultas.example", true)]
    [InlineData("https://consultas.example", false)]
    public async Task SinBaseValidaOSinLlave_NoMandaCallback(string? baseUrl, bool conLlave)
    {
        var transporte = new TransporteGuionado { Estados = { Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo) } };

        await Proveedor(transporte, OpcionesAvisoImpuesto.Crear(baseUrl, conLlave ? AvisosDePrueba.Llave : null)).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), Ct);

        CallbackDe(transporte.CuerposEnviados.Single()).Should().BeNull();
    }

    [Fact]
    public async Task HttpEnLoopback_SeAceptaParaElStubLocal()
    {
        var transporte = new TransporteGuionado { Estados = { Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo) } };

        await Proveedor(transporte, OpcionesAvisoImpuesto.Crear("http://127.0.0.1:4026", AvisosDePrueba.Llave)).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), Ct);

        CallbackDe(transporte.CuerposEnviados.Single()).Should().StartWith("http://127.0.0.1:4026/api/v1/impuesto/avisos/");
    }

    [Fact]
    public async Task EmpresaSinSecreto_NoMandaCallback()
    {
        _secretos.PorEmpresa.Clear();
        var transporte = new TransporteGuionado { Estados = { Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo) } };

        await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), Ct);

        CallbackDe(transporte.CuerposEnviados.Single()).Should().BeNull();
    }

    [Fact]
    public async Task IdentidadCaidaAlPedirElSecreto_NoMandaCallback_YSigueLaConsulta()
    {
        _secretos.Lanzar = new FalloSecretoWebhookImpuestoException("caida");
        var transporte = new TransporteGuionado { Estados = { Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo) } };

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), Ct);

        CallbackDe(transporte.CuerposEnviados.Single()).Should().BeNull();
        respuesta.Resultado.Chequeos.Single().Clave.Should().Be("impuesto");
    }

    [Fact]
    public async Task Rechazo422WebhookNotConfigured_ReenviaSinCallback()
    {
        var transporte = new TransporteGuionado
        {
            Envios = { Guion.Rechazo(ClaseFalloProveedor.Rechazo, 422, "webhook_not_configured") },
            Estados = { Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo) },
        };

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), Ct);

        transporte.CuerposEnviados.Should().HaveCount(2);
        CallbackDe(transporte.CuerposEnviados[0]).Should().NotBeNull();
        CallbackDe(transporte.CuerposEnviados[1]).Should().BeNull();
        respuesta.Resultado.Chequeos.Single().Clave.Should().Be("impuesto");
    }

    // ── Cierre único aviso + polling ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElAvisoCierraLaConsulta_SinGet_PorElMismoCaminoDelPolling()
    {
        var trabajo = Guid.NewGuid();
        var transporte = new TransporteGuionado();
        _buzon.Entregar(Empresa, "ext-1", new AvisoImpuestoVerificado(1, EstadoTrabajoExterno.Exitoso, Guion.ConDeuda, _reloj.GetUtcNow()));

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, Ct);

        transporte.Gets.Should().Be(0, "el aviso reemplaza el GET");
        respuesta.TieneDeuda.Should().BeTrue();
        respuesta.MontoTotal.Should().Be(335000);
        _seguimiento.Operaciones.Count(o => o.StartsWith("cerrar:", StringComparison.Ordinal)).Should().Be(1);
        _seguimiento.Fila(trabajo)!.Estado.Should().Be("succeeded");
    }

    [Fact]
    public async Task ElAvisoQueLlegaMientrasSondea_DespiertaYCierra()
    {
        var trabajo = Guid.NewGuid();
        var transporte = new TransporteEntregaAviso(_buzon, Empresa, _reloj);

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, Ct);

        transporte.Gets.Should().Be(1, "el primer GET dice en cola; el aviso cierra antes del segundo");
        respuesta.Resultado.Chequeos.Single().Clave.Should().Be("impuesto");
        _seguimiento.Operaciones.Count(o => o.StartsWith("cerrar:", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task AC_DC_10_AvisoCerro_ElAvisoTardioConMayorAttemptSeDescarta_YNoHaySegundoCierre()
    {
        var trabajo = Guid.NewGuid();
        _registro.Empresas[trabajo] = Empresa;
        var procesador = new ProcesadorAvisoImpuesto(ConAviso(), _secretos, _registro, _buzon, _reloj, new ListaLogger<ProcesadorAvisoImpuesto>());
        var transporte = new TransporteGuionado();
        var token = TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave);

        // Se envía y queda en cola; el aviso del attempt 1 llega antes del primer sondeo.
        _buzon.Entregar(Empresa, "ext-1", new AvisoImpuestoVerificado(1, EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo, _reloj.GetUtcNow()));
        await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, Ct);

        // Después llega un aviso de un attempt mayor: la consulta ya está cerrada, se confirma con 200 y no vuelve al buzón.
        var cuerpo = AvisosDePrueba.Cuerpo("ext-1", 2, delivery: "d-2");
        var ts = AvisosDePrueba.Ts(_reloj.GetUtcNow());
        var status = await procesador.ProcesarAsync(token, new CabecerasAvisoImpuesto(ts, AvisosDePrueba.Firmar(AvisosDePrueba.Secreto, ts, cuerpo), "d-2", "consulta.finalizada"), cuerpo, Ct);

        status.Should().Be(200);
        (await _buzon.EsperarAsync(Empresa, "ext-1", TimeSpan.Zero, Ct)).Should().BeNull();
        _seguimiento.Operaciones.Count(o => o.StartsWith("cerrar:", StringComparison.Ordinal)).Should().Be(1);
        transporte.Gets.Should().Be(0);
    }

    [Fact]
    public async Task PollingCerro_ElAvisoQueLlegaDespuesSeDescarta()
    {
        var trabajo = Guid.NewGuid();
        _registro.Empresas[trabajo] = Empresa;
        var procesador = new ProcesadorAvisoImpuesto(ConAviso(), _secretos, _registro, _buzon, _reloj, new ListaLogger<ProcesadorAvisoImpuesto>());
        var transporte = new TransporteGuionado { Estados = { Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo) } };

        await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, Ct);
        var cuerpo = AvisosDePrueba.Cuerpo("ext-1", 1);
        var ts = AvisosDePrueba.Ts(_reloj.GetUtcNow());
        var status = await procesador.ProcesarAsync(TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave),
            new CabecerasAvisoImpuesto(ts, AvisosDePrueba.Firmar(AvisosDePrueba.Secreto, ts, cuerpo), "d-1", "consulta.finalizada"), cuerpo, Ct);

        status.Should().Be(200);
        (await _buzon.EsperarAsync(Empresa, "ext-1", TimeSpan.Zero, Ct)).Should().BeNull();
        _seguimiento.Operaciones.Count(o => o.StartsWith("cerrar:", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task SinAviso_ElPollingCierraComoHoy()
    {
        var transporte = new TransporteGuionado { Estados = { Guion.EnCola(), Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo) } };

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), Ct);

        transporte.Gets.Should().Be(2);
        respuesta.Resultado.Chequeos.Single().Clave.Should().Be("impuesto");
    }

    [Fact]
    public async Task AvisoDeOtraConsulta_NoCierraEsta()
    {
        var transporte = new TransporteGuionado { Estados = { Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo) } };
        _buzon.Entregar(Empresa, "ext-otra", new AvisoImpuestoVerificado(1, EstadoTrabajoExterno.Exitoso, Guion.ConDeuda, _reloj.GetUtcNow()));

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), Ct);

        transporte.Gets.Should().Be(1);
        respuesta.TieneDeuda.Should().BeFalse();
    }

    private static ConsultarImpuestoVehicularRequest Pedido() => ProveedorImpuestoTests.Pedido();

    private static OpcionesImpuesto Opciones() =>
        OpcionesImpuesto.Desde(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IMPUESTO_API_BASE_URL"] = "http://fuente.invalid/api/v1",
            ["IMPUESTO_API_PATH"] = "consultas",
            ["IMPUESTO_CONTACTO_CORREO"] = "contacto@example.invalid",
            [OpcionesImpuesto.ClaveIdentidadAddress] = "http://identidad.invalid",
        }).Build());

    /// <summary>Primer GET: en cola, y en ese momento llega el aviso; un segundo GET sería un error del sondeo.</summary>
    private sealed class TransporteEntregaAviso(BuzonAvisosImpuesto buzon, Guid empresa, TimeProvider reloj) : ITransporteImpuesto
    {
        public int Gets { get; private set; }

        public bool Listo => true;

        public string NombreProveedor => "impuesto";

        public Task<EnvioImpuesto> EnviarAsync(string cuerpo, string apiKey, CancellationToken ct) => Task.FromResult(new EnvioImpuesto("ext-1", null, null));

        public Task<EstadoImpuesto> ConsultarAsync(string idExterno, string apiKey, CancellationToken ct)
        {
            Gets++;
            if (Gets > 1)
                throw new InvalidOperationException("el aviso debió cerrar antes del segundo GET");
            buzon.Entregar(empresa, idExterno, new AvisoImpuestoVerificado(1, EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo, reloj.GetUtcNow()));
            return Task.FromResult(Guion.EnCola());
        }

        public Task<ArchivoImpuesto> DescargarAsync(string archivoId, string apiKey, CancellationToken ct) => throw new NotSupportedException();
    }
}
