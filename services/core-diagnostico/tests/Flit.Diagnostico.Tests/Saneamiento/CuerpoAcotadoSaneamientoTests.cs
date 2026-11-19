using System.Net;
using System.Text;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// CR-11 / SF-3 / AP-5 (HU #14576): las rutas nuevas que reciben JSON, del cliente y del backoffice, tienen un tope propio de 64 KiB
/// para el cuerpo. Un cuerpo mayor responde 413 <c>CUERPO_MUY_GRANDE</c> sin parsearlo y sin cambiar nada, con <c>Content-Length</c>
/// y también sin él (cuerpo por trozos). Un cuerpo legítimo grande (el masivo con 500 ids) sigue entrando. La subida de la evidencia
/// queda fuera: tiene su propio tope (<c>EvidenciaSaneamientoTests</c>). PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~CuerpoAcotadoSaneamientoTests
/// </code>
/// </example>
public sealed class CuerpoAcotadoSaneamientoTests
{
    private const int Tope = 64 * 1024;
    private const string Codigo = "CUERPO_MUY_GRANDE";

    // CR-11 — cada ruta JSON nueva con un cuerpo de 64 KiB + 1 responde 413 CUERPO_MUY_GRANDE y el saneamiento no cambia.
    [Fact]
    public async Task HU14576_CR11_cuerpo_JSON_mayor_que_el_tope_es_413_en_las_rutas_nuevas()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBT101", EstadoSaneamiento.EnGestion, ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Pendiente, ct);
        var antes = (await GestionDePrueba.LeerAsync(empresa, s.Id, ct)).Xmin;
        var flit = GestionDePrueba.TokenFlit(api);
        var compania = SaneamientoDePrueba.Token(api, empresa.Id);
        var descarga = DeudaDePrueba.TokenDescargar(api, empresa.Id);
        var rutas = new (string Nombre, string Token, HttpMethod Metodo, string Ruta)[]
        {
            ("masivo del cliente", compania, HttpMethod.Post, SaneamientoDePrueba.RutaMasivo),
            ("Excel de varias placas", descarga, HttpMethod.Post, DeudaDePrueba.RutaExcelVarios),
            ("tomar varios", flit, HttpMethod.Post, GestionDePrueba.Base + "/tomar"),
            ("anular", flit, HttpMethod.Post, GestionDePrueba.Ruta(s.Id, empresa.Id, "/anular")),
            ("cerrar", flit, HttpMethod.Post, GestionDePrueba.Ruta(s.Id, empresa.Id, "/cerrar")),
            ("reabrir", flit, HttpMethod.Post, GestionDePrueba.Ruta(s.Id, empresa.Id, "/reabrir")),
            ("observación", flit, HttpMethod.Put, GestionDePrueba.Ruta(s.Id, empresa.Id, "/observacion")),
            ("agregar renglón", flit, HttpMethod.Post, GestionDePrueba.Ruta(s.Id, empresa.Id, "/renglones")),
            ("editar renglón", flit, HttpMethod.Put, GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id)),
        };

        var resultados = new List<string>();
        foreach (var (nombre, token, metodo, ruta) in rutas)
        {
            using var pedido = new HttpRequestMessage(metodo, ruta) { Content = new StringContent(Grande(Tope + 1), Encoding.UTF8, "application/json") };
            var respuesta = await api.Cliente(token).SendAsync(pedido, ct);
            resultados.Add($"{nombre}: {(int)respuesta.StatusCode} {await SaneamientoDePrueba.CodigoAsync(respuesta, ct)}");
        }

        resultados.Should().OnlyContain(r => r.EndsWith($": 413 {Codigo}", StringComparison.Ordinal), string.Join(" | ", resultados));
        var despues = await GestionDePrueba.LeerAsync(empresa, s.Id, ct);
        (despues.Estado, despues.Xmin).Should().Be((EstadoSaneamiento.EnGestion, antes), "un cuerpo rechazado no escribe nada");
    }

    // CR-11 — sin Content-Length (cuerpo por trozos) el tope actúa al leer: 413 CUERPO_MUY_GRANDE.
    [Fact]
    public async Task HU14576_CR11_cuerpo_por_trozos_sin_Content_Length_tambien_es_413()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        using var contenido = new StreamContent(new FlujoSinLargo(Encoding.UTF8.GetBytes(Grande(Tope * 2))));
        contenido.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        contenido.Headers.ContentLength.Should().BeNull("el flujo no expone su largo: se envía por trozos");

        var respuesta = await api.Cliente(GestionDePrueba.TokenFlit(api)).PostAsync(GestionDePrueba.Base + "/tomar", contenido, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, await respuesta.Content.ReadAsStringAsync(ct));
        (await SaneamientoDePrueba.CodigoAsync(respuesta, ct)).Should().Be(Codigo);
    }

    // CR-11 — el tope no corta un cuerpo legítimo: el masivo del cliente con 500 ids distintos (unos 20 KB) responde 200.
    [Fact]
    public async Task HU14576_CR11_un_cuerpo_legitimo_bajo_el_tope_sigue_entrando()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var ids = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToArray();
        using var contenido = GestionDePrueba.Json(new { vehiculo_ids = ids });
        (await contenido.ReadAsByteArrayAsync(ct)).Length.Should().BeInRange(10_000, Tope);

        var respuesta = await api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id)).PostAsync(SaneamientoDePrueba.RutaMasivo, contenido, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Un objeto JSON válido de exactamente <paramref name="bytes"/> bytes (ASCII), con un texto de relleno.</summary>
    private static string Grande(int bytes)
    {
        const string inicio = "{\"version\":1,\"motivo\":\"";
        const string fin = "\"}";
        return inicio + new string('a', bytes - inicio.Length - fin.Length) + fin;
    }

    /// <summary>Un flujo de solo lectura que no expone su largo (HttpClient lo manda por trozos).</summary>
    private sealed class FlujoSinLargo(byte[] bytes) : Stream
    {
        private readonly MemoryStream _interno = new(bytes, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _interno.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _interno.Dispose();
            base.Dispose(disposing);
        }
    }
}
