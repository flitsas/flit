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
/// S-23) y la arma con los valores del comparendo. Los correos van dentro del marco HTML de FLIT.
/// </summary>
public sealed class ServicioPlantillas(ComparendosDb db)
{
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
        return Armar(version, tipo, valores);
    }

    public static Armado Armar(PlantillaVersion version, TipoPlantilla tipo, IReadOnlyDictionary<string, string?> valores)
    {
        ArgumentNullException.ThrowIfNull(version);
        var cuerpo = MotorPlantillas.Renderizar(version.CuerpoHtml, valores);
        var asunto = version.Asunto is null ? null : MotorPlantillas.Renderizar(version.Asunto, valores, html: false);
        return new Armado(version.Id, asunto, EsCorreo(tipo) ? HtmlCorreo.Envolver(asunto ?? "Comparendos", cuerpo, valores.GetValueOrDefault("empresa.razon_social") is { Length: > 0 } e ? $"{e} · Comunicación enviada con FLIT." : null) : cuerpo);
    }

    public static bool EsCorreo(TipoPlantilla tipo) => tipo.ToString().StartsWith("Correo", StringComparison.Ordinal);

    public static byte[] Pdf(Armado armado, string? marcaDeAgua = null)
    {
        ArgumentNullException.ThrowIfNull(armado);
        return HtmlAPdf.Generar(armado.Html, marcaDeAgua);
    }
}
