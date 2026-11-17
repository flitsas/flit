using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>HU #14494 — unitarios del helper <see cref="ConsolidadoCoherenciaTemporal"/>.</summary>
public sealed class ConsolidadoCoherenciaTemporalTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T10:00:00Z");
    private static readonly DateTimeOffset T1 = DateTimeOffset.Parse("2026-01-15T12:00:00Z");

    [Fact]
    public void ResolverTpdf_UsaGeneradoEnDelTipo()
    {
        var instance = new ProcedureInstance { ConsolidadoWizardGeneradoEn = T0 };
        var adjunto = new ProcedureInstanceAttachment { UploadedAt = T1 };

        ConsolidadoCoherenciaTemporal
            .ResolverTpdf(instance, ConsolidadoEntregaTipo.Wizard, adjunto)
            .Should().Be(T0);
    }

    [Fact]
    public void ResolverTpdf_FallbackUploadedAtSinSello()
    {
        var instance = new ProcedureInstance();
        var adjunto = new ProcedureInstanceAttachment { UploadedAt = T1 };

        ConsolidadoCoherenciaTemporal
            .ResolverTpdf(instance, ConsolidadoEntregaTipo.Maestro, adjunto)
            .Should().Be(T1);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("2026-01-01T10:00:00Z", null, "2026-01-01T10:00:00Z")]
    [InlineData(null, "2026-01-20T08:00:00Z", "2026-01-20T08:00:00Z")]
    [InlineData("2026-01-01T10:00:00Z", "2026-01-20T08:00:00Z", "2026-01-20T08:00:00Z")]
    public void ResolverTCambioRelevante_TomaElMaximo(string? cambio, string? expediente, string? esperado)
    {
        var c = cambio is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(cambio);
        var e = expediente is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(expediente);
        var x = esperado is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(esperado);

        ConsolidadoCoherenciaTemporal.ResolverTCambioRelevante(c, e).Should().Be(x);
    }

    [Fact]
    public void EsObsoleto_BanderaAbajo()
    {
        ConsolidadoCoherenciaTemporal.EsObsoleto(T1, T0, banderaVigente: false).Should().BeTrue();
    }

    [Fact]
    public void EsObsoleto_TpdfAnteriorAlCambio()
    {
        ConsolidadoCoherenciaTemporal.EsObsoleto(T0, T1, banderaVigente: true).Should().BeTrue();
    }

    [Fact]
    public void EsObsoleto_FrescoSinRegenerar()
    {
        ConsolidadoCoherenciaTemporal.EsObsoleto(T1, T0, banderaVigente: true).Should().BeFalse();
    }

    [Fact]
    public void EsObsoleto_SinHistorialSoloBandera()
    {
        ConsolidadoCoherenciaTemporal.EsObsoleto(T0, null, banderaVigente: true).Should().BeFalse();
    }
}
