using System.Security.Cryptography;
using Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen.Credencial;

/// <summary>
/// ADR-0091 §2.4/§2.7 y §3.2, SP-12, SP-14(a), SP-18: la fuente remota pide la configuración de IA a un core-api falso en memoria,
/// valida eco y reloj, descifra y guarda la credencial solo en memoria con TTL ≤ 300 s, y separa el fallo técnico (reintento de la
/// cola) del «no configurado» (plantilla). Nunca convierte un fallo técnico en no configurado.
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --sin-bd --filter "FullyQualifiedName~FuenteCredencialPlataformaTests"
/// </code>
/// </example>
public sealed class FuenteCredencialPlataformaTests : IAsyncLifetime
{
    private const string Centinela = "sk-ant-test-CENTINELA";
    private static readonly VectorIa V = VectorIa.Cargar();
    private static readonly Lazy<RSA> ParAnterior = new(() => RSA.Create(3072));

    private readonly RelojMovil _reloj = new(V.Emitida);
    private readonly LogsIa _logs = new();
    private readonly RSA _privada = V.Privada();
    private readonly RSA _publica = V.Publica();
    private CoreApiIaFalso _core = null!;

    public async ValueTask InitializeAsync()
    {
        _core = await CoreApiIaFalso.IniciarAsync();
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow()));
    }

    public async ValueTask DisposeAsync()
    {
        await _core.DisposeAsync();
        _privada.Dispose();
        _publica.Dispose();
    }

    private FuenteCredencialPlataforma Fuente(bool conAnterior = false, string[]? modelos = null, Func<ConfiguracionIaService.ConfiguracionIaServiceClient>? cliente = null)
    {
        var claves = conAnterior
            ? new ClavesPrivadasIa(V.Kid, _privada, "diag-prueba-202610", ParAnterior.Value)
            : new ClavesPrivadasIa(V.Kid, _privada);
        var opciones = new OpcionesIa { Origen = OpcionesIa.OrigenPlataforma, Kid = V.Kid, ModelosPermitidos = modelos };
        return new FuenteCredencialPlataforma(cliente ?? _core.Cliente, claves, Options.Create(opciones), _reloj, _logs.Logger<FuenteCredencialPlataforma>());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Valida_DevuelveProveedorModeloYLlave_YPideLaHerramientaConKidYNonceDe16()
    {
        using var fuente = Fuente();

        var r = await fuente.ObtenerAsync(Ct);

        r.Falla.Should().Be(FallaCredencial.Ninguna);
        r.Credencial!.Proveedor.Should().Be("anthropic");
        r.Credencial.Modelo.Should().Be("claude-haiku-4-5");
        r.Credencial.ApiKey.Should().Be(Centinela);
        r.Credencial.RowVersion.Should().Be(7);
        var pedido = _core.Pedidos.Single();
        pedido.ToolCode.Should().Be("diagnostico_dictamen");
        pedido.Kid.Should().Be(V.Kid);
        pedido.Nonce.Length.Should().Be(16);
    }

    [Fact]
    public async Task DentroDelTtl_UsaLaCacheEnMemoria_YAlVencer_PideOtraVezConOtroNonce()
    {
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow(), ttl: 30));
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Ninguna);
        _reloj.Avanzar(TimeSpan.FromSeconds(29));
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Ninguna);
        _core.Pedidos.Should().HaveCount(1);

        _reloj.Avanzar(TimeSpan.FromSeconds(2));
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Ninguna);

        _core.Pedidos.Should().HaveCount(2);
        _core.Pedidos.Select(p => p.Nonce).Distinct().Should().HaveCount(2, "nonce nuevo por llamada");
    }

    [Theory]
    [InlineData(86400)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task SP12_LaVigenciaEsElMinimoEntreTtlY300_IgnorandoTtlNoPositivo(int ttl)
    {
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow(), ttl: ttl));
        using var fuente = Fuente();

        await fuente.ObtenerAsync(Ct);
        _reloj.Avanzar(TimeSpan.FromSeconds(299));
        await fuente.ObtenerAsync(Ct);
        _core.Pedidos.Should().HaveCount(1);

        _reloj.Avanzar(TimeSpan.FromSeconds(2));
        await fuente.ObtenerAsync(Ct);
        _core.Pedidos.Should().HaveCount(2, "la vigencia nunca pasa de 300 s");
    }

    [Fact]
    public async Task SingleFlight_VariasLlamadasALaVez_HacenUnSoloPedido()
    {
        _core.Responder = async p =>
        {
            await Task.Delay(200);
            return CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow());
        };
        using var fuente = Fuente();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fuente.ObtenerAsync(Ct)));

        resultados.Should().OnlyContain(r => r.Falla == FallaCredencial.Ninguna);
        _core.Pedidos.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(MotivoNoDisponible.SinProveedorConLlave)]
    [InlineData(MotivoNoDisponible.HerramientaSinConfigurar)]
    public async Task NoDisponible_EsNoConfigurado_YSeCacheaSesentaSegundos(MotivoNoDisponible motivo)
    {
        _core.Responder = p => Task.FromResult(new ObtenerConfiguracionIaResponse
        {
            ToolCode = p.ToolCode,
            Disponible = false,
            Motivo = motivo,
            TtlSegundos = 60,
            EmitidaEn = Timestamp.FromDateTimeOffset(_reloj.GetUtcNow()),
            Nonce = p.Nonce,
            Kid = p.Kid,
        });
        using var fuente = Fuente();

        var r = await fuente.ObtenerAsync(Ct);
        r.Falla.Should().Be(FallaCredencial.NoConfigurado);
        r.Credencial.Should().BeNull();
        _reloj.Avanzar(TimeSpan.FromSeconds(59));
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.NoConfigurado);
        _core.Pedidos.Should().HaveCount(1);
        _reloj.Avanzar(TimeSpan.FromSeconds(2));
        await fuente.ObtenerAsync(Ct);
        _core.Pedidos.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.Unknown)]
    [InlineData(StatusCode.ResourceExhausted)]
    public async Task SP12_CoreApiCaido_EsFalloTecnico_NuncaNoConfigurado_ConVigenciaNegativaDe10s(StatusCode estado)
    {
        _core.Responder = _ => throw new RpcException(new Status(estado, "detalle interno " + Centinela));
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);

        _core.Pedidos.Should().HaveCount(1, "CR-1 (review final): el fallo técnico se guarda 10 s");
        _reloj.Avanzar(TimeSpan.FromSeconds(10));
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica, "nunca se convierte en no configurado");
        _core.Pedidos.Should().HaveCount(2, "pasados 10 s se vuelve a pedir");
        _logs.Todo.Should().NotContain(Centinela).And.NotContain("detalle interno");
    }

    [Fact]
    public async Task SP12_ErrorFueraDeGrpc_ComoElTokenDeIdentidad_EsFalloTecnico()
    {
        using var fuente = Fuente(cliente: () => throw new InvalidOperationException("Identidad respondió 500 " + Centinela));

        var r = await fuente.ObtenerAsync(Ct);

        r.Falla.Should().Be(FallaCredencial.Tecnica);
        _logs.Todo.Should().NotContain(Centinela).And.NotContain("Identidad respondió");
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied, "CLIENTE_NO_PERMITIDO")]
    [InlineData(StatusCode.PermissionDenied, "HERRAMIENTA_NO_PERMITIDA")]
    [InlineData(StatusCode.InvalidArgument, "NONCE_INVALIDO")]
    [InlineData(StatusCode.InvalidArgument, "KID_INVALIDO")]
    [InlineData(StatusCode.FailedPrecondition, "CLAVE_DESTINATARIO_DESCONOCIDA")]
    public async Task Rechazos_PermanentesDeCoreApi_SonNoConfigurado(StatusCode estado, string reason)
    {
        _core.Responder = _ => throw PlatformRpcErrors.ToRpcException(reason, "rechazado", estado);
        using var fuente = Fuente();

        var r = await fuente.ObtenerAsync(Ct);

        r.Falla.Should().Be(FallaCredencial.NoConfigurado);
        _core.Pedidos.Should().HaveCount(1, "sin KidAnterior no hay reintento");
    }

    [Fact]
    public async Task KidDesconocido_ConKidAnterior_ReintentaUnaVezConElAnteriorYOtroNonce()
    {
        RSA? publicaAnterior = RSA.Create();
        publicaAnterior.ImportSubjectPublicKeyInfo(ParAnterior.Value.ExportSubjectPublicKeyInfo(), out _);
        _core.Responder = p => p.Kid == V.Kid
            ? throw PlatformRpcErrors.ToRpcException("CLAVE_DESTINATARIO_DESCONOCIDA", "kid", StatusCode.FailedPrecondition)
            : Task.FromResult(CoreApiIaFalso.Valida(p, publicaAnterior, _reloj.GetUtcNow()));
        using var fuente = Fuente(conAnterior: true);

        var r = await fuente.ObtenerAsync(Ct);

        r.Falla.Should().Be(FallaCredencial.Ninguna);
        r.Credencial!.ApiKey.Should().Be(Centinela);
        _core.Pedidos.Select(p => p.Kid).Should().Equal(V.Kid, "diag-prueba-202610");
        _core.Pedidos.Select(p => p.Nonce).Distinct().Should().HaveCount(2);
        publicaAnterior.Dispose();
    }

    /// <summary>HU-7b: la anterior en variable (base64 PKCS#8) sirve en el reintento por <c>kid</c> igual que la de archivo.</summary>
    [Fact]
    public async Task KidDesconocido_ConAnteriorEnVariable_ReintentaYDescifraConLaAnterior()
    {
        using var publicaAnterior = RSA.Create();
        publicaAnterior.ImportSubjectPublicKeyInfo(ParAnterior.Value.ExportSubjectPublicKeyInfo(), out _);
        _core.Responder = p => p.Kid == V.Kid
            ? throw PlatformRpcErrors.ToRpcException("CLAVE_DESTINATARIO_DESCONOCIDA", "kid", StatusCode.FailedPrecondition)
            : Task.FromResult(CoreApiIaFalso.Valida(p, publicaAnterior, _reloj.GetUtcNow()));
        var opciones = new OpcionesIa
        {
            Origen = OpcionesIa.OrigenPlataforma,
            Kid = V.Kid,
            ClavePrivada = V.PrivadaBase64,
            KidAnterior = "diag-prueba-202610",
            ClavePrivadaAnterior = Convert.ToBase64String(ParAnterior.Value.ExportPkcs8PrivateKey()),
        };
        var claves = ClavesPrivadasIa.Cargar(opciones, out var motivo, out var aviso);
        motivo.Should().Be("OK");
        aviso.Should().BeNull();
        claves!.KidAnterior.Should().Be("diag-prueba-202610");
        using var fuente = new FuenteCredencialPlataforma(_core.Cliente, claves, Options.Create(opciones), _reloj, _logs.Logger<FuenteCredencialPlataforma>());

        var r = await fuente.ObtenerAsync(Ct);

        r.Falla.Should().Be(FallaCredencial.Ninguna);
        r.Credencial!.ApiKey.Should().Be(Centinela);
        _core.Pedidos.Select(p => p.Kid).Should().Equal(V.Kid, "diag-prueba-202610");
    }

    [Fact]
    public async Task SP12_UnaRespuestaValidaReenviadaAOtraSolicitud_SeRechazaComoTecnica()
    {
        ObtenerConfiguracionIaResponse? capturada = null;
        _core.Responder = p =>
        {
            capturada ??= CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow());
            return Task.FromResult(capturada);
        };
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Ninguna);
        fuente.Invalidar();
        var r = await fuente.ObtenerAsync(Ct);

        r.Falla.Should().Be(FallaCredencial.Tecnica);
        r.Credencial.Should().BeNull();
    }

    [Theory]
    [InlineData("kid")]
    [InlineData("tool_code")]
    public async Task SP12_EcoDistintoDelPedido_EsTecnica(string campo)
    {
        _core.Responder = p =>
        {
            var r = CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow());
            if (campo == "kid")
                r.Kid = "diag-otro-202611";
            else
                r.ToolCode = "ocr";
            return Task.FromResult(r);
        };
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
    }

    [Theory]
    [InlineData(121, FallaCredencial.Tecnica)]
    [InlineData(-121, FallaCredencial.Tecnica)]
    [InlineData(119, FallaCredencial.Ninguna)]
    [InlineData(-119, FallaCredencial.Ninguna)]
    public async Task SP12_DesfaseDeRelojMayorA120s_SeRechaza(int segundos, FallaCredencial esperada)
    {
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow().AddSeconds(segundos)));
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(esperada);
    }

    [Fact]
    public async Task SP11_LosNanosDeEmitidaEnNoEntranAlAad()
    {
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow().AddMilliseconds(250)));
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Ninguna);
    }

    [Theory]
    [InlineData("tag")]
    [InlineData("modelo_cambiado")]
    [InlineData("row_version_cambiada")]
    public async Task SP12_SobreManipulado_EsTecnica_NuncaNoConfigurado(string que)
    {
        _core.Responder = p =>
        {
            var r = CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow());
            switch (que)
            {
                case "tag":
                    var t = r.Cola[0].Llave.Tag.ToByteArray();
                    t[0] ^= 0xFF;
                    r.Cola[0].Llave.Tag = ByteString.CopyFrom(t);
                    break;
                case "modelo_cambiado":
                    r.Cola[0].Modelo = "claude-haiku-4-5-20251001";
                    break;
                default:
                    r.RowVersion = 8;
                    break;
            }

            return Task.FromResult(r);
        };
        using var fuente = Fuente(modelos: ["claude-haiku-4-5", "claude-haiku-4-5-20251001"]);

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        _logs.Todo.Should().NotContain("AuthenticationTagMismatch").And.NotContain(Centinela);
    }

    [Fact]
    public async Task SP12_AlgDesconocido_EsNoConfigurado()
    {
        _core.Responder = p =>
        {
            var r = CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow());
            r.Cola[0].Llave.Alg = "RSA-OAEP+A128GCM";
            return Task.FromResult(r);
        };
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.NoConfigurado);
    }

    [Fact]
    public async Task ProveedorFueraDeLaAllowlistLocal_SeSalta_YSeUsaAnthropic()
    {
        _core.Responder = p =>
        {
            var r = CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow());
            var ajeno = CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow(), llave: "sk-otro-proveedor", proveedor: "openai", modelo: "gpt-4o-mini").Cola[0];
            ajeno.Prioridad = 0;
            r.Cola.Insert(0, ajeno);
            return Task.FromResult(r);
        };
        using var fuente = Fuente();

        var c = await fuente.ObtenerAsync(Ct);

        c.Falla.Should().Be(FallaCredencial.Ninguna);
        c.Credencial!.Proveedor.Should().Be("anthropic");
        c.Credencial.ApiKey.Should().Be(Centinela);
    }

    [Fact]
    public async Task ColaSinAnthropic_EsNoConfigurado()
    {
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow(), llave: "sk-otro", proveedor: "kimi", modelo: "kimi-k2"));
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.NoConfigurado);
    }

    [Fact]
    public async Task SP14_ModeloFueraDeModelosPermitidos_EsNoConfigurado_ConLogError()
    {
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow(), modelo: "claude-opus-4-1"));
        using var fuente = Fuente();

        var r = await fuente.ObtenerAsync(Ct);

        r.Falla.Should().Be(FallaCredencial.NoConfigurado);
        _logs.Eventos.Should().Contain(e => e.Nivel == Microsoft.Extensions.Logging.LogLevel.Error);
        _logs.Todo.Should().NotContain(Centinela);
    }

    [Fact]
    public async Task ModelosPermitidosPorDefecto_SoloClaudeHaiku45()
    {
        new OpcionesIa().ModelosEfectivos.Should().Equal("claude-haiku-4-5");
        new OpcionesIa { ModelosPermitidos = [] }.ModelosEfectivos.Should().Equal("claude-haiku-4-5");
        new OpcionesIa { ModelosPermitidos = ["claude-sonnet-4-5"] }.ModelosEfectivos.Should().Equal("claude-sonnet-4-5");
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow(), modelo: "claude-sonnet-4-5"));
        using var fuente = Fuente(modelos: ["claude-sonnet-4-5"]);
        (await fuente.ObtenerAsync(Ct)).Credencial!.Modelo.Should().Be("claude-sonnet-4-5");
    }

    [Fact]
    public async Task RowVersionMenorQueLaAceptada_SeDescarta()
    {
        var version = 9L;
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow(), rowVersion: version));
        using var fuente = Fuente();
        (await fuente.ObtenerAsync(Ct)).Credencial!.RowVersion.Should().Be(9);

        version = 8;
        fuente.Invalidar();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
    }

    [Fact]
    public async Task Invalidar_TrasUn401DelProveedor_ObligaAPedirOtraVez()
    {
        using var fuente = Fuente();
        await fuente.ObtenerAsync(Ct);

        fuente.Invalidar();
        await fuente.ObtenerAsync(Ct);

        _core.Pedidos.Should().HaveCount(2);
    }

    [Fact]
    public async Task SP18_NingunLogContieneLaLlaveNiElSobre()
    {
        string? textoCifrado = null, cek = null;
        _core.Responder = p =>
        {
            var r = CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow());
            textoCifrado = r.Cola[0].Llave.TextoCifrado.ToBase64();
            cek = r.Cola[0].Llave.CekCifrada.ToBase64();
            return Task.FromResult(r);
        };
        using var fuente = Fuente();

        var c = await fuente.ObtenerAsync(Ct);

        c.Credencial!.ApiKey.Should().Be(Centinela);
        _logs.Eventos.Should().NotBeEmpty("la entrega se registra con proveedor, modelo y row_version");
        _logs.Todo.Should().NotContain(Centinela).And.NotContain(textoCifrado!).And.NotContain(cek!).And.NotContain(V.PrivadaBase64[..40]);
        _logs.Todo.Should().Contain("anthropic").And.Contain("claude-haiku-4-5");
        c.ToString().Should().NotContain(Centinela);
    }

    /// <summary>CR-2: un modelo fuera de formato no llega al log (inyección de líneas); se registra un código fijo.</summary>
    [Fact]
    public async Task CR2_ModeloNoPermitidoConFormatoInvalido_NoSeRegistraTalCual()
    {
        _core.Responder = p =>
        {
            var r = CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow());
            r.Cola[0].Modelo = "x\nINYECTADO";
            return Task.FromResult(r);
        };
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.NoConfigurado);

        _logs.Todo.Should().NotContain("INYECTADO").And.Contain("FORMATO_INVALIDO");
    }

    /// <summary>CR-2: un modelo con formato válido pero fuera de la allowlist sí se registra.</summary>
    [Fact]
    public async Task CR2_ModeloNoPermitidoConFormatoValido_SeRegistra()
    {
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow(), modelo: "claude-opus-4-1"));
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.NoConfigurado);

        _logs.Todo.Should().Contain("modelo=claude-opus-4-1");
    }

    /// <summary>CR-3: emitida_en fuera del rango representable es fallo técnico de reloj, sin excepción.</summary>
    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(253402300800L)]
    [InlineData(-62135596801L)]
    public async Task CR3_EmitidaEnFueraDeRango_EsTecnicaReloj_SinExcepcion(long segundos)
    {
        _core.Responder = p =>
        {
            var r = CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow());
            r.EmitidaEn = new Timestamp { Seconds = segundos };
            return Task.FromResult(r);
        };
        using var fuente = Fuente();

        var r = await fuente.ObtenerAsync(Ct);

        r.Falla.Should().Be(FallaCredencial.Tecnica);
        _logs.Todo.Should().Contain("RELOJ");
    }

    /// <summary>CR-5: row_version menor → Error; tras 3 rechazos seguidos se acepta y se reinicia la última aceptada.</summary>
    [Fact]
    public async Task CR5_RowVersionMenor_TresRechazosSeguidos_AlTerceroSeAceptaYSeReinicia()
    {
        var version = 9L;
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow(), rowVersion: version));
        using var fuente = Fuente();
        (await fuente.ObtenerAsync(Ct)).Credencial!.RowVersion.Should().Be(9);
        fuente.Invalidar();
        version = 5;

        // CR-1 (review final): cada rechazo queda 10 s en la vigencia negativa; se avanza el reloj para que haya otro pedido.
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        _reloj.Avanzar(TimeSpan.FromSeconds(10));
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        _reloj.Avanzar(TimeSpan.FromSeconds(10));
        var tercera = await fuente.ObtenerAsync(Ct);

        tercera.Falla.Should().Be(FallaCredencial.Ninguna);
        tercera.Credencial!.RowVersion.Should().Be(5);
        tercera.Credencial.ApiKey.Should().Be(Centinela);
        _logs.Eventos.Count(e => e.Nivel == Microsoft.Extensions.Logging.LogLevel.Error && e.Texto.Contains("row_version", StringComparison.Ordinal))
            .Should().BeGreaterThanOrEqualTo(2);

        fuente.Invalidar();
        (await fuente.ObtenerAsync(Ct)).Credencial!.RowVersion.Should().Be(5, "la última aceptada quedó reiniciada a 5");
        _core.Pedidos.Should().HaveCount(5);
    }

    /// <summary>CR-5: una respuesta con row_version aceptable corta la racha de rechazos.</summary>
    [Fact]
    public async Task CR5_LaRachaDeRechazosSeCortaConUnaRespuestaAceptable()
    {
        var version = 9L;
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow(), rowVersion: version));
        using var fuente = Fuente();
        await fuente.ObtenerAsync(Ct);
        fuente.Invalidar();

        version = 5;
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        _reloj.Avanzar(TimeSpan.FromSeconds(10));
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        _reloj.Avanzar(TimeSpan.FromSeconds(10));
        version = 9;
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Ninguna);
        fuente.Invalidar();
        version = 5;

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica, "la racha volvió a empezar");
    }

    /// <summary>
    /// CR-6: UNAUTHENTICATED de core-api es fallo técnico con log de nivel Error. Desde CR-1 (review final) queda 10 s en la
    /// vigencia negativa como todo fallo técnico.
    /// </summary>
    [Fact]
    public async Task CR6_Unauthenticated_EsTecnica_ConLogError()
    {
        _core.Responder = _ => throw new RpcException(new Status(StatusCode.Unauthenticated, "token " + Centinela));
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);

        _core.Pedidos.Should().HaveCount(1, "la segunda sale de la vigencia negativa de 10 s");
        _reloj.Avanzar(TimeSpan.FromSeconds(10));
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        _core.Pedidos.Should().HaveCount(2);
        _logs.Eventos.Should().Contain(e => e.Nivel == Microsoft.Extensions.Logging.LogLevel.Error && e.Texto.Contains("Unauthenticated", StringComparison.Ordinal));
        _logs.Todo.Should().NotContain(Centinela);
    }

    /// <summary>CR-8: una llave descifrada con caracteres fuera de 0x21–0x7E es SOBRE_INVALIDO y su valor no llega al log.</summary>
    [Theory]
    [InlineData(Centinela + "\n")]
    [InlineData(" " + Centinela)]
    [InlineData("sk-ant-test-CENTINELAé")]
    [InlineData("sk-ant-test-CENTINELA\u007f")]
    public async Task CR8_LlaveConCaracteresNoImprimibles_EsSobreInvalido_SinLoguearla(string llave)
    {
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow(), llave: llave));
        using var fuente = Fuente();

        var r = await fuente.ObtenerAsync(Ct);

        r.Falla.Should().Be(FallaCredencial.Tecnica, "camino actual de SOBRE_INVALIDO");
        r.Credencial.Should().BeNull();
        _logs.Todo.Should().Contain("SOBRE_INVALIDO").And.NotContain("CENTINELA");
    }

    /// <summary>
    /// CR-1 (review final): un fallo técnico queda 10 s en la vigencia negativa; dentro de ella no hay RPC y el log Warning no se
    /// repite. Pasados 10 s se vuelve a pedir.
    /// </summary>
    [Fact]
    public async Task CR1_Tecnica_VigenciaNegativaDe10s_SinRpcNiLogRepetido()
    {
        _core.Responder = _ => throw new RpcException(new Status(StatusCode.Unavailable, "reiniciando"));
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        _core.Pedidos.Should().HaveCount(1);
        _logs.Eventos.Count(e => e.Nivel == Microsoft.Extensions.Logging.LogLevel.Warning).Should().Be(1, "la llamada servida de la caché negativa no registra");

        _reloj.Avanzar(TimeSpan.FromSeconds(9));
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        _core.Pedidos.Should().HaveCount(1, "a los 9 s sigue vigente");

        _reloj.Avanzar(TimeSpan.FromSeconds(1));
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);
        _core.Pedidos.Should().HaveCount(2, "a los 10 s vence");
        _logs.Eventos.Count(e => e.Nivel == Microsoft.Extensions.Logging.LogLevel.Warning).Should().Be(2);
    }

    /// <summary>CR-1 (review final): <c>Invalidar()</c> borra también la vigencia negativa del fallo técnico.</summary>
    [Fact]
    public async Task CR1_Invalidar_BorraLaVigenciaNegativaDeTecnica()
    {
        _core.Responder = _ => throw new RpcException(new Status(StatusCode.Unavailable, "reiniciando"));
        using var fuente = Fuente();
        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(FallaCredencial.Tecnica);

        fuente.Invalidar();
        _core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, _publica, _reloj.GetUtcNow()));
        var r = await fuente.ObtenerAsync(Ct);

        r.Falla.Should().Be(FallaCredencial.Ninguna);
        _core.Pedidos.Should().HaveCount(2, "tras Invalidar hay RPC inmediato");
    }

    /// <summary>CR-1 (review final): 8 llamadas a la vez con core-api caído comparten un solo RPC (single-flight + vigencia negativa).</summary>
    [Fact]
    public async Task CR1_OchoConcurrentesConCoreApiCaido_UnSoloRpc()
    {
        _core.Responder = async _ =>
        {
            await Task.Delay(200);
            throw new RpcException(new Status(StatusCode.Unavailable, "reiniciando"));
        };
        using var fuente = Fuente();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fuente.ObtenerAsync(Ct)));

        resultados.Should().OnlyContain(r => r.Falla == FallaCredencial.Tecnica);
        _core.Pedidos.Should().HaveCount(1);
    }

    /// <summary>
    /// H-4 (security final): el <c>reason</c> del ErrorInfo de core-api solo llega al log si cumple <c>^[A-Za-z0-9_]{1,64}\z</c>;
    /// si no, se registra <c>(invalido)</c>. La clasificación por reason no cambia.
    /// </summary>
    [Theory]
    [InlineData(StatusCode.Unavailable, "x\nINYECTADO", FallaCredencial.Tecnica)]
    [InlineData(StatusCode.PermissionDenied, "x\nINYECTADO", FallaCredencial.NoConfigurado)]
    [InlineData(StatusCode.InvalidArgument, "x\nINYECTADO", FallaCredencial.NoConfigurado)]
    [InlineData(StatusCode.FailedPrecondition, "x\nINYECTADO", FallaCredencial.NoConfigurado)]
    [InlineData(StatusCode.Unavailable, "LARGO65", FallaCredencial.Tecnica)]
    [InlineData(StatusCode.PermissionDenied, "LARGO65", FallaCredencial.NoConfigurado)]
    public async Task H4_ReasonFueraDeFormato_SeRegistraComoInvalido(StatusCode estado, string reason, FallaCredencial esperada)
    {
        var enviado = reason == "LARGO65" ? "INYECTADO" + new string('A', 56) : reason;
        enviado.Length.Should().BeOneOf(11, 65);
        _core.Responder = _ => throw PlatformRpcErrors.ToRpcException(enviado, "rechazado", estado);
        using var fuente = Fuente();

        (await fuente.ObtenerAsync(Ct)).Falla.Should().Be(esperada);

        _logs.Todo.Should().Contain("(invalido)").And.NotContain("INYECTADO");
    }

    /// <summary>H-4: un reason con formato válido (hasta 64 caracteres) se sigue registrando tal cual.</summary>
    [Theory]
    [InlineData(StatusCode.PermissionDenied, "CLIENTE_NO_PERMITIDO")]
    [InlineData(StatusCode.Unavailable, "LARGO64")]
    public async Task H4_ReasonConFormatoValido_SeRegistraTalCual(StatusCode estado, string reason)
    {
        var enviado = reason == "LARGO64" ? "VALIDO_" + new string('A', 57) : reason;
        _core.Responder = _ => throw PlatformRpcErrors.ToRpcException(enviado, "rechazado", estado);
        using var fuente = Fuente();

        await fuente.ObtenerAsync(Ct);

        _logs.Todo.Should().Contain(enviado).And.NotContain("(invalido)");
    }
}
