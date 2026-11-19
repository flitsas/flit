using System.Text.RegularExpressions;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Banners;
using Flit.Admin.Application.Banners.GetBannerImage;
using Flit.Admin.Application.Banners.Ports;
using Flit.Admin.Domain.Plataforma.PersonaNatural;

namespace Flit.Admin.Application.Plataforma.PersonaNatural;

/// <summary>Códigos de error estables de la administración del contenido del portal.</summary>
public static class ContenidoPnErrores
{
    public const string TextoInvalido = "texto_invalido";
    public const string HtmlNoPermitido = "html_no_permitido";
    public const string BannerInvalido = "banner_invalido";
    public const string ImagenInvalida = "imagen_invalida";
    public const string ImagenObligatoria = "imagen_obligatoria";
    public const string EnlaceInvalido = "enlace_invalido";
    public const string CodigoEnlaceInvalido = "codigo_enlace_invalido";
    public const string NoEncontrado = "no_encontrado";
    public const string OrdenInvalido = "orden_invalido";
    public const string SinCambios = "sin_cambios_por_publicar";
}

/// <summary>Resultado de una operación de contenido: el valor, o un código de error estable con su mensaje claro.</summary>
public sealed record ResultadoContenidoPn<T>(T? Valor, string? Error, string? Mensaje)
{
    public static ResultadoContenidoPn<T> Ok(T valor) => new(valor, null, null);

    public static ResultadoContenidoPn<T> Falla(string error, string mensaje) => new(default, error, mensaje);
}

/// <summary>Imagen subida de un banner (el flujo lo abre el endpoint y lo cierra quien lo creó).</summary>
public sealed record ImagenSubidaPn(string NombreArchivo, string ContentType, long Bytes, Stream Contenido);

/// <summary>Datos de un banner a crear o a editar. En la edición, <c>Imagen</c> nula conserva la actual.</summary>
public sealed record DatosBannerPn(
    string? Titulo, string? TextoAlternativo, string? EnlaceUrl, int? Orden, DateTimeOffset? VigenteDesde, DateTimeOffset? VigenteHasta, bool? Activo,
    ImagenSubidaPn? Imagen);

/// <summary>Contenido administrable de texto: reglas de forma (texto plano con límite de longitud, sin HTML) y de enlaces.</summary>
public static partial class ContenidoPnReglas
{
    public const int PreguntaMax = 300;
    public const int RespuestaMax = 2000;
    public const int TextoAlternativoMax = 250;
    public const int EnlaceMax = 2048;

    [GeneratedRegex(@"<[A-Za-z/!?]", RegexOptions.CultureInvariant)]
    private static partial Regex EtiquetaHtml();

    public static bool TieneHtml(string texto) => EtiquetaHtml().IsMatch(texto);

    /// <summary>Texto obligatorio: ni vacío ni con HTML ni más largo que el límite. Devuelve el mensaje de error o <c>null</c>.</summary>
    public static (string Error, string Mensaje)? ValidarTexto(string? texto, string nombre, int max)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return (ContenidoPnErrores.TextoInvalido, $"{nombre} es obligatoria.");
        if (texto.Trim().Length > max)
            return (ContenidoPnErrores.TextoInvalido, $"{nombre} admite hasta {max} caracteres.");
        if (TieneHtml(texto))
            return (ContenidoPnErrores.HtmlNoPermitido, $"{nombre} es texto simple: no se acepta HTML.");
        return null;
    }

    /// <summary>
    /// Enlace: URL absoluta http/https; el contacto de ventas admite además <c>mailto:</c> y <c>tel:</c>. No se inventa contenido
    /// legal: un enlace vacío se guarda como «sin documento oficial».
    /// </summary>
    public static (string Error, string Mensaje)? ValidarEnlace(string codigo, string? url)
    {
        if (url is null)
            return null;
        if (url.Length > EnlaceMax || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (ContenidoPnErrores.EnlaceInvalido, "El enlace debe ser una URL válida.");
        var permitido = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps
            || (codigo == CodigosEnlacePn.ContactoVentas && (uri.Scheme == Uri.UriSchemeMailto || uri.Scheme == "tel"));
        return permitido ? null : (ContenidoPnErrores.EnlaceInvalido, "El enlace debe empezar por http:// o https://.");
    }
}

