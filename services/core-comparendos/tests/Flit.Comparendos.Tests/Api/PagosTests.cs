using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13493 — registro de pagos individual y por Excel (§6.19).</summary>
public sealed class PagosTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly string[] Operador = [Permisos.BandejaVer, Permisos.PagosRegistrar, Permisos.PagosCargarMasivo, Permisos.DpGenerar];

    private async Task<(Guid Empresa, Dictionary<string, Guid> Ids, HttpClient Cliente)> PrepararAsync(params ItemFuente[] items)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api, dp: true);
        var ids = await IngresarAsync(api, empresa, items);
        return (empresa, ids, api.Cliente(new UsuarioDePrueba(empresa, Operador)));
    }

    private static MultipartFormDataContent Pago(string tipo, string valor, string fecha)
    {
        var form = new MultipartFormDataContent { { new StringContent(tipo), "tipo" }, { new StringContent(valor), "valor" }, { new StringContent(fecha), "fecha" } };
        return form;
    }

    [Fact]
    public async Task AC1_PagoIndividual_MarcaPagado_Timeline_YBloqueaUnSegundoPago()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, ids, cliente) = await PrepararAsync(Item("PG-1", valor: 604100m));
        var id = ids["PG1"];

        var r = await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/pago", Pago("bc", "302.050", "2026-10-08"), ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("diferencia").GetDecimal().Should().Be(-302050m);

        var c = await api.EnDbAsync(db => db.Comparendos.SingleAsync(x => x.Id == id));
        c.Pagado.Should().BeTrue();
        c.Gestionado.Should().BeTrue();
        var pago = await api.EnDbAsync(db => db.Pagos.SingleAsync(p => p.ComparendoId == id));
        (pago.TipoPago, pago.ValorPagado, pago.Origen).Should().Be((TipoPago.BC, 302050m, OrigenPago.Manual));
        (await api.EnDbAsync(db => db.Timeline.AnyAsync(e => e.ComparendoId == id && e.Tipo == TipoEventoTimeline.PagoRegistrado))).Should().BeTrue();

        var otra = await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/pago", Pago("PC", "100", "2026-10-08"), ct);
        otra.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("XX", "1000", "2026-10-08", "TIPO_PAGO_INVALIDO")]
    [InlineData("CC", "0", "2026-10-08", "VALOR_INVALIDO")]
    [InlineData("CC", "1000", "2026-10-10", "FECHA_INVALIDA")]
    public async Task AC2_PagoIndividual_Validaciones(string tipo, string valor, string fecha, string codigo)
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, ids, cliente) = await PrepararAsync(Item($"PV-{codigo}"));
        var r = await cliente.PostAsync($"/api/v1/comparendos/comparendos/{ids.Values.Single()}/pago", Pago(tipo, valor, fecha), ct);
        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadAsStringAsync(ct)).Should().Contain(codigo);
    }

    [Fact]
    public async Task AC3_CargaMasiva_AplicaValidas_YDevuelveErroresPorFila()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, ids, cliente) = await PrepararAsync(Item("CM-1", placa: "ABC124"), Item("CM-2", placa: "XYZ987"), Item("CM-3", placa: "ABC124"));
        var xlsx = Libro(
            ["Tipo pago", "Placa", "Número comparendo", "Valor", "Fecha pago"],
            ["BC", "abc-124", "CM-1", 604100, 46303], // 46303 = 08/10/2026 en serie de Excel
            ["PC", "ABC124", "CM-2", "1.000", "08/10/2026"], // placa equivocada
            ["CC", "XYZ987", "NO-EXISTE", "1000", "2026-10-08"],
            ["PC", "ABC124", "CM-1", "1000", "2026-10-08"], // repetido
            ["ZZ", "ABC124", "CM-3", "-5", "2030-01-01"]);

        var form = new MultipartFormDataContent { { new ByteArrayContent(xlsx), "archivo", "pagos.xlsx" } };
        var r = await cliente.PostAsync("/api/v1/comparendos/pagos/cargas", form, ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        var resumen = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        resumen.GetProperty("estado").GetString().Should().Be("ProcesadaConErrores");
        (resumen.GetProperty("totalFilas").GetInt32(), resumen.GetProperty("filasValidas").GetInt32()).Should().Be((5, 1));
        var filas = resumen.GetProperty("filas").EnumerateArray().ToDictionary(f => f.GetProperty("numeroFila").GetInt32(), f => string.Join("|", f.GetProperty("errores").EnumerateArray().Select(e => e.GetString())));
        filas[2].Should().BeEmpty();
        filas[3].Should().Contain("placa no corresponde");
        filas[4].Should().Contain("no encontrado");
        filas[5].Should().Contain("repetido en el archivo (fila 2)").And.Contain("ya tiene un pago");
        filas[6].Should().Contain("Tipo de pago inválido").And.Contain("Valor inválido").And.Contain("Fecha inválida");

        var pago = await api.EnDbAsync(db => db.Pagos.SingleAsync(p => p.ComparendoId == ids["CM1"]));
        (pago.Origen, pago.FechaPago, pago.ValorPagado).Should().Be((OrigenPago.MasivoExcel, new DateOnly(2026, 10, 8), 604100m));
        (await api.EnDbAsync(db => db.Pagos.CountAsync(p => p.TenantId == empresa))).Should().Be(1);

        var errores = await cliente.GetAsync($"/api/v1/comparendos/pagos/cargas/{resumen.GetProperty("id").GetGuid()}/errores", ct);
        errores.StatusCode.Should().Be(HttpStatusCode.OK);
        using var libro = SpreadsheetDocument.Open(new MemoryStream(await errores.Content.ReadAsByteArrayAsync(ct)), false);
        libro.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Row>().Count().Should().Be(5); // encabezado + 4 filas con error

        // Otra empresa no ve la carga.
        var ajena = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), Operador));
        (await ajena.GetAsync($"/api/v1/comparendos/pagos/cargas/{resumen.GetProperty("id").GetGuid()}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AC4_CargaMasiva_RechazaColumnasFaltantes_YLaPlantillaSeDescarga()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, _, cliente) = await PrepararAsync(Item("CR-1"));
        var form = new MultipartFormDataContent { { new ByteArrayContent(Libro(["Placa", "Valor"], ["ABC124", 1])), "archivo", "malo.xlsx" } };
        var r = await cliente.PostAsync("/api/v1/comparendos/pagos/cargas", form, ct);
        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadAsStringAsync(ct)).Should().Contain("tipo_pago").And.Contain("CARGA_RECHAZADA");
        (await api.EnDbAsync(db => db.CargasMasivasPagos.SingleAsync(c => c.TenantId == empresa))).Estado.Should().Be(EstadoCargaMasiva.Rechazada);

        var plantilla = await cliente.GetAsync("/api/v1/comparendos/pagos/plantilla", ct);
        plantilla.StatusCode.Should().Be(HttpStatusCode.OK);
        var filas = Flit.Comparendos.Api.Infraestructura.Excel.LectorExcel.LeerPrimeraHoja(new MemoryStream(await plantilla.Content.ReadAsByteArrayAsync(ct)));
        filas[0].Values.Select(c => c.Texto).Should().Equal("tipo_pago", "placa", "numero_comparendo", "valor", "fecha_pago");
    }

    /// <summary>Libro mínimo con textos en línea y números, con celdas sin referencia omitida.</summary>
    private static byte[] Libro(params object[][] filas)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var wb = doc.AddWorkbookPart();
            wb.Workbook = new Workbook();
            var ws = wb.AddNewPart<WorksheetPart>();
            var datos = new SheetData();
            for (var i = 0; i < filas.Length; i++)
            {
                var row = new Row { RowIndex = (uint)(i + 1) };
                for (var j = 0; j < filas[i].Length; j++)
                {
                    var referencia = $"{(char)('A' + j)}{i + 1}";
                    row.Append(filas[i][j] switch
                    {
                        int n => new Cell { CellReference = referencia, CellValue = new CellValue(n) },
                        var o => new Cell { CellReference = referencia, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(o.ToString()!)) },
                    });
                }

                datos.Append(row);
            }

            ws.Worksheet = new Worksheet(datos);
            wb.Workbook.AppendChild(new Sheets(new Sheet { Id = wb.GetIdOfPart(ws), SheetId = 1, Name = "Pagos" }));
        }

        return ms.ToArray();
    }
}
