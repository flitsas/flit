using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>HU #10522 (RF40) — política de validación por IA de improntas.</summary>
public sealed class ImprontaPolicyEvaluatorTests
{
    [Fact]
    public void AlcanzaUmbral_Ok()
    {
        ImprontaPolicyEvaluator.Evaluate(0.9, 0.8, blockBelowThreshold: false)
            .Should().Be(ImprontaPolicyDecision.Ok);
        ImprontaPolicyEvaluator.Evaluate(0.8, 0.8, blockBelowThreshold: true)
            .Should().Be(ImprontaPolicyDecision.Ok);
    }

    [Fact]
    public void BajoUmbral_PoliticaAdvertir_Warn()
    {
        // Por defecto (advertir) no bloquea la radicación.
        ImprontaPolicyEvaluator.Evaluate(0.5, 0.8, blockBelowThreshold: false)
            .Should().Be(ImprontaPolicyDecision.Warn);
    }

    [Fact]
    public void BajoUmbral_PoliticaBloquear_Block()
    {
        ImprontaPolicyEvaluator.Evaluate(0.5, 0.8, blockBelowThreshold: true)
            .Should().Be(ImprontaPolicyDecision.Block);
    }

    [Fact]
    public void NormalizaEscalaPorcentual()
    {
        // 90 (%) vs 80 (%) equivale a 0.9 vs 0.8.
        ImprontaPolicyEvaluator.Evaluate(90, 80, blockBelowThreshold: false)
            .Should().Be(ImprontaPolicyDecision.Ok);
        ImprontaPolicyEvaluator.Evaluate(50, 80, blockBelowThreshold: false)
            .Should().Be(ImprontaPolicyDecision.Warn);
    }
}
