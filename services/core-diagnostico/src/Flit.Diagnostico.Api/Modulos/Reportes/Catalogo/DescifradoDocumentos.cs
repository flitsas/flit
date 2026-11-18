using System.Security.Cryptography;
using Flit.Diagnostico.Domain.Personas;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Descifra en memoria los documentos de las filas ya registradas (HU #14579, BE-2; SP-9, SP-16, diseno.md «Límites»), por lotes de
/// <see cref="ValoresReporte.Lote"/> filas, con <see cref="IProtectorDocumentoPersona"/>. Cada documento sale como «{tipo} {número}» y
/// los de una columna se unen con «; » en su orden. Si uno no se puede descifrar, ese número sale «No disponible», el reporte sigue y al
/// log solo va el id interno de la persona y el tipo de la excepción (nunca su mensaje ni el cifrado).
/// </summary>
internal sealed class DescifradoDocumentos(IProtectorDocumentoPersona protector, ILogger<DescifradoDocumentos> logger)
{
    public void Aplicar(TipoReporte tipo, IReadOnlyList<FilaReporte> filas)
    {
        ArgumentNullException.ThrowIfNull(filas);
        var codigo = TiposDeReporte.Codigo(tipo);
        foreach (var lote in ValoresReporte.Lotes(filas))
        {
            foreach (var fila in lote)
            {
                foreach (var campo in fila.Cifrados)
                {
                    fila.Valores[campo.Clave] = campo.Documentos.Count == 0
                        ? null
                        : string.Join("; ", campo.Documentos.Select(d => $"{d.Tipo} {Numero(codigo, d)}"));
                }

                fila.Cifrados.Clear();
            }
        }
    }

    private string Numero(string tipo, DocumentoCifrado documento)
    {
        try
        {
            return protector.Desproteger(documento.Cifrado);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException or InvalidOperationException)
        {
            LogReportes.NoDescifrado(logger, tipo, documento.PersonaId, ex.GetType().Name);
            return DefinicionReporte.NoDisponible;
        }
    }
}
