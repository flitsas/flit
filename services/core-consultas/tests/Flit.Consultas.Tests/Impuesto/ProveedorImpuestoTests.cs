using System.Text.Json;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Impuesto;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

/// <summary>
/// Diseño §2.3 y §2.4 — POST, sondeo 5, 5, 10, 15… s con tope desde <c>submitted_at</c>, seguimiento por trabajo, credencial por
/// empresa (D7) y mapeo de fallos. Sin red ni base: transporte, credencial y seguimiento falsos; el reloj avanza solo.
/// <para>Uso de ejemplo: <c>await Proveedor(t).ConsultarAsync(Pedido(), Llamador, trabajo, ct)</c>.</para>
/// </summary>
public sealed class ProveedorImpuestoTests
{
    private static readonly Guid Empresa = Guid.Parse("0194aaaa-0000-7000-8000-000000000001");
    private static readonly PlatformServiceCaller Llamador = new("svc-diagnostico", Empresa);

    private readonly RelojFalso _reloj = new();
    private readonly FuenteCredencialFalsa _credenciales = new();
    private readonly SeguimientoEnMemoria _seguimiento = new();
    private readonly ListaLogger<ProveedorImpuesto> _log = new();

    [Fact]
    public async Task Departamento_fuera_de_la_lista_no_llama()
    {
        var transporte = new TransporteGuionado();
        var accion = async () => await Proveedor(transporte, "13").ConsultarAsync(Pedido("05"), Llamador, Guid.NewGuid(), CancellationToken.None);
        await accion.Should().ThrowAsync<RpcException>().Where(e => e.StatusCode == StatusCode.FailedPrecondition);
        transporte.Posts.Should().Be(0);
        _credenciales.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_documento_sin_homologar_no_llama()
    {
        var transporte = new TransporteGuionado();
        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(tipo: "RC"), Llamador, Guid.NewGuid(), CancellationToken.None);
        transporte.Posts.Should().Be(0);
        Clave(respuesta).Should().Be("impuesto_documento_no_homologado");
        respuesta.Resultado.Chequeos[0].Estado.Should().Be(EstadoChequeo.Error);
    }

