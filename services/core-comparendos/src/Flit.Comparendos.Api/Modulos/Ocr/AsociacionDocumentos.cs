using Flit.Comparendos.Api.Modulos.Avisos;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ocr;

/// <summary>
/// GDC-EVID: asociar un documento escaneado y mandar su correo de evidencia, igual en la pantalla individual y en
/// «Asociar los N» (§10, desde el trabajador de fondo). Una vez empezado, el trabajo de un documento NO usa el token de la petición: si el navegador se va
/// justo después de confirmar la asociación, el documento quedaría asociado sin correo ni nota y, al reintentar, ya figuraría
/// asociado y no se reenviaría nunca. Corre con un plazo propio por documento.
/// </summary>
public sealed class AsociacionDocumentos(ComparendosDb db, ServicioOcr servicio, ServicioEvidencia evidencia)
{
    /// <summary>Plazo propio de asociar un documento (sin contar el correo, que tiene el suyo).</summary>
    private static readonly TimeSpan TopeDocumento = TimeSpan.FromSeconds(30);

    /// <summary>Solo pruebas: se llama justo antes de asociar cada documento (para ejercer una cancelación a mitad).</summary>
    internal Action<Guid>? AntesDeAsociar { get; set; }

    /// <summary>
    /// Asocia <paramref name="doc"/> y registra su correo. <paramref name="despachar"/> false = el correo queda en cola (lote).
    /// Lanza las ReglaException de <see cref="ServicioOcr.AsociarAsync"/> (409) antes de confirmar nada.
    /// </summary>
    /// <param name="desdeLote">Solo el trabajador de «Asociar los N» (§10): el documento está PorAsociar.</param>
    public async Task<(Asociacion Asociacion, ResultadoEvidencia Correo)> UnoAsync(DocumentoOcr doc, bool reemplazar, Guid? evidenciaVista, Actor actor, bool despachar,
        CancellationToken peticion, bool desdeLote = false)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var (tenant, archivo) = (doc.TenantId, doc.ArchivoId);
        AntesDeAsociar?.Invoke(doc.Id);
        Asociacion r;
        using (var propio = new CancellationTokenSource(TopeDocumento))
            r = await servicio.AsociarAsync(doc, reemplazar, evidenciaVista, actor, propio.Token, desdeLote).ConfigureAwait(false);
        if (r.YaAsociado)
            return (r, new ResultadoEvidencia(ResultadoEvidencia.Omitido, "El documento ya estaba asociado: no se vuelve a enviar"));
        db.ChangeTracker.Clear();
        var correo = await evidencia.EnviarAlAsociarAsync(r.ComparendoId, tenant, archivo, r.Reemplazo, actor, peticion, despachar).ConfigureAwait(false);
        return (r, correo);
    }
}