/// <summary>
/// HU #14073 (Feature J, Épica #14021) — administración del contenido del portal: preguntas frecuentes, banners, enlaces y
/// publicación. Todo lo que se edita es el BORRADOR; el portal solo ve lo publicado (AC4). Cada cambio queda auditado con
/// actor y valores anterior y nuevo.
/// </summary>
public sealed class ContenidoPnHandler(IPersonaNaturalContenidoRepository contenido, IBannerImageStorage imagenes, IAdminAuditWriter audit)
{
    private const string EntidadFaq = "persona_natural_faqs";
    private const string EntidadBanner = "persona_natural_banners";
    private const string EntidadEnlace = "persona_natural_links";
    private const string EntidadVersion = "persona_natural_content_versions";

    // ── Preguntas frecuentes ──────────────────────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<FaqPn>> ListarFaqsAsync(CancellationToken ct = default) => contenido.ListarFaqsAsync(ct);

    public async Task<ResultadoContenidoPn<FaqPn>> CrearFaqAsync(string? pregunta, string? respuesta, bool? activa, Guid actor, CancellationToken ct = default)
    {
        if (ValidarFaq(pregunta, respuesta) is { } invalido)
            return ResultadoContenidoPn<FaqPn>.Falla(invalido.Error, invalido.Mensaje);
        var creada = await contenido.CrearFaqAsync(pregunta!.Trim(), respuesta!.Trim(), activa ?? true, actor, ct).ConfigureAwait(false);
        await PersonaNaturalAuditoria.EscribirAsync(audit, EntidadFaq, AuditVocabulary.Operations.Create, actor, creada.Id, null, ResumenFaq(creada), ct).ConfigureAwait(false);
        return ResultadoContenidoPn<FaqPn>.Ok(creada);
    }

    public async Task<ResultadoContenidoPn<FaqPn>> ActualizarFaqAsync(
        Guid id, string? pregunta, string? respuesta, bool? activa, Guid actor, CancellationToken ct = default)
    {
        if (ValidarFaq(pregunta, respuesta) is { } invalido)
            return ResultadoContenidoPn<FaqPn>.Falla(invalido.Error, invalido.Mensaje);
        var antes = await contenido.ObtenerFaqAsync(id, ct).ConfigureAwait(false);
        if (antes is null)
            return ResultadoContenidoPn<FaqPn>.Falla(ContenidoPnErrores.NoEncontrado, "La pregunta no existe.");
        var despues = await contenido.ActualizarFaqAsync(id, pregunta!.Trim(), respuesta!.Trim(), activa ?? antes.Activa, actor, ct).ConfigureAwait(false);
        if (despues is null)
            return ResultadoContenidoPn<FaqPn>.Falla(ContenidoPnErrores.NoEncontrado, "La pregunta no existe.");
        await PersonaNaturalAuditoria.EscribirAsync(audit, EntidadFaq, AuditVocabulary.Operations.Update, actor, id, ResumenFaq(antes), ResumenFaq(despues), ct).ConfigureAwait(false);
        return ResultadoContenidoPn<FaqPn>.Ok(despues);
    }

