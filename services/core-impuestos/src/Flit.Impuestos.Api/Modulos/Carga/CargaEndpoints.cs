using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Liquidaciones;
using Flit.Impuestos.Api.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Impuestos.Api.Modulos.Carga;

/// <summary>
/// Rutas de la carga masiva de M12 sobre el grupo <c>/liquidaciones</c> (plan §5.1; impuestos.v1.yaml 1.1.0, delta 2), todas con
/// <c>impuestos.liquidaciones.crear</c> y la regla de <c>?empresa</c> de <see cref="LiquidacionesEndpoints.EmpresaDeEscrituraAsync"/>
/// (el Super Admin la necesita; quien no lo es y la envía recibe 403):
/// <list type="bullet">
///   <item><c>GET /plantilla</c>: el .xlsx de <see cref="PlantillaCarga"/>.</item>
///   <item><c>POST /masiva/analizar</c> (multipart <c>archivo</c>): <c>AnalisisCarga</c> con el tope de la compañía.</item>
///   <item><c>POST /masiva</c> (multipart <c>archivo</c>): 201 con la cabecera del lote nuevo.</item>
/// </list>
/// El archivo se lee del formulario con tope <see cref="AnalizadorCarga.MaximoBytes"/> (413 <c>ARCHIVO_DEMASIADO_GRANDE</c>); una
/// petición que no es multipart o no trae la parte <c>archivo</c> responde 415 <c>FORMATO_NO_SOPORTADO</c>.
/// </summary>
/// <example>
/// <code>
/// CargaEndpoints.Map(LiquidacionesEndpoints.Map(api));
/// </code>
/// </example>
internal static class CargaEndpoints
{
    public const string CampoArchivo = "archivo";

    /// <summary>Margen del multipart sobre el tope del archivo (cabeceras y límites de la parte).</summary>
    private const int MargenMultipart = 64 * 1024;

    public static RouteGroupBuilder Map(RouteGroupBuilder liquidaciones)
    {
        ArgumentNullException.ThrowIfNull(liquidaciones);
        liquidaciones.MapGet("/plantilla", PlantillaAsync).RequirePermiso(Permisos.LiquidacionesCrear).WithName("DescargarPlantillaMasiva");
        liquidaciones.MapPost("/masiva/analizar", AnalizarAsync).RequirePermiso(Permisos.LiquidacionesCrear)
            .WithMetadata(new RequestSizeLimitAttribute(AnalizadorCarga.MaximoBytes + MargenMultipart)).WithName("AnalizarCargaMasiva");
        liquidaciones.MapPost("/masiva", CrearAsync).RequirePermiso(Permisos.LiquidacionesCrear)
            .WithMetadata(new RequestSizeLimitAttribute(AnalizadorCarga.MaximoBytes + MargenMultipart)).WithName("CrearLiquidacionMasiva");
        return liquidaciones;
    }

    private static async Task<IResult> PlantillaAsync(
        HttpContext http, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito, CancellationToken ct)
    {
        await LiquidacionesEndpoints.EmpresaDeEscrituraAsync(http.Request.Query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, ct).ConfigureAwait(false);
        return Results.File(PlantillaCarga.Generar(), PlantillaCarga.ContentType, PlantillaCarga.NombreArchivo);
    }

    private static async Task<IResult> AnalizarAsync(
        HttpContext http, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito, CrearLiquidacionMasiva masiva,
        CancellationToken ct)
    {
        var (empresa, _) = await LiquidacionesEndpoints.EmpresaDeEscrituraAsync(
            http.Request.Query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, ct).ConfigureAwait(false);
        var archivo = await ArchivoAsync(http.Request, ct).ConfigureAwait(false);
        return Results.Ok(await masiva.AnalizarAsync(empresa, archivo, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> CrearAsync(
        HttpContext http, UsuarioActual usuario, ServicioConfiguracion configuracion, AmbitoDeLaEmpresa ambito, CrearLiquidacionMasiva masiva,
        CancellationToken ct)
    {
        var (empresa, referencia) = await LiquidacionesEndpoints.EmpresaDeEscrituraAsync(
            http.Request.Query[EmpresaExplicita.Parametro], usuario, configuracion, ambito, ct).ConfigureAwait(false);
        var archivo = await ArchivoAsync(http.Request, ct).ConfigureAwait(false);
        var creada = await masiva.CrearAsync(empresa, archivo, usuario.Id, usuario.Nombre, referencia, ct).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}{LiquidacionesEndpoints.Ruta}/{creada.Id}", creada);
    }

    /// <summary>La parte <c>archivo</c> del multipart en memoria; 413 si pasa del tope y 415 si no hay multipart o falta la parte.</summary>
    internal static async Task<ArchivoCarga> ArchivoAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType)
            throw ErroresCarga.Formato();
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct).ConfigureAwait(false);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            throw ErroresCarga.DemasiadoGrande();
        }
        catch (InvalidDataException)
        {
            throw ErroresCarga.Formato();
        }

        if (form.Files.GetFile(CampoArchivo) is not { } archivo)
            throw ErroresCarga.Formato();
        if (archivo.Length > AnalizadorCarga.MaximoBytes)
            throw ErroresCarga.DemasiadoGrande();
        using var memoria = new MemoryStream((int)archivo.Length);
        await archivo.CopyToAsync(memoria, ct).ConfigureAwait(false);
        return new ArchivoCarga(archivo.FileName, memoria.ToArray());
    }
}
