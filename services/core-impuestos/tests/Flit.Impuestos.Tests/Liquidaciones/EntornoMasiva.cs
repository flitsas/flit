using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Ejecucion;
using Flit.Impuestos.Tests.Usuarios;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Impuestos.Tests.Liquidaciones;

/// <summary>
/// Entorno común de las pruebas de M12 (HU #14694): la API de verdad sobre una base efímera, como <see cref="EntornoEjecucion"/>
/// (API de liquidación y RUNT dobles, reconciliador y conciliación apagados), más el directorio de compañías como doble (para el
/// nombre del correo) y, si se da, <c>Impuestos:Avisos:UrlFrontend</c>. Arma archivos CSV y XLSX de placas, el multipart de la carga
/// y lee de la outbox los trabajos <c>notificaciones.email.send</c>.
/// </summary>
/// <example>
/// <code>
/// await using var api = await EntornoMasiva.ConApiAsync(urlFrontend: "https://impuestos.flit.prueba");
/// var r = await cliente.PostAsync(EntornoMasiva.Masiva, EntornoMasiva.Archivo(EntornoMasiva.Csv("placa", "ABC123"), "placas.csv"), ct);
/// </code>
/// </example>
internal static class EntornoMasiva
{
    public const string Liquidaciones = Program.Prefijo + "/liquidaciones";
    public const string Analizar = Liquidaciones + "/masiva/analizar";
    public const string Masiva = Liquidaciones + "/masiva";
    public const string Plantilla = Liquidaciones + "/plantilla";
    public const string Notificaciones = Program.Prefijo + "/notificaciones";
    public const string TipoCorreo = "notificaciones.email.send";
    public const string NombreEmpresa = "Compañía Demo";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static Task<ApiDePrueba> ConApiAsync(
        ApiLiquidacionDePrueba? proveedor = null, RuntDePrueba? runt = null, string? urlFrontend = null, DirectorioEmpresasDePrueba? directorio = null,
        TimeProvider? reloj = null, Action<Microsoft.AspNetCore.Builder.WebApplicationBuilder>? configurar = null) =>
        ApiDePrueba.ConBaseAsync(b =>
        {
            var config = new Dictionary<string, string?>
            {
                ["Impuestos:Api:BaseUrl"] = ClienteApiLiquidacionTests.Base,
                [GeneradorTokenAviso.ClaveUrlBase] = EntornoEjecucion.UrlAvisos,
                ["Impuestos:Ejecucion:Reconciliador:Activo"] = "false",
                ["Impuestos:Ejecucion:TasaPorSegundo"] = "1000",
                ["Impuestos:Conciliacion:TrabajadorActivo"] = "false",
            };
            if (urlFrontend is not null)
                config["Impuestos:Avisos:UrlFrontend"] = urlFrontend;
            b.Configuration.AddInMemoryCollection(config);
            b.Services.AddHttpClient<ClienteApiLiquidacion>().ConfigurePrimaryHttpMessageHandler(() => proveedor ?? new ApiLiquidacionDePrueba());
            b.Services.AddSingleton<IClienteRunt>(runt ?? new RuntDePrueba());
            if (directorio is not null)
                b.Services.AddSingleton<IDirectorioEmpresas>(directorio);
            if (reloj is not null)
                b.Services.AddSingleton(reloj);
            configurar?.Invoke(b);
        });

    /// <summary>CSV en UTF-8 (con BOM si se pide): una línea por elemento, tal cual.</summary>
    public static byte[] Csv(params string[] lineas) => Encoding.UTF8.GetBytes(string.Join("\r\n", lineas));

    public static byte[] CsvConBom(params string[] lineas) => [.. Encoding.UTF8.GetPreamble(), .. Csv(lineas)];

