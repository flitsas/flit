using System.Net;
using System.Text;
using Flit.Comparendos.Api.Modulos.Ocr;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Domain.Modelo;
using Flit.Platform.Sdk.IaConsumo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// ADR-0090 — el OCR de comparendos deja una llamada registrada por cada petición a Anthropic (con el <c>usage</c> que
/// antes no se leía) sin cambiar lo que devuelve, y la base mapea las tablas del contrato ia-consumo v1 en su esquema.
/// </summary>
public sealed class IaConsumoOcrTests
{
    private sealed class RegistradorFalso : IIaConsumoRegistrador
    {
        public List<(IaLlamada Llamada, IaAmbitoActual? Ambito)> Llamadas { get; } = [];
        public IIaCallContext Contexto => IaCallContext.Instancia;
        public bool Activo => true;
        public bool DetalleActivo => false;
        public void Registrar(IaLlamada llamada) => Llamadas.Add((llamada, Contexto.Actual));
    }

    private sealed class Handler(Func<HttpResponseMessage> respuesta) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respuesta());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string cuerpo) =>
        new(status) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static async Task<(ResultadoOcr R, RegistradorFalso Reg)> Extraer(Func<HttpResponseMessage> respuesta)
    {
        var reg = new RegistradorFalso();
        var proveedor = new ProveedorOcrAnthropic(new HttpClient(new Handler(respuesta)),
            Options.Create(new OpcionesAnthropic { ApiKey = "k" }), reg);
        ResultadoOcr r;
        using (IaCallContext.Instancia.Iniciar(new IaAmbito
               {
                   Modulo = IaProcesosFlit.ModuloComparendos, Proceso = IaProcesosFlit.OcrComparendo, Origen = IaOrigenes.Job,
               }))
        {
            r = await proveedor.ExtraerAsync(new byte[50], "application/pdf", "PROMPT", "claude-haiku-5-5", TestContext.Current.CancellationToken);
        }
        return (r, reg);
    }

    [Fact]
    public async Task Exito_LeeElUsageYElAmbito()
    {
        var (r, reg) = await Extraer(() => Json(HttpStatusCode.OK, """
            {"id":"msg_7","model":"claude-haiku-5-5","stop_reason":"end_turn",
             "content":[{"type":"text","text":"{\"placa\":\"ABC123\"}"}],
             "usage":{"input_tokens":2500,"output_tokens":300,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}
            """));

        r.Exitoso.Should().BeTrue();
        var (llamada, ambito) = reg.Llamadas.Single();
        llamada.Resultado.Should().Be(IaResultados.Exito);
        llamada.TokensEntrada.Should().Be(2500);
        llamada.TokensSalida.Should().Be(300);
        llamada.IdRespuestaProveedor.Should().Be("msg_7");
        llamada.ModeloSolicitado.Should().Be("claude-haiku-5-5");
        llamada.ArchivoBytes.Should().Be(50);
        ambito!.Ambito.Proceso.Should().Be(IaProcesosFlit.OcrComparendo);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, IaResultados.RateLimit, TipoFalloOcr.RateLimit)]
    [InlineData(HttpStatusCode.InternalServerError, IaResultados.ErrorProveedor, TipoFalloOcr.Http5xx)]
    [InlineData(HttpStatusCode.BadRequest, IaResultados.Rechazado, TipoFalloOcr.ArchivoInvalido)]
    public async Task NoExito_RegistraElResultadoDelContratoYDevuelveLoMismoQueAntes(HttpStatusCode status, string resultado, TipoFalloOcr fallo)
    {
        var (r, reg) = await Extraer(() => Json(status, """{"type":"error","error":{"type":"x_error","message":"m"}}"""));

        r.TipoFallo.Should().Be(fallo);
        var llamada = reg.Llamadas.Single().Llamada;
        llamada.Resultado.Should().Be(resultado);
        llamada.HttpStatus.Should().Be((int)status);
        llamada.ErrorCodigo.Should().Be("x_error");
    }

    [Fact]
    public async Task ErrorDeRed_SeRegistraComoRed()
    {
        var (r, reg) = await Extraer(() => throw new HttpRequestException("sin ruta"));

        r.TipoFallo.Should().Be(TipoFalloOcr.Red);
        reg.Llamadas.Single().Llamada.Resultado.Should().Be(IaResultados.Red);
    }

    [Fact]
    public void LaBaseMapeaLasTablasDelContratoEnSuEsquema()
    {
        using var db = new ComparendosDb(new DbContextOptionsBuilder<ComparendosDb>().UseNpgsql("Host=127.0.0.1;Database=solo_modelo").Options);
        var modelo = db.GetService<IDesignTimeModel>().Model;
        foreach (var (tipo, tabla) in new[] { (typeof(IaConsumoLog), "ia_consumo_log"), (typeof(IaConsumoDetalle), "ia_consumo_detalle"), (typeof(IaTarifaModelo), "ia_tarifa_modelo") })
        {
            var entidad = modelo.FindEntityType(tipo)!;
            entidad.GetSchema().Should().Be(ComparendosDb.Schema);
            entidad.GetTableName().Should().Be(tabla);
        }
    }
}
