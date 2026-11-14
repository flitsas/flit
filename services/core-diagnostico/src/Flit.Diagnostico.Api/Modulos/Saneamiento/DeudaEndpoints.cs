using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Saneamiento;

/// <summary>
/// Panel de deuda y sus exportaciones (plan §6.5, HU #14576 AC36-AC38), con la visibilidad de <c>AccesoLote</c> (otra compañía, PROPIOS
/// ajeno o vehículo de otro lote: 404 sin datos, antes que cualquier 409, SP-9):
/// <list type="bullet">
/// <item><c>GET /diagnosticos/{id}/vehiculos/{vid}/deuda</c> con <c>diagnostico.diagnosticos.ver</c>; sin caché (trae el documento completo).</item>
/// <item><c>GET …/deuda/pdf</c> y <c>GET …/deuda/excel</c> con <c>diagnostico.reportes.descargar</c>; adjuntos <c>deuda-{placa}.pdf|xlsx</c>.</item>
/// <item><c>POST /vehiculos/deuda/excel</c> con <c>{"vehiculo_ids": [...]}</c>: 1 a <see cref="MaximoVehiculos"/> ids distintos (los
/// repetidos no cuentan); vacía, más del tope, un elemento que no es uuid o un cuerpo que no es JSON: 400 <see cref="CodigoListaInvalida"/>.
/// Los no visibles o sin terminar se omiten sin revelarse.</item>
/// </list>
/// Toda descarga lleva las cabeceras de <see cref="DescargaSeguraFiltro"/> (SP-18). Sin logs con placa ni documento.
/// </summary>
internal static class DeudaEndpoints
{
    public const string RutaPanel = DiagnosticosEndpoints.Ruta + "/{id:guid}/vehiculos/{vid:guid}/deuda";
    public const string RutaPdf = RutaPanel + "/pdf";
    public const string RutaExcel = RutaPanel + "/excel";
    public const string RutaExcelVarios = "/vehiculos/deuda/excel";

    public const string CodigoListaInvalida = "LISTA_INVALIDA";
    public const string CampoLista = "vehiculo_ids";
    public const int MaximoVehiculos = 200;

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet(RutaPanel, PanelAsync).RequirePermiso(Permisos.DiagnosticosVer).SinCacheEnRespuesta().WithName("ObtenerDeudaVehiculo");
        api.MapGet(RutaPdf, PdfAsync).RequirePermiso(Permisos.ReportesDescargar).DescargaSegura().WithName("DescargarPdfDeuda");
        api.MapGet(RutaExcel, ExcelAsync).RequirePermiso(Permisos.ReportesDescargar).DescargaSegura().WithName("DescargarExcelDeuda");
        api.MapPost(RutaExcelVarios, ExcelVariosAsync).RequirePermiso(Permisos.ReportesDescargar).DescargaSegura().WithName("DescargarExcelDeudaVarios");
    }

    private static async Task<IResult> PanelAsync(Guid id, Guid vid, ConsultaDeuda consulta, CancellationToken ct) =>
        Results.Ok(await consulta.ObtenerAsync(id, vid, ct).ConfigureAwait(false));

    private static async Task<IResult> PdfAsync(Guid id, Guid vid, ConsultaDeuda consulta, TimeProvider reloj, CancellationToken ct)
    {
        var panel = await consulta.ObtenerAsync(id, vid, ct).ConfigureAwait(false);
        return Results.File(PdfDeuda.Generar(panel, reloj.GetUtcNow()), PdfDeuda.ContentType, PdfDeuda.NombreArchivo(panel.Vehiculo.Placa));
    }

    private static async Task<IResult> ExcelAsync(Guid id, Guid vid, ConsultaDeuda consulta, CancellationToken ct)
    {
        var panel = await consulta.ObtenerAsync(id, vid, ct).ConfigureAwait(false);
        return Results.File(ExcelDeuda.Generar([panel], conPlaca: false), ExcelDeuda.ContentType, ExcelDeuda.NombreArchivo(panel.Vehiculo.Placa));
    }

    private static async Task<IResult> ExcelVariosAsync(HttpRequest peticion, ConsultaDeuda consulta, CancellationToken ct)
    {
        var ids = await LeerIdsAsync(peticion, ct).ConfigureAwait(false);
        var resultado = await consulta.VariosAsync(ids, ct).ConfigureAwait(false);
        return Results.File(ExcelDeuda.Generar(resultado.Paneles, conPlaca: true, resultado.Omitidos), ExcelDeuda.ContentType, ExcelDeuda.NombreArchivoVarios);
    }

    /// <summary>Los ids distintos del cuerpo, en el orden en que llegan; 400 <see cref="CodigoListaInvalida"/> si la lista no es válida.</summary>
    internal static async Task<IReadOnlyList<Guid>> LeerIdsAsync(HttpRequest peticion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peticion);
        JsonDocument documento;
        try
        {
            documento = await JsonDocument.ParseAsync(peticion.Body, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw Invalida();
        }

        using (documento)
        {
            if (documento.RootElement.ValueKind != JsonValueKind.Object
                || !documento.RootElement.TryGetProperty(CampoLista, out var lista)
                || lista.ValueKind != JsonValueKind.Array)
                throw Invalida();
            var ids = new List<Guid>();
            var vistos = new HashSet<Guid>();
            foreach (var item in lista.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || !Guid.TryParse(item.GetString(), out var id))
                    throw Invalida();
                if (vistos.Add(id))
                    ids.Add(id);
            }

            if (ids.Count is 0 or > MaximoVehiculos)
                throw Invalida();
            return ids;
        }
    }

    private static ReglaException Invalida() =>
        new(CodigoListaInvalida, $"vehiculo_ids es una lista de 1 a {MaximoVehiculos} ids de vehículo distintos.", StatusCodes.Status400BadRequest) { Campo = CampoLista };
}
