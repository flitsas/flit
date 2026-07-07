using Flit.Admin.Domain.Companies.MandateSigners;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// Huella de integridad del mandatario (ADR-0023, decisión #2): SHA-256 determinista de
/// nombre + documento + fecha de registro. Verifica determinismo, sensibilidad a cada insumo
/// (regeneración al editar) y estabilidad frente al huso horario de la fecha.
/// </summary>
public sealed class MandateSignerIntegrityHashTests
{
    private static readonly DateTimeOffset RegisteredAt =
        new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_IsDeterministic_ForSameInputs()
    {
        var a = MandateSignerIntegrityHash.Compute("Samuel Cárdenas", "123456", RegisteredAt);
        var b = MandateSignerIntegrityHash.Compute("Samuel Cárdenas", "123456", RegisteredAt);

        a.Should().Be(b);
        a.Should().MatchRegex("^[0-9a-f]{64}$"); // SHA-256 en hex minúsculas.
    }

    [Fact]
    public void Compute_ChangesWhenNameChanges_RegeneratesOnEdit()
    {
        var original = MandateSignerIntegrityHash.Compute("Samuel Cárdenas", "123456", RegisteredAt);
        var edited = MandateSignerIntegrityHash.Compute("Samuel A. Cárdenas", "123456", RegisteredAt);

        edited.Should().NotBe(original);
    }

    [Fact]
    public void Compute_ChangesWhenDocumentChanges()
    {
        var a = MandateSignerIntegrityHash.Compute("Samuel Cárdenas", "123456", RegisteredAt);
        var b = MandateSignerIntegrityHash.Compute("Samuel Cárdenas", "999999", RegisteredAt);

        b.Should().NotBe(a);
    }

    [Fact]
    public void Compute_IsStableAcrossTimeZones_ForSameInstant()
    {
        var utc = MandateSignerIntegrityHash.Compute("Samuel", "123456", RegisteredAt);
        // Mismo instante expresado en otro huso → misma huella (se normaliza a UTC).
        var offset = MandateSignerIntegrityHash.Compute(
            "Samuel", "123456", RegisteredAt.ToOffset(TimeSpan.FromHours(-5)));

        offset.Should().Be(utc);
    }

    [Fact]
    public void Compute_TrimsSurroundingWhitespace()
    {
        var trimmed = MandateSignerIntegrityHash.Compute("Samuel", "123456", RegisteredAt);
        var padded = MandateSignerIntegrityHash.Compute("  Samuel  ", "  123456 ", RegisteredAt);

        padded.Should().Be(trimmed);
    }
}
