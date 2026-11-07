using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Marca de la web de persona natural (HU #14401, Feature #14400, Épica #14297): <c>/api/v1/admin/plataforma/persona-natural/marca-web</c>.
/// Mismo modelo y mismas reglas que la marca de un organismo (borrador, vista previa, publicar, retirar, contraste 4,5:1), con ámbito
/// web: no hay organismo y no se cruza con ninguna marca de organismo. Solo SuperAdmin (hereda la policy del grupo). Sin banner: los
/// de la web siguen en Contenido. Errores <c>{ code, message }</c>.
/// </summary>
public static partial class AdminPlataformaPersonaNaturalEndpoints
{
    public const string RutaMarcaWeb = BasePath + "/marca-web";

    private static void MapMarcaWeb(RouteGroupBuilder group)
    {
        var marca = group.MapGroup("/marca-web");

        marca.MapGet(string.Empty, async (PortalMarcaAdminHandler h, CancellationToken ct) =>
                Results.Ok(ConRutasWeb(await h.ObtenerAsync(PortalMarcaAmbito.Web, ct).ConfigureAwait(false))))
            .WithName("AdminPnMarcaWebGet").WithSummary("Marca de la web: borrador, publicado y versión")
            .Produces<PortalMarcaDto>(StatusCodes.Status200OK);

        marca.MapPut(string.Empty, async ([FromBody] GuardarPortalMarcaRequest? body, PortalMarcaAdminHandler h, HttpContext http, CancellationToken ct) =>
            {
                if (body is null)
                    return ProblemaMarca(400, PortalMarcaErrores.Datos, "Falta el cuerpo de la solicitud.");
                if (Actor(http) is not { } actor)
                    return SinUsuario();
                return ResultadoMarca(await h.GuardarAsync(PortalMarcaAmbito.Web, body, actor, ct).ConfigureAwait(false));
            })
            .WithName("AdminPnMarcaWebPut").WithSummary("Guarda el borrador (nombre y colores) con control de versión")
            .Produces<PortalMarcaDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status409Conflict);

        marca.MapPost("/publish", async ([FromBody] VersionPortalMarcaRequest? body, PortalMarcaAdminHandler h, HttpContext http, CancellationToken ct) =>
            {
                if (body is null)
                    return ProblemaMarca(400, PortalMarcaErrores.Datos, "Falta la versión (rowVersion) que leyó el editor.");
                if (Actor(http) is not { } actor)
                    return SinUsuario();
                return ResultadoMarca(await h.PublicarAsync(PortalMarcaAmbito.Web, body.RowVersion, actor, ct).ConfigureAwait(false));
            })
            .WithName("AdminPnMarcaWebPublish").WithSummary("Publica el borrador: exige nombre, colores y contraste 4,5:1")
            .Produces<PortalMarcaDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status409Conflict);

        marca.MapPost("/retire", async ([FromBody] VersionPortalMarcaRequest? body, PortalMarcaAdminHandler h, HttpContext http, CancellationToken ct) =>
            {
                if (body is null)
                    return ProblemaMarca(400, PortalMarcaErrores.Datos, "Falta la versión (rowVersion) que leyó el editor.");
                if (Actor(http) is not { } actor)
                    return SinUsuario();
                return ResultadoMarca(await h.RetirarAsync(PortalMarcaAmbito.Web, body.RowVersion, actor, ct).ConfigureAwait(false));
            })
            .WithName("AdminPnMarcaWebRetire").WithSummary("Retira la marca publicada y conserva el borrador")
            .Produces<PortalMarcaDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status409Conflict);

        marca.MapPost("/logo", async (IFormFile? file, PortalMarcaImagenHandler h, HttpContext http, CancellationToken ct) =>
            {
                if (file is null)
                    return ProblemaMarca(400, PortalMarcaErrores.ArchivoInvalido, "Falta el archivo del logo (campo «file»).");
                if (Actor(http) is not { } actor)
                    return SinUsuario();
                var stream = file.OpenReadStream();
                await using (stream.ConfigureAwait(false))
                    return ResultadoMarca(await h.SubirAsync(PortalMarcaAmbito.Web, PortalMarcaImagen.KindLogo, file.FileName, stream, actor, ct).ConfigureAwait(false));
            })
            .WithName("AdminPnMarcaWebLogoPost").DisableAntiforgery().WithSummary("Carga el logo (PNG, JPEG o WebP, hasta 512 KB)")
            .Produces<PortalMarcaDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest);

        marca.MapDelete("/logo", async (PortalMarcaImagenHandler h, HttpContext http, CancellationToken ct) =>
            {
                if (Actor(http) is not { } actor)
                    return SinUsuario();
                return Results.Ok(ConRutasWeb(await h.QuitarAsync(PortalMarcaAmbito.Web, PortalMarcaImagen.KindLogo, actor, ct).ConfigureAwait(false)));
            })
            .WithName("AdminPnMarcaWebLogoDelete").WithSummary("Quita el logo del borrador")
            .Produces<PortalMarcaDto>(StatusCodes.Status200OK);

        marca.MapGet("/imagenes/{id:guid}", async (Guid id, PortalMarcaImagenHandler h, HttpContext http, CancellationToken ct) =>
            {
                // Solo imágenes de la marca web: las de un organismo se piden por su propia ruta.
                if (await h.BuscarAsync(id, ct).ConfigureAwait(false) is not { } imagen || imagen.TransitOfficeId != PortalMarcaAmbito.Web
                    || await h.AbrirAsync(imagen, ct).ConfigureAwait(false) is not { } contenido)
                    return ProblemaMarca(404, PortalMarcaErrores.NoEncontrada, "La imagen no existe.");

                http.Response.Headers.CacheControl = "private, no-cache";
                http.Response.Headers.ETag = $"\"{contenido.Sha256}\"";
                return Results.Stream(contenido.Contenido, contenido.ContentType);
            })
            .WithName("AdminPnMarcaWebImagen").WithSummary("Imagen de la marca web, también de borrador (vista previa)")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>El manejador compartido arma las URL del organismo; aquí se llevan a la ruta de la web y se omite el banner.</summary>
    private static PortalMarcaDto ConRutasWeb(PortalMarcaDto m) => m with
    {
        LogoUrl = m.Draft.LogoId is { } logo ? $"{RutaMarcaWeb}/imagenes/{logo}" : null,
        BannerUrl = null,
    };

    private static IResult ResultadoMarca(PortalMarcaResultado<PortalMarcaDto> r) =>
        r.Error is { } e ? ProblemaMarca(e.Status, e.Code, e.Message) : Results.Ok(ConRutasWeb(r.Valor!));

    private static IResult ProblemaMarca(int estado, string code, string message) =>
        Results.Json(new { code, message }, statusCode: estado);
}