    /// <summary>Activa o desactiva una pregunta (el contenido no se borra: solo deja de publicarse).</summary>
    public async Task<ResultadoContenidoPn<FaqPn>> EstablecerFaqActivaAsync(Guid id, bool activa, Guid actor, CancellationToken ct = default)
    {
        var antes = await contenido.ObtenerFaqAsync(id, ct).ConfigureAwait(false);
        if (antes is null)
            return ResultadoContenidoPn<FaqPn>.Falla(ContenidoPnErrores.NoEncontrado, "La pregunta no existe.");
        if (antes.Activa == activa)
            return ResultadoContenidoPn<FaqPn>.Ok(antes);
        var despues = await contenido.ActualizarFaqAsync(id, antes.Pregunta, antes.Respuesta, activa, actor, ct).ConfigureAwait(false);
        await PersonaNaturalAuditoria.EscribirAsync(
            audit, EntidadFaq, activa ? AuditVocabulary.Operations.Activate : AuditVocabulary.Operations.Deactivate, actor, id,
            ResumenFaq(antes), ResumenFaq(despues!), ct).ConfigureAwait(false);
        return ResultadoContenidoPn<FaqPn>.Ok(despues!);
    }

    public async Task<ResultadoContenidoPn<bool>> ReordenarFaqsAsync(IReadOnlyList<Guid>? ids, Guid actor, CancellationToken ct = default)
    {
        if (ids is not { Count: > 0 } || ids.Distinct().Count() != ids.Count || !await contenido.ReordenarFaqsAsync(ids, actor, ct).ConfigureAwait(false))
            return ResultadoContenidoPn<bool>.Falla(ContenidoPnErrores.OrdenInvalido, "La lista debe traer todas las preguntas, cada una una sola vez.");
        await PersonaNaturalAuditoria.EscribirAsync(
            audit, EntidadFaq, AuditVocabulary.Operations.Update, actor, null, null,
            PersonaNaturalAuditoria.Json(("orden", string.Join(',', ids))), ct).ConfigureAwait(false);
        return ResultadoContenidoPn<bool>.Ok(true);
    }

    // ── Banners ───────────────────────────────────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<BannerPn>> ListarBannersAsync(CancellationToken ct = default) => contenido.ListarBannersAsync(ct);

    public async Task<ResultadoContenidoPn<BannerPn>> CrearBannerAsync(DatosBannerPn datos, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (datos.Imagen is null)
            return ResultadoContenidoPn<BannerPn>.Falla(ContenidoPnErrores.ImagenObligatoria, "Suba la imagen del banner (JPG, PNG o WEBP de hasta 2 MB).");
        if (ValidarBanner(datos) is { } invalido)
            return ResultadoContenidoPn<BannerPn>.Falla(invalido.Error, invalido.Mensaje);
        var imagen = await GuardarImagenAsync(datos.Imagen, ct).ConfigureAwait(false);
        if (imagen.Error is not null)
            return ResultadoContenidoPn<BannerPn>.Falla(imagen.Error, imagen.Mensaje!);

        var (path, sha, tipo, bytes) = imagen.Valor;
        var creado = await contenido.CrearBannerAsync(
            new NuevoBannerPn(
                datos.Titulo!.Trim(), datos.TextoAlternativo!.Trim(), Limpio(datos.EnlaceUrl), datos.Orden ?? 0, datos.VigenteDesde, datos.VigenteHasta,
                datos.Activo ?? true, path, sha, tipo, bytes),
            actor, ct).ConfigureAwait(false);
        await PersonaNaturalAuditoria.EscribirAsync(audit, EntidadBanner, AuditVocabulary.Operations.Create, actor, creado.Id, null, ResumenBanner(creado), ct).ConfigureAwait(false);
        return ResultadoContenidoPn<BannerPn>.Ok(creado);
    }

