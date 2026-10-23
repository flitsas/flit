using System.Buffers.Text;
using System.Text;
using Flit.Tramites.Domain.ExternalSync;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.ExternalSync;

/// <summary>
/// HU #13081 — cursor opaco del feed: ida y vuelta de la posición y de la fecha de arranque, y rechazo
/// de todo lo que no sea un cursor emitido por FLIT.
/// <code>ExternalSyncCursor.TryDecode(ExternalSyncCursor.Encode(new(900, 48213)), out var pos, out _)</code>
/// </summary>
public sealed class ExternalSyncCursorTests
{
    [Fact]
    public void LaPosicionVaYVuelve()
    {
        var cursor = ExternalSyncCursor.Encode(new ProcedureSyncPosition(ulong.MaxValue - 1, 48213));

        ExternalSyncCursor.TryDecode(cursor, out var posicion, out var desde).Should().BeTrue();
        posicion.Should().Be(new ProcedureSyncPosition(ulong.MaxValue - 1, 48213));
        desde.Should().BeNull();
        cursor.Should().MatchRegex("^[A-Za-z0-9_-]+$", "base64url sin relleno, seguro en la URL");
    }

    [Fact]
    public void LaFechaDeArranqueVaYVuelveConSuOffset()
    {
        var since = new DateTimeOffset(2026, 9, 21, 10, 15, 0, TimeSpan.FromHours(-5));

        ExternalSyncCursor.TryDecode(ExternalSyncCursor.EncodeSince(since), out var posicion, out var desde).Should().BeTrue();

        posicion.Should().BeNull();
        desde.Should().Be(since);
        desde!.Value.Offset.Should().Be(TimeSpan.FromHours(-5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-base64!!")]
    [InlineData("eyJ2IjoxLCJzdiI6NDgyMTN9")] // {"v":1,"sv":48213}: el formato del plan, sin transacción
    public void CursoresInvalidosSeRechazan(string cursor)
    {
        ExternalSyncCursor.TryDecode(cursor, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"v\":2,\"x\":\"1\",\"sv\":1}")]
    [InlineData("{\"v\":1,\"x\":\"-1\",\"sv\":1}")]
    [InlineData("{\"v\":1,\"x\":\"1\",\"sv\":-1}")]
    [InlineData("{\"v\":1,\"x\":\"1\",\"sv\":1,\"since\":\"2026-09-21T10:15:00.0000000-05:00\"}")]
    [InlineData("{\"v\":1,\"since\":\"ayer\"}")]
    [InlineData("[1,2,3]")]
    public void JsonAjenoOIncoherenteSeRechaza(string json)
    {
        var cursor = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

        ExternalSyncCursor.TryDecode(cursor, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void UnCursorDemasiadoLargoSeRechazaSinDecodificar()
    {
        ExternalSyncCursor.TryDecode(new string('A', 600), out _, out _).Should().BeFalse();
    }
}
