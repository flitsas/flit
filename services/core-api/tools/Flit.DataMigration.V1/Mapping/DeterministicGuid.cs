using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Genera UUIDs estables a partir del origen en V1 (UUID v5, RFC 4122).
/// <para>
/// El mismo trámite de V1 produce SIEMPRE el mismo id en V2. Eso hace que la migración
/// sea idempotente por construcción —no solo por la tabla <c>migration_map</c>— y que un
/// <c>--force</c> reemplace el registro anterior en lugar de crear un duplicado silencioso.
/// </para>
/// </summary>
public static class DeterministicGuid
{
    /// <summary>Namespace propio de la migración (UUID fijo, no cambiar: cambiaría todos los ids).</summary>
    private static readonly Guid MigrationNamespace = new("6f9a1d3e-6c2f-5a4b-9e7d-2f1c8b3a4d50");

    /// <summary>Id determinístico para una fila de V1 (<paramref name="table"/> + <paramref name="id"/>).</summary>
    public static Guid ForV1Row(string table, long id) =>
        Create(MigrationNamespace, $"{table}:{id.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>Id determinístico para un hijo del trámite (actor, campo, evento de historial).</summary>
    public static Guid ForV1Child(string table, long id, string discriminator) =>
        Create(MigrationNamespace, $"{table}:{id.ToString(CultureInfo.InvariantCulture)}:{discriminator}");

    private static Guid Create(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);

#pragma warning disable CA5350 // UUID v5 exige SHA-1 por especificación (RFC 4122); no es uso criptográfico.
        var hash = SHA1.HashData(input);
#pragma warning restore CA5350

        var result = new byte[16];
        Buffer.BlockCopy(hash, 0, result, 0, 16);

        // Versión 5 y variante RFC 4122.
        result[6] = (byte)((result[6] & 0x0F) | 0x50);
        result[8] = (byte)((result[8] & 0x3F) | 0x80);

        SwapByteOrder(result);
        return new Guid(result);
    }

    /// <summary>Convierte entre el orden de bytes de .NET (little-endian) y el de RFC 4122.</summary>
    private static void SwapByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