    /// <summary>Un .xlsx con una hoja <paramref name="hoja"/>: la primera fila es el encabezado; null deja la celda vacía.</summary>
    public static byte[] Xlsx(IReadOnlyList<string?[]> filas, string hoja = "Hoja1")
    {
        using var flujo = new MemoryStream();
        using (var libro = SpreadsheetDocument.Create(flujo, SpreadsheetDocumentType.Workbook))
        {
            var partes = libro.AddWorkbookPart();
            partes.Workbook = new Workbook();
            var parte = partes.AddNewPart<WorksheetPart>();
            var datos = new SheetData();
            for (var f = 0; f < filas.Count; f++)
            {
                var fila = new Row { RowIndex = (uint)f + 1 };
                for (var c = 0; c < filas[f].Length; c++)
                {
                    if (filas[f][c] is not { } valor)
                        continue;
                    fila.Append(new Cell
                    {
                        CellReference = $"{(char)('A' + c)}{f + 1}",
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(valor)),
                    });
                }

                datos.Append(fila);
            }

            parte.Worksheet = new Worksheet(datos);
            partes.Workbook.AppendChild(new Sheets(new Sheet { Id = partes.GetIdOfPart(parte), SheetId = 1, Name = hoja }));
        }

        return flujo.ToArray();
    }

    /// <summary>Encabezados y filas de la primera hoja de un .xlsx (texto de cada celda).</summary>
    public static (string Hoja, List<List<string>> Filas) LeerXlsx(byte[] contenido)
    {
        using var flujo = new MemoryStream(contenido);
        using var libro = SpreadsheetDocument.Open(flujo, false);
        var partes = libro.WorkbookPart!;
        var hoja = partes.Workbook.Sheets!.Elements<Sheet>().First();
        var parte = (WorksheetPart)partes.GetPartById(hoja.Id!.Value!);
        var filas = parte.Worksheet.Descendants<Row>()
            .Select(r => r.Elements<Cell>().Select(c => c.InlineString?.InnerText ?? c.CellValue?.Text ?? string.Empty).ToList())
            .ToList();
        return (hoja.Name!.Value!, filas);
    }

    /// <summary>El multipart de la carga con la parte <c>archivo</c>.</summary>
    public static MultipartFormDataContent Archivo(byte[] contenido, string nombre, string tipo = "application/octet-stream")
    {
        var parte = new ByteArrayContent(contenido);
        parte.Headers.ContentType = new MediaTypeHeaderValue(tipo);
        return new MultipartFormDataContent { { parte, "archivo", nombre } };
    }

    /// <summary>Configuración de la compañía (contractual, operativa y correos del aviso).</summary>
    public static async Task SembrarConfiguracionAsync(
        ApiDePrueba api, Guid empresa, CancellationToken ct, int contractual = 500, int? operativo = null, string[]? departamentos = null, string[]? correos = null)
    {
        await using var db = api.Contexto(empresa);
        db.Add(new ConfiguracionEmpresa
        {
            TenantId = empresa,
            MaxRegistrosLoteContractual = contractual,
            MaxRegistrosLoteOperativo = operativo,
            DepartamentosHabilitados = departamentos,
            CorreosAviso = correos ?? [],
            ActualizadoEn = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Trabajos de correo de la outbox (todos los de la base), en orden.</summary>
    public static async Task<List<OutboxMessage>> CorreosAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var db = api.Contexto(null);
        return await db.Set<OutboxMessage>().AsNoTracking().Where(m => m.RoutingKey == TipoCorreo).OrderBy(m => m.OccurredAt).ToListAsync(ct);
    }

    /// <summary>El <c>data</c> del sobre de un mensaje de la outbox.</summary>
    public static JsonElement Datos(OutboxMessage mensaje) => EventEnvelope.FromJson(Encoding.UTF8.GetBytes(mensaje.Payload)).Data.Clone();

    public static async Task<List<Notificacion>> NotificacionesAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<Notificacion>().AsNoTracking().OrderBy(n => n.CreadaEn).ToListAsync(ct);
    }

    public static async Task<List<LiquidacionItem>> ItemsAsync(ApiDePrueba api, Guid empresa, Guid lote, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<LiquidacionItem>().AsNoTracking().Where(i => i.LiquidacionId == lote).OrderBy(i => i.Fila).ThenBy(i => i.Placa).ToListAsync(ct);
    }

    public static async Task<int> LotesAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<Liquidacion>().CountAsync(ct);
    }
}
