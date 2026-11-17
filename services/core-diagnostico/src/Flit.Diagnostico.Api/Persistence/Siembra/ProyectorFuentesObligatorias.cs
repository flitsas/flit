using System.Security.Cryptography;
using System.Text;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Domain.Catalogos;

namespace Flit.Diagnostico.Api.Persistence.Siembra;

/// <summary>
/// Siembra de las fuentes obligatorias (ADR-0078). Lee <see cref="ResolucionFuentesObligatorias.Semilla"/> y produce el
/// identificador UUID v5, las cuatro cabeceras y las líneas. No copia los códigos. La cabecera del unilateral entra;
/// sus líneas no, porque esa definición trae la lista vacía.
/// </summary>
internal static class ProyectorFuentesObligatorias
{
    /// <summary>Espacio de nombres DNS de la RFC 4122. Cambiarlo cambia todos los id de la siembra.</summary>
    private static readonly Guid EspacioDns = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>Instante fijo de la semilla. No es el reloj del proceso.</summary>
    public static readonly DateTimeOffset ActualizadoEnSemilla = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Las cuatro cabeceras, incluida la del unilateral. Cada llamada vuelve a proyectar.</summary>
    public static IReadOnlyList<DefinicionFuentesObligatorias> Cabeceras() =>
        ResolucionFuentesObligatorias.Semilla.Select(ProyectarCabecera).ToArray();

    /// <summary>Líneas de la semilla. Una definición con lista vacía no produce filas.</summary>
    public static IReadOnlyList<FuenteDefinicionObligatoria> Lineas()
    {
        var lineas = new List<FuenteDefinicionObligatoria>();
        foreach (var definicion in ResolucionFuentesObligatorias.Semilla)
        {
            var id = IdDe(definicion);
            foreach (var fuente in definicion.Fuentes.Distinct())
            {
                lineas.Add(new FuenteDefinicionObligatoria
                {
                    DefinicionId = id,
                    Fuente = fuente,
                });
            }
        }

        return lineas;
    }

    private static DefinicionFuentesObligatorias ProyectarCabecera(DefinicionDeFuentes definicion) => new()
    {
        Id = IdDe(definicion),
        Familia = definicion.Familia,
        TipoTramiteCodigo = definicion.TipoTramiteCodigo,
        ActualizadoEn = ActualizadoEnSemilla,
        ActualizadoPor = null,
    };

    private static Guid IdDe(DefinicionDeFuentes definicion) => UuidV5(EspacioDns, NombreDe(definicion));

    /// <summary>
    /// <c>flit.diagnostico.definiciones_fuentes_obligatorias/familia/{familia}</c> o
    /// <c>.../tipo/{tipo}</c>, sin espacios.
    /// </summary>
    private static string NombreDe(DefinicionDeFuentes definicion)
    {
        if (definicion.Familia is not null && definicion.TipoTramiteCodigo is null)
            return string.Concat("flit.diagnostico.definiciones_fuentes_obligatorias/familia/", definicion.Familia);
        if (definicion.Familia is null && definicion.TipoTramiteCodigo is not null)
            return string.Concat("flit.diagnostico.definiciones_fuentes_obligatorias/tipo/", definicion.TipoTramiteCodigo);
        throw new InvalidOperationException("La semilla de fuentes obligatorias no tiene un alcance exclusivo.");
    }

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
