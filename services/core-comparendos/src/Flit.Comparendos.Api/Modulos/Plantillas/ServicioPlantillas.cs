using Flit.Comparendos.Api.Infraestructura.Pdf;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Plantillas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Plantillas;

/// <summary>Un documento o correo armado con la versión de plantilla que se usó (los DP y comunicaciones la guardan).</summary>
public sealed record Armado(Guid VersionId, string? Asunto, string Html);

/// <summary>
/// Elige la versión publicada de una plantilla para el conjunto de la empresa (si Renting no tiene la suya, la de FLIT,
/// S-23) y la arma con los valores del comparendo. Los correos van dentro del marco HTML: el de FLIT, o uno neutro sin
/// rastro de FLIT para el conjunto Renting (D9), aunque el texto venga de la plantilla de FLIT por respaldo.
/// </summary>
public sealed class ServicioPlantillas(ComparendosDb db, IConfiguration configuration)
{
    /// <summary>Imágenes de los correos (HU #13883): las publica el frontend de Comparendos en /assets/correo.</summary>
    public string Recursos => $"{(configuration["Comparendos:AppUrl"] ?? "http://127.0.0.1:4023").TrimEnd('/')}/assets/correo";

    public async Task<PlantillaVersion?> PublicadaAsync(ConjuntoPlantillas conjunto, TipoPlantilla tipo, Guid? tipologiaId, CancellationToken ct)
    {
        foreach (var c in conjunto == ConjuntoPlantillas.Flit ? new[] { ConjuntoPlantillas.Flit } : [conjunto, ConjuntoPlantillas.Flit])
        {
            var version = await (from p in db.Plantillas.AsNoTracking()
                                 join v in db.PlantillaVersiones.AsNoTracking() on p.VersionPublicadaId equals v.Id
                                 where p.Conjunto == c && p.Tipo == tipo && p.TipologiaId == tipologiaId && p.Activo
                                 select v).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (version is not null)
                return version;
        }

        return null;
    }

    public async Task<Armado> ArmarAsync(ConjuntoPlantillas conjunto, TipoPlantilla tipo, Guid? tipologiaId, IReadOnlyDictionary<string, string?> valores, CancellationToken ct)
    {
        var version = await PublicadaAsync(conjunto, tipo, tipologiaId, ct).ConfigureAwait(false)
            ?? throw new ReglaException("SIN_PLANTILLA", $"No hay una plantilla publicada de «{tipo}» para el conjunto de la empresa.");
        return Armar(version, tipo, valores, conjunto, Recursos);
    }

    /// <summary>
    /// Los correos que lleva el conductor o el organismo (HU #13883): van en el marco de Trámites (FLIT) o en el de V1
    /// (Renting). Las alertas y reportes internos conservan el marco sencillo.
    /// </summary>
    public static bool ConMarca(TipoPlantilla tipo) => tipo is TipoPlantilla.CorreoAviso1 or TipoPlantilla.CorreoAviso2 or TipoPlantilla.CorreoAviso3
        or TipoPlantilla.CorreoAviso4 or TipoPlantilla.CorreoCambioContraventorOt;

    public static Armado Armar(PlantillaVersion version, TipoPlantilla tipo, IReadOnlyDictionary<string, string?> valores, ConjuntoPlantillas conjunto = ConjuntoPlantillas.Flit,
        string recursos = "http://127.0.0.1:4023/assets/correo")
    {
        ArgumentNullException.ThrowIfNull(version);
        var cuerpo = MotorPlantillas.Renderizar(version.CuerpoHtml, valores);
        var asunto = version.Asunto is null ? null : MotorPlantillas.Renderizar(version.Asunto, valores, html: false);
        if (!EsCorreo(tipo))
            return new Armado(version.Id, asunto, cuerpo);
        var empresa = valores.GetValueOrDefault("empresa.razon_social") is { Length: > 0 } e ? e : null;
        var flit = conjunto == ConjuntoPlantillas.Flit;
        if (ConMarca(tipo))
            return new Armado(version.Id, asunto, flit ? HtmlCorreo.Flit(cuerpo, empresa, recursos, cierre: tipo != TipoPlantilla.CorreoCambioContraventorOt) : HtmlCorreo.Renting(cuerpo, recursos));
        var pie = empresa is null ? null : flit ? $"{empresa} · Comunicación enviada con FLIT." : $"{empresa} · Correo automático, no lo respondas.";
        return new Armado(version.Id, asunto, HtmlCorreo.Envolver(asunto ?? (flit ? "Comparendos" : empresa ?? "Comparendos"), cuerpo, pie, flit));
    }

    public static bool EsCorreo(TipoPlantilla tipo) => tipo.ToString().StartsWith("Correo", StringComparison.Ordinal);

    public static byte[] Pdf(Armado armado, string? marcaDeAgua = null, string? titulo = null, byte[]? fondo = null)
    {
        ArgumentNullException.ThrowIfNull(armado);
        return HtmlAPdf.Generar(armado.Html, marcaDeAgua, titulo, fondo);
    }

    /// <summary>HU #13884: el membrete de Renting de V1 para sus documentos en PDF; null para FLIT.</summary>
    public static byte[]? Membrete(ConjuntoPlantillas conjunto)
    {
        if (conjunto != ConjuntoPlantillas.Renting)
            return null;
        var asm = typeof(ServicioPlantillas).Assembly;
        using var stream = asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n => n.EndsWith(".Membretes.renting.jpg", StringComparison.Ordinal)))!;
        using var memoria = new MemoryStream();
        stream.CopyTo(memoria);
        return memoria.ToArray();
    }
}
