using Flit.Tramites.Domain.ExternalSync;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.ExternalSync;

/// <summary>
/// HU #13076 — la posición del feed ordena primero por transacción y después por versión: un cambio de
/// una transacción anterior va antes aunque su versión sea mayor.
/// <code>new ProcedureSyncPosition(Transaction: 10, Version: 99) &lt; new ProcedureSyncPosition(11, 1)</code>
/// </summary>
public sealed class ProcedureSyncPositionTests
{
    [Fact]
    public void OrdenaPorTransaccionAntesQuePorVersion()
    {
        new ProcedureSyncPosition(10, 99).Should().BeLessThan(new ProcedureSyncPosition(11, 1));
        (new ProcedureSyncPosition(10, 99) < new ProcedureSyncPosition(11, 1)).Should().BeTrue();
    }

    [Fact]
    public void EnLaMismaTransaccionOrdenaPorVersion()
    {
        (new ProcedureSyncPosition(10, 2) > new ProcedureSyncPosition(10, 1)).Should().BeTrue();
        (new ProcedureSyncPosition(10, 2) >= new ProcedureSyncPosition(10, 2)).Should().BeTrue();
        (new ProcedureSyncPosition(10, 1) <= new ProcedureSyncPosition(10, 2)).Should().BeTrue();
    }

    [Fact]
    public void LaAsignacionInicialSinTransaccionVaPrimero()
    {
        new ProcedureSyncPosition(0, 500).Should().BeLessThan(new ProcedureSyncPosition(1, 1));
    }

    [Fact]
    public void LasPeticionesDeCursorYFechaSonExcluyentesPorConstruccion()
    {
        ProcedureSyncPageRequest.FromCursor(new(1, 1), 200, TimeSpan.FromSeconds(5)).Since.Should().BeNull();
        ProcedureSyncPageRequest.FromSince(DateTimeOffset.UnixEpoch, 200, TimeSpan.FromSeconds(5)).After.Should().BeNull();
    }
}
