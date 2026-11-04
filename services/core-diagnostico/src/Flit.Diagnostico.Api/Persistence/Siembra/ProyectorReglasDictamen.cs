using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Domain.Dictamen;

namespace Flit.Diagnostico.Api.Persistence.Siembra;

/// <summary>
/// Siembra de <c>diagnostico.reglas_dictamen</c> (ADR-0077). Lee <see cref="CatalogoReglasDictamen.Filas"/> y produce el
/// identificador UUID v5, la condición y el resto de la fila. No copia efectos ni plantillas a mano. Dos llamadas con la
/// misma clave devuelven el mismo <see cref="Guid"/>.
/// </summary>
internal static class ProyectorReglasDictamen
{
    /// <summary>Espacio de nombres DNS de la RFC 4122. Cambiarlo cambia todos los id de la siembra.</summary>
    private static readonly Guid EspacioDns = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>42 filas v1, todas vigentes. Cada llamada vuelve a proyectar: el id sale del nombre, no de un caché.</summary>
    public static IReadOnlyList<ReglaDictamen> Proyectar() =>
        CatalogoReglasDictamen.Filas.Select(ProyectarFila).ToArray();

    private static ReglaDictamen ProyectarFila(FilaReglaDictamen fila) => new()
    {
        Id = UuidV5(EspacioDns, NombreDe(fila)),
        CatalogoVersion = fila.CatalogoVersion,
        TipoTramiteCodigo = fila.TipoTramiteCodigo,
        Codigo = fila.Codigo,
        Condicion = CondicionDe(fila.Codigo),
        Efecto = fila.Efecto,
        PlantillaTexto = fila.PlantillaTexto,
        Vigente = true,
    };

    /// <summary>
    /// <c>flit.diagnostico.reglas_dictamen/{catalogo_version}/{tipo_tramite_codigo}/{codigo}</c>, sin espacios.
    /// <c>catalogo_version</c> en decimal invariante, sin relleno.
    /// </summary>
    private static string NombreDe(FilaReglaDictamen fila) =>
        string.Concat(
            "flit.diagnostico.reglas_dictamen/",
            fila.CatalogoVersion.ToString(CultureInfo.InvariantCulture),
            "/",
            fila.TipoTramiteCodigo,
            "/",
            fila.Codigo);

    /// <summary>Un objeto con una sola clave. No es un motor de reglas.</summary>
    private static string CondicionDe(string codigo) => string.Concat("{\"codigo\":\"", codigo, "\"}");

    /// <summary>UUID versión 5 (RFC 4122 §4.3): SHA-1 del espacio de nombres y del nombre. No es un hash de contraseñas.</summary>
    private static Guid UuidV5(Guid espacio, string nombre)
    {
        Span<byte> espacioBytes = stackalloc byte[16];
        if (!espacio.TryWriteBytes(espacioBytes, bigEndian: true, out _))
            throw new InvalidOperationException("El espacio de nombres UUID no cabe en 16 bytes.");

        var nombreBytes = Encoding.UTF8.GetBytes(nombre);
        var entrada = new byte[espacioBytes.Length + nombreBytes.Length];
        espacioBytes.CopyTo(entrada);
        nombreBytes.CopyTo(entrada.AsSpan(espacioBytes.Length));

#pragma warning disable CA5350 // UUID v5 exige SHA-1 por la RFC 4122; no es uso criptográfico.
        var hash = SHA1.HashData(entrada);
#pragma warning restore CA5350

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
