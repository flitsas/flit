using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;
using Flit.Platform.Grpc.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen.Credencial;

/// <summary>
/// Vector compartido de ADR-0091 (<c>contracts/proto/flit/platform/v1/testdata/configuracion_ia_vector.json</c>). El par RSA es
/// SOLO de prueba: no es la clave de ningún ambiente.
/// </summary>
/// <example><code>var v = VectorIa.Cargar(); using var rsa = v.Privada();</code></example>
public sealed class VectorIa
{
    private readonly JsonElement _raiz;

    private VectorIa(JsonElement raiz) => _raiz = raiz;

    public static VectorIa Cargar()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ia", "configuracion_ia_vector.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
        return new VectorIa(doc.RootElement.Clone());
    }

    public string Texto(string nombre) => _raiz.GetProperty(nombre).GetString()!;

    public long Numero(string nombre) => _raiz.GetProperty(nombre).GetInt64();

    public byte[] Bytes(string nombre) => Convert.FromBase64String(Texto(nombre));

    public string Kid => Texto("kid");

    public string PrivadaBase64 => Texto("privada_pkcs8_b64");

    public byte[] Nonce => Bytes("nonce_b64");

    public long EmitidaSegundos => Numero("emitida_en_segundos");

    public DateTimeOffset Emitida => DateTimeOffset.FromUnixTimeSeconds(EmitidaSegundos);

    public RSA Privada()
    {
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(PrivadaBase64), out _);
        return rsa;
    }

    public RSA Publica()
    {
        var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Bytes("publica_spki_b64"), out _);
        return rsa;
    }

    public LlaveCifrada Sobre()
    {
        var s = _raiz.GetProperty("sobre");
        return new LlaveCifrada
        {
            Alg = s.GetProperty("alg").GetString(),
            Kid = s.GetProperty("kid").GetString(),
            CekCifrada = ByteString.FromBase64(s.GetProperty("cek_cifrada_b64").GetString()),
            Iv = ByteString.FromBase64(s.GetProperty("iv_b64").GetString()),
            TextoCifrado = ByteString.FromBase64(s.GetProperty("texto_cifrado_b64").GetString()),
            Tag = ByteString.FromBase64(s.GetProperty("tag_b64").GetString()),
        };
    }
}

/// <summary>Lo que haría core-api: sella una llave para la pública de Diagnóstico con el AAD canónico (SP-11).</summary>
public static class SelladorDePrueba
{
    public static LlaveCifrada Sellar(RSA publica, string kid, string proveedor, string modelo, long rowVersion, byte[] nonce, long emitidaSegundos, string llave,
        string toolCode = FuenteCredencialPlataforma.ToolCode)
    {
        var aad = SobreLlaveIa.ComponerAad(toolCode, proveedor, modelo, rowVersion, kid, nonce, emitidaSegundos);
        var cek = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(12);
        var claro = Encoding.UTF8.GetBytes(llave);
        var cifrado = new byte[claro.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16))
            aes.Encrypt(iv, claro, cifrado, tag, aad);
        return new LlaveCifrada
        {
            Alg = SobreLlaveIa.Alg,
            Kid = kid,
            CekCifrada = ByteString.CopyFrom(publica.Encrypt(cek, RSAEncryptionPadding.OaepSHA256)),
            Iv = ByteString.CopyFrom(iv),
            TextoCifrado = ByteString.CopyFrom(cifrado),
            Tag = ByteString.CopyFrom(tag),
        };
    }
}

/// <summary>Reloj que la prueba mueve a mano.</summary>
public sealed class RelojMovil(DateTimeOffset inicio) : TimeProvider
{
    private DateTimeOffset _ahora = inicio;

    public override DateTimeOffset GetUtcNow() => _ahora;

    public void Avanzar(TimeSpan cuanto) => _ahora += cuanto;
}

/// <summary>Logs en memoria con el texto ya formateado y la excepción, para buscar fugas.</summary>
public sealed class LogsIa : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Nivel, string Texto)> _eventos = new();

    public IReadOnlyCollection<(LogLevel Nivel, string Texto)> Eventos => _eventos;

    public string Todo => string.Join("\n", _eventos.Select(e => e.Texto));

    public ILogger<T> Logger<T>() => new Registro<T>(_eventos);

    public ILogger CreateLogger(string categoryName) => new Registro<object>(_eventos);

    public void Dispose()
    {
    }

    private sealed class Registro<T>(ConcurrentQueue<(LogLevel, string)> eventos) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            eventos.Enqueue((logLevel, formatter(state, exception) + (exception is null ? string.Empty : " | " + exception)));
    }
}

/// <summary>
/// core-api falso en memoria (<see cref="ConfiguracionIaService.ConfiguracionIaServiceBase"/> sobre TestServer): cada pedido pasa por
/// <see cref="Responder"/>, y se guardan los pedidos recibidos.
/// </summary>
public sealed class CoreApiIaFalso : IAsyncDisposable
{
    private WebApplication? _app;

    public ConcurrentQueue<ObtenerConfiguracionIaRequest> Pedidos { get; } = new();

    public Func<ObtenerConfiguracionIaRequest, Task<ObtenerConfiguracionIaResponse>> Responder { get; set; } =
        _ => throw new RpcException(new Status(StatusCode.Unimplemented, "sin respuesta"));

    public HttpMessageHandler Handler => _app!.GetTestServer().CreateHandler();

    public static async Task<CoreApiIaFalso> IniciarAsync()
    {
        var falso = new CoreApiIaFalso();
        var b = WebApplication.CreateBuilder();
        b.WebHost.UseTestServer();
        b.Services.AddGrpc();
        b.Services.AddSingleton(falso);
        falso._app = b.Build();
        falso._app.MapGrpcService<Servicio>();
        await falso._app.StartAsync(TestContext.Current.CancellationToken);
        return falso;
    }

    public ConfiguracionIaService.ConfiguracionIaServiceClient Cliente() =>
        new(GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = Handler }));

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    /// <summary>Respuesta válida para <paramref name="pedido"/>: eco de nonce y kid, sellada con la pública del vector.</summary>
    public static ObtenerConfiguracionIaResponse Valida(ObtenerConfiguracionIaRequest pedido, RSA publica, DateTimeOffset emitida,
        string llave = "sk-ant-test-CENTINELA", string modelo = "claude-haiku-4-5", string proveedor = "anthropic", long rowVersion = 7, int ttl = 300)
    {
        var r = new ObtenerConfiguracionIaResponse
        {
            ToolCode = pedido.ToolCode,
            Disponible = true,
            ProveedorActivo = proveedor,
            RowVersion = rowVersion,
            TtlSegundos = ttl,
            EmitidaEn = Timestamp.FromDateTimeOffset(emitida),
            Nonce = pedido.Nonce,
            Kid = pedido.Kid,
        };
        r.Cola.Add(new ProveedorIa
        {
            Proveedor = proveedor,
            Prioridad = 1,
            Modelo = modelo,
            Llave = SelladorDePrueba.Sellar(publica, pedido.Kid, proveedor, modelo, rowVersion, pedido.Nonce.ToByteArray(), emitida.ToUnixTimeSeconds(), llave),
        });
        return r;
    }

    private sealed class Servicio(CoreApiIaFalso falso) : ConfiguracionIaService.ConfiguracionIaServiceBase
    {
        public override Task<ObtenerConfiguracionIaResponse> ObtenerConfiguracionIa(ObtenerConfiguracionIaRequest request, ServerCallContext context)
        {
            falso.Pedidos.Enqueue(request.Clone());
            return falso.Responder(request);
        }
    }
}