    [Fact]
    public async Task D7_sin_llave_no_llama_y_queda_en_credencial_no_configurada_con_motivo_credenciales()
    {
        using var _ = RegistroFallosProveedor.Abrir();
        _credenciales.Ultima = FuenteCredencialFalsa.SinLlave();
        var transporte = new TransporteGuionado();
        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);
        transporte.Posts.Should().Be(0);
        Clave(respuesta).Should().Be("impuesto_credencial_no_configurada");
        respuesta.Resultado.Chequeos[0].Estado.Should().Be(EstadoChequeo.Error);
        RegistroFallosProveedor.Motivo().Should().Be("CREDENCIALES");
        _credenciales.Pedidos.Should().Equal((Empresa, false));
    }

    [Fact]
    public async Task Identidad_caida_es_fallo_de_red_transitorio()
    {
        using var _ = RegistroFallosProveedor.Abrir();
        _credenciales.Lanzar = new FalloCredencialImpuestoException(new FalloProveedor(ClaseFalloProveedor.Red), "impuesto_red");
        var transporte = new TransporteGuionado();
        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);
        transporte.Posts.Should().Be(0);
        Clave(respuesta).Should().Be("impuesto_red");
        RegistroFallosProveedor.Motivo().Should().Be("RED");
    }

    [Fact]
    public async Task Envia_con_la_llave_de_la_empresa_sondea_5_5_y_devuelve_el_resultado()
    {
        var trabajo = Guid.NewGuid();
        var transporte = new TransporteGuionado();
        transporte.Estados.Enqueue(Guion.EnCola());
        transporte.Estados.Enqueue(Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.ConDeuda));

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, CancellationToken.None);

        transporte.Posts.Should().Be(1);
        transporte.LlavesUsadas.Should().AllBe(FuenteCredencialFalsa.Llave);
        transporte.IdsConsultados.Should().Equal("ext-1", "ext-1");
        _reloj.Esperas.Should().Equal(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        respuesta.TieneDeuda.Should().BeTrue();
        respuesta.MontoTotal.Should().Be(335_000);
        respuesta.Resultado.Chequeos[0].Estado.Should().Be(EstadoChequeo.Fail);
        _seguimiento.Fila(trabajo)!.Estado.Should().Be("succeeded");
        _seguimiento.Operaciones.Should().ContainInOrder("reservar", "guardar", "cerrar:succeeded");
        using var cuerpo = JsonDocument.Parse(transporte.CuerposEnviados[0]);
        cuerpo.RootElement.TryGetProperty("processStep", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AProto_copia_advertencias_vigencias_con_referencia_y_vencimiento_y_la_consolidada()
    {
        var transporte = new TransporteGuionado();
        transporte.Estados.Enqueue(Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.ConDeuda));
        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);

        respuesta.Vigencias.Should().ContainSingle();
        var vigencia = respuesta.Vigencias[0];
        (vigencia.Anio, vigencia.Valor, vigencia.LiquidacionId, vigencia.ReferenciaPago, vigencia.FechaVencimiento, vigencia.Avaluo)
            .Should().Be((2025, 335_000L, "archivo-2025", "REF-2025-1", "2025-07-31", 25_000_000L));
        respuesta.LiquidacionConsolidadaId.Should().Be("archivo-consolidado");
        respuesta.Advertencias.Select(a => (a.Tipo, a.Campo, a.Codigo)).Should().Equal(
            (TipoAdvertenciaImpuesto.Validacion, "vehicleServiceType", ""),
            (TipoAdvertenciaImpuesto.Aviso, "", "periodo_en_cobro"));
        respuesta.Advertencias[1].Anio.Should().Be(2025);
        respuesta.ToString().Should().NotContain("texto del proveedor");
    }

    [Fact]
    public async Task Parcial_con_deuda_es_FAIL_con_dato_parcial_y_parcial_sin_deuda_es_ERROR()
    {
        var conDeuda = new TransporteGuionado();
        conDeuda.Estados.Enqueue(Guion.Terminal(EstadoTrabajoExterno.Parcial, Guion.ConDeuda.Replace("\"status\":\"succeeded\"", "\"status\":\"partial\"", StringComparison.Ordinal)));
        var fail = await Proveedor(conDeuda).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);
        (fail.Resultado.Chequeos[0].Estado, fail.DatoParcial, fail.TieneDeuda).Should().Be((EstadoChequeo.Fail, true, true));

        var sinDeuda = new TransporteGuionado();
        sinDeuda.Estados.Enqueue(Guion.Terminal(EstadoTrabajoExterno.Parcial, Guion.PazYSalvo.Replace("\"status\":\"succeeded\"", "\"status\":\"partial\"", StringComparison.Ordinal)));
        var error = await Proveedor(sinDeuda).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);
        (Clave(error), error.Resultado.Chequeos[0].Estado, error.DatoParcial).Should().Be(("impuesto_no_confirmado", EstadoChequeo.Error, true));
    }

    [Fact]
    public async Task Un_reintento_del_mismo_trabajo_retoma_el_sondeo_sin_otro_POST_y_el_tope_cuenta_desde_el_envio()
    {
        var trabajo = Guid.NewGuid();
        _seguimiento.Sembrar(new SeguimientoImpuesto(trabajo, "ext-previo", _reloj.GetUtcNow() - TimeSpan.FromSeconds(100), "running"));
        var transporte = new TransporteGuionado();
        transporte.Estados.Enqueue(Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo));

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, CancellationToken.None);

        transporte.Posts.Should().Be(0);
        transporte.IdsConsultados.Should().Equal("ext-previo");
        respuesta.Resultado.Chequeos[0].Estado.Should().Be(EstadoChequeo.Ok);
    }

    [Fact]
    public async Task Agotado_el_presupuesto_queda_no_confirmado_tiempo_sin_motivo_y_cerrado_en_timeout()
    {
        using var _ = RegistroFallosProveedor.Abrir();
        var trabajo = Guid.NewGuid();
        var transporte = new TransporteGuionado(); // siempre «running»
        var respuesta = await Proveedor(transporte, presupuesto: 60).ConsultarAsync(Pedido(), Llamador, trabajo, CancellationToken.None);

        Clave(respuesta).Should().Be("impuesto_no_confirmado_tiempo");
        respuesta.Resultado.Chequeos[0].Estado.Should().Be(EstadoChequeo.Error);
        RegistroFallosProveedor.Motivo().Should().BeNull();
        _seguimiento.Fila(trabajo)!.Estado.Should().Be("timeout");
        _reloj.Esperas.Select(e => e.TotalSeconds).Should().Equal(5, 5, 10, 15, 15, 10);
        _reloj.Esperas.Aggregate(TimeSpan.Zero, (a, b) => a + b).Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Un_reintento_con_el_presupuesto_ya_vencido_no_vuelve_a_consultar()
    {
        var trabajo = Guid.NewGuid();
        _seguimiento.Sembrar(new SeguimientoImpuesto(trabajo, "ext-viejo", _reloj.GetUtcNow() - TimeSpan.FromSeconds(400), "running"));
        var transporte = new TransporteGuionado();
        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, CancellationToken.None);
        Clave(respuesta).Should().Be("impuesto_no_confirmado_tiempo");
        (transporte.Posts, transporte.Gets).Should().Be((0, 0));
    }

    [Fact]
    public async Task Un_401_en_el_POST_refresca_la_llave_una_vez_y_reintenta_si_cambio_la_version()
    {
        _credenciales.Respuestas.Enqueue(new CredencialImpuesto(true, "llave-vieja-sintetica", 1, MotivoSinCredencial.Ninguno));
        _credenciales.Respuestas.Enqueue(new CredencialImpuesto(true, "llave-nueva-sintetica", 2, MotivoSinCredencial.Ninguno));
        var transporte = new TransporteGuionado();
        transporte.Envios.Enqueue(Guion.Rechazo(ClaseFalloProveedor.Credenciales, 401, "unauthorized"));
        transporte.Estados.Enqueue(Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo));

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);

        respuesta.Resultado.Chequeos[0].Estado.Should().Be(EstadoChequeo.Ok);
        _credenciales.Pedidos.Should().Equal((Empresa, false), (Empresa, true));
        transporte.LlavesUsadas.Should().Equal("llave-vieja-sintetica", "llave-nueva-sintetica", "llave-nueva-sintetica");
    }

    [Fact]
    public async Task Un_401_con_la_misma_version_tras_refrescar_es_autenticacion_con_motivo_credenciales()
    {
        using var _ = RegistroFallosProveedor.Abrir();
        var trabajo = Guid.NewGuid();
        var transporte = new TransporteGuionado();
        transporte.Envios.Enqueue(Guion.Rechazo(ClaseFalloProveedor.Credenciales, 401, "unauthorized"));
        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, CancellationToken.None);
        Clave(respuesta).Should().Be("impuesto_autenticacion");
        RegistroFallosProveedor.Motivo().Should().Be("CREDENCIALES");
        transporte.Posts.Should().Be(1);
        _seguimiento.Fila(trabajo)!.Estado.Should().Be("error");
    }

    [Fact]
    public async Task Un_401_en_el_sondeo_que_persiste_tras_refrescar_es_autenticacion()
    {
        var transporte = new TransporteGuionado();
        transporte.Estados.Enqueue(Guion.Fallo(ClaseFalloProveedor.Credenciales, 401));
        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);
        Clave(respuesta).Should().Be("impuesto_autenticacion");
        _credenciales.Pedidos.Should().Equal((Empresa, false), (Empresa, true));
    }

    [Theory]
    [InlineData(ClaseFalloProveedor.Rechazo, 400, "bad_request", "impuesto_rechazo", "RECHAZO")]
    [InlineData(ClaseFalloProveedor.Rechazo, 422, "department_unsupported", "impuesto_rechazo", "RECHAZO")]
    [InlineData(ClaseFalloProveedor.Limite, 429, "", "impuesto_limite", "LIMITE")]
    [InlineData(ClaseFalloProveedor.Caida, 503, "", "impuesto_caida", "CAIDA")]
    public async Task Un_POST_rechazado_cierra_en_error_con_su_clave_y_su_motivo(ClaseFalloProveedor clase, int http, string codigo, string clave, string motivo)
    {
        using var _ = RegistroFallosProveedor.Abrir();
        var trabajo = Guid.NewGuid();
        var transporte = new TransporteGuionado();
        transporte.Envios.Enqueue(Guion.Rechazo(clase, http, codigo));
        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, CancellationToken.None);
        Clave(respuesta).Should().Be(clave);
        RegistroFallosProveedor.Motivo().Should().Be(motivo);
        transporte.Gets.Should().Be(0);
        _seguimiento.Fila(trabajo)!.Estado.Should().Be("error");
    }

    [Fact]
    public async Task Tres_fallos_transitorios_seguidos_en_el_sondeo_cortan_con_su_clave()
    {
        using var _ = RegistroFallosProveedor.Abrir();
        var transporte = new TransporteGuionado();
        for (var i = 0; i < 3; i++)
            transporte.Estados.Enqueue(Guion.Fallo(ClaseFalloProveedor.Caida, 502));
        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);
        Clave(respuesta).Should().Be("impuesto_caida");
        RegistroFallosProveedor.Motivo().Should().Be("CAIDA");
        transporte.Gets.Should().Be(3);
    }

    [Fact]
    public async Task Dos_fallos_de_red_y_luego_respuesta_no_cortan_el_sondeo()
    {
        var transporte = new TransporteGuionado();
        transporte.Estados.Enqueue(Guion.Fallo(ClaseFalloProveedor.Red));
        transporte.Estados.Enqueue(Guion.Fallo(ClaseFalloProveedor.Timeout));
        transporte.Estados.Enqueue(Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo));
        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);
        respuesta.Resultado.Chequeos[0].Estado.Should().Be(EstadoChequeo.Ok);
    }

    [Fact]
    public async Task Un_404_al_retomar_con_tiempo_vuelve_a_enviar_una_sola_vez()
    {
        var trabajo = Guid.NewGuid();
        _seguimiento.Sembrar(new SeguimientoImpuesto(trabajo, "ext-perdido", _reloj.GetUtcNow() - TimeSpan.FromSeconds(30), "running"));
        var transporte = new TransporteGuionado();
        transporte.Estados.Enqueue(Guion.NoEncontrado());
        transporte.Estados.Enqueue(Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo));

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, CancellationToken.None);

        respuesta.Resultado.Chequeos[0].Estado.Should().Be(EstadoChequeo.Ok);
        transporte.Posts.Should().Be(1);
        transporte.IdsConsultados.Should().Equal("ext-perdido", "ext-1");
        _seguimiento.Operaciones.Should().ContainInOrder("olvidar", "guardar");
    }

    [Fact]
    public async Task Un_404_de_un_envio_propio_es_caida()
    {
        using var _ = RegistroFallosProveedor.Abrir();
        var transporte = new TransporteGuionado();
        transporte.Estados.Enqueue(Guion.NoEncontrado());
        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);
        Clave(respuesta).Should().Be("impuesto_caida");
        RegistroFallosProveedor.Motivo().Should().Be("CAIDA");
        transporte.Posts.Should().Be(1);
    }

    [Fact]
    public async Task La_cancelacion_del_consumidor_se_propaga_sin_cerrar_el_seguimiento()
    {
        var trabajo = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var transporte = new TransporteCancelador(cts);
        var accion = async () => await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, cts.Token);
        await accion.Should().ThrowAsync<OperationCanceledException>();
        _seguimiento.Fila(trabajo)!.IdExterno.Should().Be("ext-cancelado");
        _seguimiento.Operaciones.Should().NotContain(o => o.StartsWith("cerrar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sin_ruta_la_lista_de_departamentos_queda_vacia()
    {
        var proveedor = Proveedor(new TransporteGuionado { Listo = false }, "13", "41");
        (await proveedor.DepartamentosAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task La_lista_efectiva_sale_del_catalogo()
    {
        var proveedor = Proveedor(new TransporteGuionado(), "50", "52");
        (await proveedor.DepartamentosAsync(CancellationToken.None)).Should().Equal("50", "52");
    }

    [Fact]
    public void La_credencial_no_muestra_la_llave_al_imprimirse()
    {
        var credencial = new CredencialImpuesto(true, FuenteCredencialFalsa.Llave, 7, MotivoSinCredencial.Ninguno);
        credencial.ToString().Should().NotContain(FuenteCredencialFalsa.Llave).And.Contain("Version = 7");
    }

    [Fact]
    public async Task Una_solicitud_rechazada_por_la_fuente_deja_un_Warning_con_trabajo_id_externo_error_code_y_clave()
    {
        var trabajo = Guid.NewGuid();
        var transporte = new TransporteGuionado();
        transporte.Estados.Enqueue(Guion.Terminal(EstadoTrabajoExterno.Fallido,
            """{"id":"ext-1","status":"failed","error_code":"invalid_params","result":{"status":"error","warnings":[{"code":"periodo_en_cobro","message":"texto del proveedor"}]}}"""));

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, trabajo, CancellationToken.None);

        Clave(respuesta).Should().Be("impuesto_solicitud_invalida");
        var texto = UnicoAvisoSinVeredicto();
        texto.Should().Contain(trabajo.ToString()).And.Contain("ext-1").And.Contain("departamento 05")
            .And.Contain("estado failed").And.Contain("estado del resultado error").And.Contain("error_code invalid_params")
            .And.Contain("clave impuesto_solicitud_invalida").And.Contain("aviso:periodo_en_cobro");
        TodoElLog().Should().NotContain("texto del proveedor").And.NotContain("900000001").And.NotContain("AAA111")
            .And.NotContain("PROPIETARIO SINTETICO UNO");
    }

    [Theory]
    [InlineData("parametros con espacios")]
    [InlineData("persona@example.invalid")]
    public async Task Un_error_code_con_forma_invalida_se_registra_como_no_reconocido_sin_el_valor_crudo(string codigo)
    {
        var transporte = new TransporteGuionado();
        transporte.Estados.Enqueue(Guion.Terminal(EstadoTrabajoExterno.Fallido, "{\"status\":\"failed\",\"error_code\":\"" + codigo + "\"}"));

        var respuesta = await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);

        Clave(respuesta).Should().Be("impuesto_no_confirmado");
        UnicoAvisoSinVeredicto().Should().Contain("error_code no_reconocido").And.Contain("clave impuesto_no_confirmado");
        TodoElLog().Should().NotContain(codigo);
    }

    [Fact]
    public async Task Agotado_el_presupuesto_deja_el_Warning_con_la_clave_de_tiempo()
    {
        var trabajo = Guid.NewGuid();
        var transporte = new TransporteGuionado(); // siempre «running»
        await Proveedor(transporte, presupuesto: 60).ConsultarAsync(Pedido(), Llamador, trabajo, CancellationToken.None);

        UnicoAvisoSinVeredicto().Should().Contain(trabajo.ToString()).And.Contain("ext-1").And.Contain("estado running")
            .And.Contain("clave impuesto_no_confirmado_tiempo");
    }

    [Fact]
    public async Task Un_veredicto_confirmado_no_deja_el_Warning()
    {
        var transporte = new TransporteGuionado();
        transporte.Estados.Enqueue(Guion.Terminal(EstadoTrabajoExterno.Exitoso, Guion.PazYSalvo));
        await Proveedor(transporte).ConsultarAsync(Pedido(), Llamador, Guid.NewGuid(), CancellationToken.None);
        _log.Eventos.Should().NotContain(e => e.Id == 7436);
    }

    internal static ConsultarImpuestoVehicularRequest Pedido(string departamento = "05", string tipo = "CC") => new()
    {
        Placa = new Placa { Valor = "AAA111" },
        DepartamentoCodigo = departamento,
        Documento = new DocumentoIdentidad { Tipo = tipo, Numero = "900000001" },
        PropietarioNombre = "PROPIETARIO SINTETICO UNO",
        Municipio = "SABANETA",
        Modelo = 2020,
    };

    private static string Clave(ConsultarImpuestoVehicularResponse r) => r.Resultado.Chequeos.Single().Clave;

    private string UnicoAvisoSinVeredicto()
    {
        var indices = _log.Eventos.Select((e, i) => (e, i)).Where(x => x.e.Id == 7436).Select(x => x.i).ToList();
        indices.Should().ContainSingle("un solo Warning por trabajo sin veredicto");
        _log.Niveles[indices[0]].Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        return string.Join(" | ", _log.Textos);
    }

    private string TodoElLog() => string.Join(" | ", _log.Textos);

    private ProveedorImpuesto Proveedor(ITransporteImpuesto transporte, params string[] departamentos) =>
        Proveedor(transporte, 280, departamentos);

    private ProveedorImpuesto Proveedor(ITransporteImpuesto transporte, int presupuesto, params string[] departamentos)
    {
        var opciones = OpcionesImpuesto.Desde(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IMPUESTO_API_BASE_URL"] = "http://fuente.invalid/api/v1",
            ["IMPUESTO_API_PATH"] = "consultas",
            ["IMPUESTO_CONTACTO_CORREO"] = "contacto@example.invalid",
        }).Build());
        return new ProveedorImpuesto(transporte, opciones, new CatalogoImpuestoFijo(departamentos.Length == 0 ? ["05"] : departamentos), _credenciales,
            _seguimiento, PlanSondeoImpuesto.Real(presupuesto), _reloj, _log);
    }

    private sealed class TransporteCancelador(CancellationTokenSource cts) : ITransporteImpuesto
    {
        public bool Listo => true;
        public string NombreProveedor => "impuesto";
        public Task<EnvioImpuesto> EnviarAsync(string cuerpo, string apiKey, CancellationToken ct) => Task.FromResult(new EnvioImpuesto("ext-cancelado", null, null));
        public Task<EstadoImpuesto> ConsultarAsync(string idExterno, string apiKey, CancellationToken ct)
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Guion.EnCola());
        }

        public Task<ArchivoImpuesto> DescargarAsync(string archivoId, string apiKey, CancellationToken ct) => throw new NotSupportedException();
    }
}
