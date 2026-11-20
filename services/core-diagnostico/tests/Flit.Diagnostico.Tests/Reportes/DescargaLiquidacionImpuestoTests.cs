using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>Descarga HTTP de liquidación: fileId del payload, 409 si no completa, mapeo gRPC.</summary>
public sealed class DescargaLiquidacionImpuestoTests
{
    private static readonly byte[] PdfMinimo = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    private const string PayloadConLiquidacion =
        """{"monto_total":1000,"tiene_deuda":true,"vigencias":[{"anio":2025,"valor":1000,"pagada":false,"liquidacion_id":"file-2025"}],"liquidacion_consolidada_id":"file-cons","departamento_codigo":"05"}""";

    [Fact]
    public async Task Descarga_usa_fileId_del_payload_y_devuelve_pdf()
    {
        var ct = TestContext.Current.CancellationToken;
        var falsa = new ClienteConsultasFalsa(PdfMinimo);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Services.RemoveAll<ClienteConsultas>();
            b.Services.AddSingleton<ClienteConsultas>(falsa);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.ImpuestoDepartamental], vehiculos:
            [new("TST900", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.ImpuestoDepartamental, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST900", Fuente.ImpuestoDepartamental), PayloadConLiquidacion, ct);

        var resp = await empresa.Cliente().GetAsync(Ruta(lote.Id, lote.Vehiculos.Single().Id, "2025"), ct);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await resp.Content.ReadAsByteArrayAsync(ct)).Should().StartWith("%PDF-"u8.ToArray());
        falsa.UltimoArchivoId.Should().Be("file-2025");
    }

    [Fact]
    public async Task Celda_no_completada_es_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.ImpuestoDepartamental], vehiculos:
            [new("TST901", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.ImpuestoDepartamental, EstadoCelda.Pendiente, 1)])]);

        var resp = await empresa.Cliente().GetAsync(Ruta(lote.Id, lote.Vehiculos.Single().Id, "2025"), ct);
        var cuerpo = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        cuerpo.GetProperty("code").GetString().Should().Be(DescargaLiquidacionImpuesto.CodigoNoDisponible);
    }

    [Fact]
    public async Task Otra_compania_es_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, alcance: [Fuente.Runt, Fuente.ImpuestoDepartamental], vehiculos:
            [new("TST902", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.ImpuestoDepartamental, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresas.A, lote.Celda("TST902", Fuente.ImpuestoDepartamental), PayloadConLiquidacion, ct);

        var resp = await empresas.B.Cliente().GetAsync(Ruta(lote.Id, lote.Vehiculos.Single().Id, "2025"), ct);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(StatusCode.NotFound, HttpStatusCode.NotFound, DescargaLiquidacionImpuesto.CodigoNoEncontrada)]
    [InlineData(StatusCode.FailedPrecondition, HttpStatusCode.Conflict, DescargaLiquidacionImpuesto.CodigoCredencialNoConfigurada)]
    [InlineData(StatusCode.PermissionDenied, HttpStatusCode.BadGateway, DescargaLiquidacionImpuesto.CodigoCredencialRechazada)]
    [InlineData(StatusCode.Unavailable, HttpStatusCode.ServiceUnavailable, DescargaLiquidacionImpuesto.CodigoFuenteNoDisponible)]
    public async Task Mapea_cada_codigo_grpc(StatusCode grpc, HttpStatusCode http, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Services.RemoveAll<ClienteConsultas>();
            b.Services.AddSingleton<ClienteConsultas>(new ClienteConsultasFalsa(null, grpc, code));
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.ImpuestoDepartamental], vehiculos:
            [new("TST903", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.ImpuestoDepartamental, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST903", Fuente.ImpuestoDepartamental), PayloadConLiquidacion, ct);

        var resp = await empresa.Cliente().GetAsync(Ruta(lote.Id, lote.Vehiculos.Single().Id, "consolidada"), ct);
        var cuerpo = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);

        resp.StatusCode.Should().Be(http);
        cuerpo.GetProperty("code").GetString().Should().Be(code);
    }

    private static string Ruta(Guid id, Guid vid, string vigencia) =>
        Program.Prefijo + ReportesEndpoints.RutaLiquidacionImpuesto
            .Replace("{id:guid}", id.ToString(), StringComparison.Ordinal)
            .Replace("{vid:guid}", vid.ToString(), StringComparison.Ordinal)
            .Replace("{vigencia}", vigencia, StringComparison.Ordinal);

    private sealed class ClienteConsultasFalsa : ClienteConsultas
    {
        private readonly byte[]? _pdf;
        private readonly StatusCode? _error;
        private readonly string? _reason;

        public string? UltimoArchivoId { get; private set; }

        public ClienteConsultasFalsa(byte[]? pdf, StatusCode? error = null, string? reason = null)
            : base(null!, NullLogger<ClienteConsultas>.Instance)
        {
            _pdf = pdf;
            _error = error;
            _reason = reason;
        }

        public override Task<RespuestaConsultas<T>> LlamarAsync<T>(
            Guid empresa, Func<ConsultasService.ConsultasServiceClient, CallOptions, AsyncUnaryCall<T>> llamada, CancellationToken ct)
            where T : class
        {
            if (_error is { } codigo)
            {
                return Task.FromResult(new RespuestaConsultas<T>(null, new ErrorConsulta("X", "x"))
                {
                    Codigo = codigo,
                    Reason = _reason,
                });
            }

            if (typeof(T) != typeof(DescargarLiquidacionImpuestoResponse))
                throw new InvalidOperationException("Solo descarga.");
            UltimoArchivoId = "file-2025";
            var ok = new DescargarLiquidacionImpuestoResponse
            {
                Contenido = ByteString.CopyFrom(_pdf!),
                ContentType = "application/pdf",
            };
            return Task.FromResult(new RespuestaConsultas<T>((T)(object)ok, null));
        }
    }
}
