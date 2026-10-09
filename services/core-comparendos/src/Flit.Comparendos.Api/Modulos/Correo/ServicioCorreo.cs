using System.Text;
using System.Text.Json;
using Flit.Comparendos.Api.Infraestructura.Archivos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Api.Modulos.Correo;

public sealed record Destinatario(string Email, string? Nombre = null);

public sealed record AdjuntoCorreo(string Nombre, string ContentType, byte[] Contenido, Guid? ArchivoId = null);

/// <summary>Un correo por enviar, ya armado (asunto y HTML finales, decisión del Feature #13324 de Notificaciones).</summary>
public sealed record CorreoNuevo
{
    public Guid? TenantId { get; init; }
    public Guid? ComparendoId { get; init; }
    public required TipoComunicacion Tipo { get; init; }
    public required string Plantilla { get; init; }
    public Guid? PlantillaVersionId { get; init; }
    public required IReadOnlyList<Destinatario> Para { get; init; }
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];
    public required string Asunto { get; init; }
    public required string Html { get; init; }
    public IReadOnlyList<AdjuntoCorreo> Adjuntos { get; init; } = [];
    public CanalCorreo Canal { get; init; } = CanalCorreo.FlitSmtp;
    public string? RemitenteNombre { get; init; }
    public Guid? CreadoPor { get; init; }
}

/// <summary>
/// Registra correos en el Centro de Mensajes (§3.23) para que el <see cref="DespachadorCorreos"/> los envíe por la
/// plataforma. La plataforma no maneja CC: va un correo por cada destinatario visible (para y CC), cada uno con su
/// registro y su píxel, y el BCC como copia oculta (S-59). El HTML enviado se guarda como archivo para auditoría.
/// Todo queda en el contexto: lo confirma el SaveChanges de quien llama, en su misma transacción.
/// </summary>
public sealed class ServicioCorreo(ComparendosDb db, IAlmacenArchivos almacen, Reloj reloj, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>URL pública de la app (enlaces del portal y píxel).</summary>
    public string AppUrl => (configuration["Comparendos:AppUrl"] ?? "http://127.0.0.1:4023").TrimEnd('/');

    public async Task<IReadOnlyList<ComunicacionSaliente>> RegistrarAsync(CorreoNuevo correo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(correo);
        var visibles = correo.Para.Concat(correo.Cc.Select(c => new Destinatario(c)))
            .Where(d => !string.IsNullOrWhiteSpace(d.Email))
            .DistinctBy(d => d.Email.Trim().ToLowerInvariant())
            .ToList();
        if (visibles.Count == 0)
            throw new ReglaException("SIN_DESTINATARIO", "El correo no tiene destinatarios.");

        var adjuntos = new List<(Guid ArchivoId, string Nombre)>();
        foreach (var a in correo.Adjuntos)
            adjuntos.Add((a.ArchivoId ?? await GuardarAsync(correo.TenantId, "adjunto_correo", a.Nombre, a.ContentType, a.Contenido, ct).ConfigureAwait(false), a.Nombre));

        var bcc = correo.Bcc.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim().ToLowerInvariant())
            .Except(visibles.Select(v => v.Email.Trim().ToLowerInvariant())).Distinct().ToList();
        var lista = new List<ComunicacionSaliente>();
        foreach (var d in visibles)
        {
            var id = Guid.CreateVersion7();
            var html = ConPixel(correo.Html, id);
            var cuerpo = await GuardarAsync(correo.TenantId, "correo_html", $"{id}.html", "text/html", Encoding.UTF8.GetBytes(html), ct).ConfigureAwait(false);
            var c = new ComunicacionSaliente
            {
                Id = id,
                TenantId = correo.TenantId,
                ComparendoId = correo.ComparendoId,
                Tipo = correo.Tipo,
                Canal = CanalComunicacion.Email,
                CanalCorreo = correo.Canal,
                RemitenteNombre = correo.RemitenteNombre,
                Destinatarios = JsonSerializer.Serialize(new
                {
                    para = new[] { new { email = d.Email.Trim(), nombre = d.Nombre } },
                    cc = visibles.Where(v => v != d).Select(v => v.Email.Trim()),
                    bcc,
                }, Json),
                Asunto = correo.Asunto.Length > 300 ? correo.Asunto[..300] : correo.Asunto,
                CuerpoHtmlArchivoId = cuerpo,
                PlantillaVersionId = correo.PlantillaVersionId,
                Estado = EstadoComunicacion.Pendiente,
                PlantillaClave = correo.Plantilla,
                CreadoEn = reloj.Ahora,
                CreadoPor = correo.CreadoPor,
            };
            db.Comunicaciones.Add(c);
            foreach (var (archivoId, nombre) in adjuntos)
                db.ComunicacionAdjuntos.Add(new ComunicacionAdjunto { Id = Guid.CreateVersion7(), ComunicacionId = id, ArchivoId = archivoId, Nombre = nombre });
            lista.Add(c);
        }

        return lista;
    }

    /// <summary>Píxel de apertura al final del cuerpo: dato informativo, nunca prueba de notificación [Excel C12].</summary>
    public string ConPixel(string html, Guid comunicacionId)
    {
        ArgumentNullException.ThrowIfNull(html);
        var pixel = $"<img src=\"{AppUrl}/publico/t/{comunicacionId:N}.gif\" width=\"1\" height=\"1\" alt=\"\" style=\"display:block;border:0\" />";
        var cierre = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return cierre >= 0 ? html.Insert(cierre, pixel) : html + pixel;
    }

    public async Task<Guid> GuardarAsync(Guid? tenantId, string dominio, string nombre, string contentType, byte[] contenido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var guardado = await almacen.GuardarAsync(tenantId ?? Guid.Empty, dominio, nombre, contentType, contenido, ct).ConfigureAwait(false);
        var archivo = new Archivo
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Referencia = guardado.Referencia,
            Dominio = dominio,
            NombreOriginal = nombre.Length > 255 ? nombre[^255..] : nombre,
            ContentType = contentType,
            TamanoBytes = guardado.Tamano,
            Sha256 = guardado.Sha256,
            CreadoEn = reloj.Ahora,
        };
        db.Archivos.Add(archivo);
        return archivo.Id;
    }
}
