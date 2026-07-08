using Flit.Infrastructure.Analytics.Scheduling;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Scheduling;

/// <summary>
/// Reportes 2.0 HU-D — decisión de disparo de alertas (§8): operadores gt/gte/lt/lte,
/// NO disparo bajo umbral y respeto del cooldown (vigente NO dispara, vencido SÍ).
/// </summary>
public sealed class AlertRuleEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("gt", 30, 25, true)]
    [InlineData("gt", 25, 25, false)]
    [InlineData("gte", 25, 25, true)]
    [InlineData("gte", 24.9, 25, false)]
    [InlineData("lt", 3, 5, true)]
    [InlineData("lt", 5, 5, false)]
    [InlineData("lte", 5, 5, true)]
    [InlineData("lte", 5.1, 5, false)]
    public void Matches_evalua_cada_operador(string op, double value, double threshold, bool expected)
    {
        AlertRuleEvaluator.Matches(op, (decimal)value, (decimal)threshold).Should().Be(expected);
    }

    [Fact]
    public void Operador_desconocido_nunca_dispara()
    {
        AlertRuleEvaluator.Matches("eq", 10, 10).Should().BeFalse();
    }

    [Fact]
    public void No_dispara_bajo_el_umbral()
    {
        AlertRuleEvaluator.ShouldTrigger("gt", value: 10m, threshold: 25m,
            lastTriggeredAtUtc: null, cooldownMinutes: 240, Now).Should().BeFalse();
    }

    [Fact]
    public void Dispara_sobre_el_umbral_sin_disparos_previos()
    {
        AlertRuleEvaluator.ShouldTrigger("gt", 31.2m, 25m, null, 240, Now).Should().BeTrue();
    }

    [Fact]
    public void Cooldown_vigente_NO_dispara_aunque_cumpla_el_umbral()
    {
        var lastTriggered = Now.AddMinutes(-30); // cooldown de 240 min aún vigente

        AlertRuleEvaluator.ShouldTrigger("gt", 31.2m, 25m, lastTriggered, 240, Now).Should().BeFalse();
        AlertRuleEvaluator.IsCooldownActive(lastTriggered, 240, Now).Should().BeTrue();
    }

    [Fact]
    public void Cooldown_vencido_SI_dispara()
    {
        var lastTriggered = Now.AddMinutes(-241);

        AlertRuleEvaluator.ShouldTrigger("gt", 31.2m, 25m, lastTriggered, 240, Now).Should().BeTrue();
        AlertRuleEvaluator.IsCooldownActive(lastTriggered, 240, Now).Should().BeFalse();
    }

    [Fact]
    public void Cooldown_exactamente_en_el_limite_se_considera_vencido()
    {
        var lastTriggered = Now.AddMinutes(-240);

        AlertRuleEvaluator.IsCooldownActive(lastTriggered, 240, Now).Should().BeFalse();
    }
}
