using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Configuracion;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Plantillas;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Api.Modulos.Avisos;

/// <summary>Los adjuntos de un aviso y lo que no se pudo adjuntar, para decirlo en la línea de tiempo.</summary>
public sealed record Adjuntos(IReadOnlyList<AdjuntoCorreo> Archivos, bool Autorizacion, bool Poder, IReadOnlyList<string> Faltantes);

/// <summary>
/// HU #13884: lo que va adjunto a los avisos al conductor, como en V1: la autorización de cambio de infractor firmada por
/// el representante legal del propietario y, en los avisos 1 a 3, su poder («Escritura o poder» en Representantes
/// legales). Si falta el representante, su firma o el poder, el aviso sale sin ese adjunto (nunca con un archivo vacío,
/// como pasaba en V1) y queda dicho en la línea de tiempo.
/// </summary>
public sealed class AdjuntosAviso(ResolutorRepresentante resolutor, RegistroArchivos archivos, ContextoPlantilla contexto, ServicioPlantillas plantillas, FotoFotomulta foto)
{
    public async Task<Adjuntos> ParaAvisoAsync(Comparendo c, ConfiguracionEmpresa config, TipoAviso tipo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(config);
        var lista = new List<AdjuntoCorreo>();
        var faltantes = new List<string>();
        var (_, rl) = await resolutor.DelPropietarioAsync(c, config.EmpresaNit, ParaFirmar.CambioContraventor, ct).ConfigureAwait(false);
        var firma = rl?.FirmaArchivoId is { } f ? (await archivos.LeerAsync(f, ct).ConfigureAwait(false))?.Contenido : null;

        var autorizacion = false;
        if (rl is null)
            faltantes.Add("la autorización y el poder (la empresa no tiene un representante legal con permiso para el cambio de contraventor)");
        else if (firma is not { Length: > 0 })
            faltantes.Add("la autorización (el representante legal no tiene la firma cargada)");
        else
        {
            var valores = await contexto.DeAsync(c, new ExtrasContexto(Representante: rl, FirmaPng: firma), ct).ConfigureAwait(false);
            var armado = await plantillas.ArmarAsync(config.ConjuntoPlantillas, TipoPlantilla.AutorizacionCambioInfractor, null, valores, ct).ConfigureAwait(false);
            var pdf = ServicioPlantillas.Pdf(armado, titulo: $"Autorización de cambio de infractor — {c.Numero}", fondo: ServicioPlantillas.Membrete(config.ConjuntoPlantillas));
            lista.Add(new AdjuntoCorreo($"autorizacion-cambio-infractor-{c.Placa ?? c.Numero}.pdf", "application/pdf", pdf));
            autorizacion = true;
        }

        // El aviso 4 confirma que la solicitud ya se radicó: lleva la autorización, no el poder.
        var poder = false;
        if (rl is not null && tipo != TipoAviso.Aviso4)
        {
            if (rl.EscrituraArchivoId is { } escritura && await archivos.LeerAsync(escritura, ct).ConfigureAwait(false) is { } archivo && archivo.Contenido.Length > 0)
            {
                lista.Add(new AdjuntoCorreo($"poder-{Nombre(config.EmpresaNombre)}{Path.GetExtension(archivo.Archivo.NombreOriginal)}", archivo.Archivo.ContentType, [], archivo.Archivo.Id));
                poder = true;
            }
            else
            {
                faltantes.Add("el poder (el representante legal no tiene «Escritura o poder» cargado)");
            }
        }

        if (tipo != TipoAviso.Aviso4 && await foto.ParaAvisoAsync(c, ct).ConfigureAwait(false) is { } documento)
            lista.Add(documento);
        return new Adjuntos(lista, autorizacion, poder, faltantes);
    }

    /// <summary>«Renting Colombia S.A.S» → «renting-colombia-s-a-s»: el nombre de archivo del poder.</summary>
    internal static string Nombre(string? empresa)
    {
        var limpio = new string((empresa ?? "empresa").Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray());
        return string.Join('-', limpio.Split('-', StringSplitOptions.RemoveEmptyEntries)) is { Length: > 0 } n ? n : "empresa";
    }
}
