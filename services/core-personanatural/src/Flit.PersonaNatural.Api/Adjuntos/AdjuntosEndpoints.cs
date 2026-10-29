using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Adjuntos;

/// <summary>
/// Endpoints de archivos del trámite (HU #14048): <c>POST /solicitudes/{id}/adjuntos</c> (multipart: <c>archivo</c> y
/// <c>tipo</c>), <c>DELETE /solicitudes/{id}/adjuntos/{adjuntoId}</c> y <c>POST /solicitudes/{id}/pago/comprobante</c>.
/// Todos exigen la sesión de ESA solicitud. Los errores son <c>problem+json</c> con <c>code</c> estable.
/// </summary>
public static class AdjuntosEndpoints
{
    public const string CampoArchivo = "archivo";
    public const string CampoTipo = "tipo";

    /// <summary>El multipart lleva algo de sobrecarga sobre el archivo: el tope del cuerpo es un poco mayor que el del archivo.</summary>
    private const long SobrecargaMultipart = 1024 * 1024;

    public static IServiceCollection AddAdjuntosPortal(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<AdjuntosOptions>(configuration.GetSection(AdjuntosOptions.SectionName));
        services.Configure<FileManagerOptions>(o => FileManagerOptions.Configurar(o, configuration));
        services.TryAddSingleton<IAnalizadorDeArchivos, AnalizadorDeArchivosNulo>();
        services.AddScoped<AdjuntosService>();
        services.AddScoped<ILimpiadorDeSolicitud, LimpiadorDeAdjuntos>();

        var baseUrl = configuration["FileManager:BaseUrl"] ?? Environment.GetEnvironmentVariable("FILE_MANAGER_BASE_URL");
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            services.AddHttpClient<IAlmacenAdjuntos, AlmacenFileManager>((sp, c) =>
            {
                var o = sp.GetRequiredService<IOptions<FileManagerOptions>>().Value;
                c.BaseAddress = new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/");
                c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds > 0 ? o.TimeoutSeconds : 30);
            });
        }
        else
        {
            services.TryAddSingleton<IAlmacenAdjuntos, AlmacenAdjuntosNoConfigurado>();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapAdjuntosEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var conSesion = app.MapGroup(SesionEndpoints.Base).AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp).MapGroup("/solicitudes/{id:guid}");
        conSesion.AddEndpointFilter(Radicacion.SesionTrasRadicar.Filtro);

        conSesion.MapPost("/adjuntos", (Guid id, HttpContext http, AdjuntosService s, IOptions<AdjuntosOptions> o, CancellationToken ct) =>
            Subir(http, o.Value, (tipo, nombre, bytes) => s.CargarAsync(id, tipo, nombre, bytes, ct), ct));

        conSesion.MapPost("/pago/comprobante", (Guid id, HttpContext http, AdjuntosService s, IOptions<AdjuntosOptions> o, CancellationToken ct) =>
            Subir(http, o.Value, (_, nombre, bytes) => s.CargarAsync(id, SolicitudesService.TipoComprobantePago, nombre, bytes, ct), ct));

        conSesion.MapDelete("/adjuntos/{adjuntoId:guid}", (Guid id, Guid adjuntoId, AdjuntosService s, CancellationToken ct) =>
            SolicitudesEndpoints.Atender(async () =>
            {
                await s.EliminarAsync(id, adjuntoId, ct).ConfigureAwait(false);
                return Results.NoContent();
            }));

        return app;
    }

    /// <summary>Lee el multipart (un archivo y, si aplica, su tipo) y lo entrega ya en memoria; la validación es del servicio.</summary>
    private static Task<IResult> Subir(HttpContext http, AdjuntosOptions opciones, Func<string?, string?, byte[], Task<AdjuntoDto>> cargar, CancellationToken ct) =>
        SolicitudesEndpoints.Atender(async () =>
        {
            if (!http.Request.HasFormContentType)
                return SesionEndpoints.Problema(StatusCodes.Status415UnsupportedMediaType, "Archivo no permitido", "Envíe el archivo como multipart/form-data.", AdjuntosService.CodigoNoPermitido);

            // El tope del cuerpo se fija antes de leerlo: un archivo enorme no llega a la memoria del servicio.
            var limite = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limite is { IsReadOnly: false })
                limite.MaxRequestBodySize = opciones.MaxBytes + SobrecargaMultipart;

            IFormCollection form;
            try
            {
                form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } or InvalidDataException or IOException)
            {
                return SesionEndpoints.Problema(StatusCodes.Status413PayloadTooLarge, "Archivo no permitido", $"El archivo supera los {opciones.MaxBytes / (1024 * 1024)} MB permitidos.", AdjuntosService.CodigoNoPermitido);
            }

            var archivo = form.Files.GetFile(CampoArchivo);
            if (archivo is null)
                return SesionEndpoints.Problema(StatusCodes.Status400BadRequest, "Datos inválidos", "Envíe el archivo en el campo 'archivo'.", "datos_invalidos");
            if (archivo.Length > opciones.MaxBytes)
                return SesionEndpoints.Problema(StatusCodes.Status413PayloadTooLarge, "Archivo no permitido", $"El archivo supera los {opciones.MaxBytes / (1024 * 1024)} MB permitidos.", AdjuntosService.CodigoNoPermitido);

            await using var entrada = archivo.OpenReadStream();
            using var memoria = new MemoryStream((int)archivo.Length);
            await entrada.CopyToAsync(memoria, ct).ConfigureAwait(false);
            var adjunto = await cargar(form[CampoTipo].ToString(), archivo.FileName, memoria.ToArray()).ConfigureAwait(false);
            return Results.Json(adjunto, statusCode: StatusCodes.Status201Created);
        });
}
