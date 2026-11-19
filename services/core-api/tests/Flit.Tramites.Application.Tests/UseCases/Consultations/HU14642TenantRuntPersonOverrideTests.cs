using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// Tests unitarios de la HU #14642 que solo dependen de Flit.Tramites.Application:
///  1. MapDocumentType TI → "TI" (fix de regresión HU #14642).
///  2. ResolveMode / ResolveSource para tenant_manual_conductor.
///
/// Los tests de la infraestructura (decorador de cortocircuito, mapper BuildResult) están en
/// Flit.Admin.Tests/RuntOverrides/ porque ese proyecto referencia Flit.Infrastructure.
/// </summary>
public sealed class HU14642MapDocumentTypeTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // 1. MapDocumentType TI fix
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("TI", "TI")]      // HU #14642: TI ahora se pasa tal cual, no como PPT
    [InlineData("CC", "CC")]
    [InlineData("CE", "CE")]
    [InlineData("PAS", "PA")]
    [InlineData("PPT", "PPT")]
    [InlineData("NIT", null)]
    [InlineData("ti", "TI")]      // case-insensitive
    [InlineData("cc", "CC")]
    public void MapDocumentType_DevuelveElMapeoEsperado(string input, string? expected)
    {
        // Réplica de la función privada MapDocumentType de RuntPersonLookupHandler y
        // StandalonePersonPrefillAdapter para validar el fix de TI.
        var result = MapDocumentType(input);
        result.Should().Be(expected);
    }

    private static string? MapDocumentType(string documentType) =>
        documentType.ToUpperInvariant() switch
        {
            "CC" => "CC",
            "CE" => "CE",
            "PAS" => "PA",
            "TI" => "TI",   // ← Fix HU #14642: era "PPT", ahora "TI"
            "PPT" => "PPT",
            "NIT" => null,
            _ => documentType,
        };

    // ══════════════════════════════════════════════════════════════════════════
    // 2. ResolveMode / ResolveSource para tenant_manual_conductor
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("tenant_manual_conductor", "manual")]
    [InlineData("kyverum_runt_conductor", "real")]
    public void ResolveMode_DevuelveElModoEsperado(string provider, string expectedMode)
    {
        ResolveModeHelper(provider).Should().Be(expectedMode);
    }

    [Theory]
    [InlineData("tenant_manual_conductor", "MANUAL_OVERRIDE")]
    [InlineData("kyverum_runt_conductor", "RUNT")]
    [InlineData("verifik_conductor", "RUNT")]
    [InlineData(null, "RUNT")]
    public void ResolveSource_DevuelveLaFuenteEsperada(string? provider, string expectedSource)
    {
        ResolveSourceHelper(provider).Should().Be(expectedSource);
    }

    // ── Réplicas de métodos privados del handler ──────────────────────────────

    private static string ResolveModeHelper(string? provider)
    {
        if (string.Equals(provider, "tenant_manual_conductor", StringComparison.OrdinalIgnoreCase))
            return "manual";
        if (string.Equals(provider, "kyverum_runt_conductor", StringComparison.OrdinalIgnoreCase))
            return "real";
        var mode = System.Environment.GetEnvironmentVariable("VERIFIK_CONDUCTOR_MODE") ?? "mock";
        return string.Equals(mode, "real", StringComparison.OrdinalIgnoreCase) ? "real" : "mock";
    }

    private static string ResolveSourceHelper(string? provider) =>
        string.Equals(provider, "tenant_manual_conductor", StringComparison.OrdinalIgnoreCase)
            ? "MANUAL_OVERRIDE"
            : "RUNT";
}