    public async Task<ResultadoContenidoPn<BannerPn>> ActualizarBannerAsync(Guid id, DatosBannerPn datos, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var antes = await contenido.ObtenerBannerAsync(id, ct).ConfigureAwait(false);
        if (antes is null)
            return ResultadoContenidoPn<BannerPn>.Falla(ContenidoPnErrores.NoEncontrado, "El banner no existe.");
        if (ValidarBanner(datos) is { } invalido)
            return ResultadoContenidoPn<BannerPn>.Falla(invalido.Error, invalido.Mensaje);

        string? path = null, sha = null, tipo = null;
        long? bytes = null;
        if (datos.Imagen is not null)
        {
            var imagen = await GuardarImagenAsync(datos.Imagen, ct).ConfigureAwait(false);
            if (imagen.Error is not null)
                return ResultadoContenidoPn<BannerPn>.Falla(imagen.Error, imagen.Mensaje!);
            (path, sha, tipo, var b) = imagen.Valor;
            bytes = b;
        }

        var despues = await contenido.ActualizarBannerAsync(
            id,
            new CambiosBannerPn(
                datos.Titulo!.Trim(), datos.TextoAlternativo!.Trim(), Limpio(datos.EnlaceUrl), datos.Orden ?? antes.Orden, datos.VigenteDesde, datos.VigenteHasta,
                datos.Activo ?? antes.Activo, path, sha, tipo, bytes),
            actor, ct).ConfigureAwait(false);
        if (despues is null)
            return ResultadoContenidoPn<BannerPn>.Falla(ContenidoPnErrores.NoEncontrado, "El banner no existe.");
        await PersonaNaturalAuditoria.EscribirAsync(audit, EntidadBanner, AuditVocabulary.Operations.Update, actor, id, ResumenBanner(antes), ResumenBanner(despues), ct).ConfigureAwait(false);
        return ResultadoContenidoPn<BannerPn>.Ok(despues);
    }

    public async Task<ResultadoContenidoPn<BannerPn>> EstablecerBannerActivoAsync(Guid id, bool activo, Guid actor, CancellationToken ct = default)
    {
        var antes = await contenido.ObtenerBannerAsync(id, ct).ConfigureAwait(false);
        if (antes is null)
            return ResultadoContenidoPn<BannerPn>.Falla(ContenidoPnErrores.NoEncontrado, "El banner no existe.");
        if (antes.Activo == activo)
            return ResultadoContenidoPn<BannerPn>.Ok(antes);
        var despues = await contenido.ActualizarBannerAsync(
            id,
            new CambiosBannerPn(antes.Titulo, antes.TextoAlternativo, antes.EnlaceUrl, antes.Orden, antes.VigenteDesde, antes.VigenteHasta, activo, null, null, null, null),
            actor, ct).ConfigureAwait(false);
        await PersonaNaturalAuditoria.EscribirAsync(
            audit, EntidadBanner, activo ? AuditVocabulary.Operations.Activate : AuditVocabulary.Operations.Deactivate, actor, id,
            ResumenBanner(antes), ResumenBanner(despues!), ct).ConfigureAwait(false);
        return ResultadoContenidoPn<BannerPn>.Ok(despues!);
    }

    public async Task<ResultadoContenidoPn<bool>> ReordenarBannersAsync(IReadOnlyList<Guid>? ids, Guid actor, CancellationToken ct = default)
    {
        if (ids is not { Count: > 0 } || ids.Distinct().Count() != ids.Count || !await contenido.ReordenarBannersAsync(ids, actor, ct).ConfigureAwait(false))
            return ResultadoContenidoPn<bool>.Falla(ContenidoPnErrores.OrdenInvalido, "La lista debe traer todos los banners, cada uno una sola vez.");
        await PersonaNaturalAuditoria.EscribirAsync(
            audit, EntidadBanner, AuditVocabulary.Operations.Update, actor, null, null,
            PersonaNaturalAuditoria.Json(("orden", string.Join(',', ids))), ct).ConfigureAwait(false);
        return ResultadoContenidoPn<bool>.Ok(true);
    }

    /// <summary>Abre la imagen del banner de trabajo (vista previa del administrador). <c>null</c> si no existe o se perdió el archivo.</summary>
    public async Task<(Stream Contenido, string ContentType, string Sha256)?> AbrirImagenAsync(Guid id, CancellationToken ct = default)
    {
        var imagen = await contenido.ImagenDeTrabajoAsync(id, ct).ConfigureAwait(false);
        if (imagen is null)
            return null;
        var stream = await imagenes.OpenReadAsync(imagen.Path, ct).ConfigureAwait(false);
        return stream is null ? null : (stream, imagen.ContentType, imagen.Sha256);
    }

