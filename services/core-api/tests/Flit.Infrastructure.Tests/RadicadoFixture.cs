using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Infrastructure.Tests;

/// <summary>
/// El <c>consecutivo</c> que acompaña al radicado de un fixture (HU #12371).
/// <para>En Postgres lo asigna el trigger; EF InMemory no tiene trigger, así que el fixture tiene
/// que traerlo o quedaría en 0 en todas las filas y el orden por radicado —que va por el número,
/// no por el texto— sería el de los GUID. Si el radicado se puede leer (<c>FT1-0000012</c>,
/// <c>4571</c>) se usa su número, para que las pruebas de orden y búsqueda numéricas signifiquen
/// algo; si es una etiqueta (<c>R1</c>, <c>A1</c>) se asigna por orden de creación, desde un
/// arranque alto para no chocar con los leídos.</para>
/// </summary>
internal static class RadicadoFixture
{
    private static long _siguiente = 1_000_000;

    public static long ConsecutivoDe(string reference) =>
        Radicado.TryLeer(reference, out var lectura)
            ? lectura.Value.Consecutivo
            : Interlocked.Increment(ref _siguiente);
}