    // ── Enlaces ───────────────────────────────────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<EnlacePn>> ListarEnlacesAsync(CancellationToken ct = default) => contenido.ListarEnlacesAsync(ct);

    public async Task<ResultadoContenidoPn<EnlacePn>> EstablecerEnlaceAsync(string? codigo, string? url, Guid actor, CancellationToken ct = default)
    {
        var normalizado = codigo?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!CodigosEnlacePn.Todos.Contains(normalizado))
            return ResultadoContenidoPn<EnlacePn>.Falla(ContenidoPnErrores.CodigoEnlaceInvalido, $"Enlace desconocido: use {string.Join(", ", CodigosEnlacePn.Todos)}.");
        var limpio = Limpio(url);
        if (ContenidoPnReglas.ValidarEnlace(normalizado, limpio) is { } invalido)
            return ResultadoContenidoPn<EnlacePn>.Falla(invalido.Error, invalido.Mensaje);

        var antes = (await contenido.ListarEnlacesAsync(ct).ConfigureAwait(false)).First(e => e.Codigo == normalizado);
        if (string.Equals(antes.Url, limpio, StringComparison.Ordinal))
            return ResultadoContenidoPn<EnlacePn>.Ok(antes);
        var despues = await contenido.EstablecerEnlaceAsync(normalizado, limpio, actor, ct).ConfigureAwait(false);
        await PersonaNaturalAuditoria.EscribirAsync(
            audit, EntidadEnlace, AuditVocabulary.Operations.Update, actor, null,
            PersonaNaturalAuditoria.Json(("codigo", antes.Codigo), ("url", antes.Url)), PersonaNaturalAuditoria.Json(("codigo", despues.Codigo), ("url", despues.Url)), ct).ConfigureAwait(false);
        return ResultadoContenidoPn<EnlacePn>.Ok(despues);
    }

    // ── Borrador, vista previa y publicación ──────────────────────────────────────────────────────────────────────────

    /// <summary>El contenido tal como lo vería el portal si se publicara hoy (vista previa) y si difiere de lo ya publicado.</summary>
    public async Task<BorradorContenidoPn> BorradorAsync(CancellationToken ct = default)
    {
        var publicable = await contenido.ContenidoPublicableAsync(ct).ConfigureAwait(false);
        var ultima = await contenido.UltimaVersionAsync(ct).ConfigureAwait(false);
        return new BorradorContenidoPn(publicable, ultima?.Version, ContenidoPnSnapshot.Difiere(publicable, ultima?.Contenido));
    }

    /// <summary>Publica el borrador como versión nueva (fecha, usuario y versión anterior consultables). Sin cambios no publica.</summary>
    public async Task<ResultadoContenidoPn<VersionContenidoPn>> PublicarAsync(Guid actor, CancellationToken ct = default)
    {
        var borrador = await BorradorAsync(ct).ConfigureAwait(false);
        if (!borrador.HayCambiosSinPublicar)
            return ResultadoContenidoPn<VersionContenidoPn>.Falla(ContenidoPnErrores.SinCambios, "No hay cambios por publicar: el contenido ya es el publicado.");
        var version = await contenido.PublicarAsync(actor, ct).ConfigureAwait(false);
        await PersonaNaturalAuditoria.EscribirAsync(
            audit, EntidadVersion, AuditVocabulary.Operations.Create, actor, null,
            borrador.VersionPublicada is { } anterior ? PersonaNaturalAuditoria.Json(("version", anterior)) : null,
            PersonaNaturalAuditoria.Json(("version", version.Version), ("preguntas", version.Contenido.Preguntas.Count), ("banners", version.Contenido.Banners.Count)), ct)
            .ConfigureAwait(false);
        return ResultadoContenidoPn<VersionContenidoPn>.Ok(version);
    }

    public Task<IReadOnlyList<ResumenVersionContenidoPn>> ListarVersionesAsync(CancellationToken ct = default) => contenido.ListarVersionesAsync(ct);

    public Task<VersionContenidoPn?> ObtenerVersionAsync(int version, CancellationToken ct = default) => contenido.ObtenerVersionAsync(version, ct);

    // ── Soporte ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static (string Error, string Mensaje)? ValidarFaq(string? pregunta, string? respuesta) =>
        ContenidoPnReglas.ValidarTexto(pregunta, "La pregunta", ContenidoPnReglas.PreguntaMax)
        ?? ContenidoPnReglas.ValidarTexto(respuesta, "La respuesta", ContenidoPnReglas.RespuestaMax);

    private static (string Error, string Mensaje)? ValidarBanner(DatosBannerPn d)
    {
        if (ContenidoPnReglas.ValidarTexto(d.Titulo, "El título", BannerValidator.NameMaxLength) is { } titulo)
            return titulo;
        // Una imagen sin texto alternativo se rechaza.
        if (string.IsNullOrWhiteSpace(d.TextoAlternativo))
            return (ContenidoPnErrores.BannerInvalido, "El texto alternativo de la imagen es obligatorio.");
        if (ContenidoPnReglas.ValidarTexto(d.TextoAlternativo, "El texto alternativo", ContenidoPnReglas.TextoAlternativoMax) is { } alt)
            return alt;
        var payload = BannerValidator.ValidatePayload(d.Titulo!.Trim(), Limpio(d.EnlaceUrl), d.VigenteDesde, d.VigenteHasta);
        if (payload is not null)
            return (ContenidoPnErrores.BannerInvalido, payload);
        if (d.VigenteDesde is { } desde && d.VigenteHasta is { } hasta && hasta <= desde)
            return (ContenidoPnErrores.BannerInvalido, "El fin de la vigencia debe ser posterior al inicio.");
        return null;
    }

    /// <summary>JPG, PNG o WEBP de hasta 2 MB: se valida el tipo declarado, el tamaño y los primeros bytes (firma real del archivo).</summary>
    private async Task<(string? Error, string? Mensaje, (string Path, string Sha, string Tipo, long Bytes) Valor)> GuardarImagenAsync(
        ImagenSubidaPn imagen, CancellationToken ct)
    {
        var error = BannerValidator.ValidateImage(imagen.ContentType, imagen.Bytes);
        if (error is not null)
            return (ContenidoPnErrores.ImagenInvalida, error, default);

        var flujo = imagen.Contenido;
        MemoryStream? copia = null;
        try
        {
            if (!flujo.CanSeek)
            {
                copia = new MemoryStream();
                await flujo.CopyToAsync(copia, ct).ConfigureAwait(false);
                copia.Position = 0;
                flujo = copia;
            }

            var real = await ImageContentTypeSniffer.DetectAsync(flujo, ct).ConfigureAwait(false);
            if (!BannerValidator.AllowedImageMimeTypes.Contains(real, StringComparer.OrdinalIgnoreCase))
                return (ContenidoPnErrores.ImagenInvalida, "El archivo no es una imagen JPG, PNG o WEBP válida.", default);

            var guardada = await imagenes.SaveAsync(imagen.NombreArchivo, flujo, ct).ConfigureAwait(false);
            return (null, null, (guardada.StoragePath, guardada.Sha256, real, guardada.SizeBytes));
        }
        finally
        {
            if (copia is not null)
                await copia.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static string ResumenFaq(FaqPn f) => PersonaNaturalAuditoria.Json(
        ("pregunta", f.Pregunta), ("respuesta", f.Respuesta), ("orden", f.Orden), ("activa", f.Activa));

    private static string ResumenBanner(BannerPn b) => PersonaNaturalAuditoria.Json(
        ("titulo", b.Titulo), ("textoAlternativo", b.TextoAlternativo), ("enlaceUrl", b.EnlaceUrl), ("orden", b.Orden),
        ("vigenteDesde", b.VigenteDesde), ("vigenteHasta", b.VigenteHasta), ("activo", b.Activo), ("imagenSha256", b.ImagenSha256));
}
