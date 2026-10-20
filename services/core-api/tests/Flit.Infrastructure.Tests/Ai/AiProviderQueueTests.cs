using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Routing;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Ai;

/// <summary>HU #13510 — orden de cola: active primero, priority, skip sin key / no capaz.</summary>
public sealed class AiProviderQueueTests
{
    private static readonly IReadOnlyList<AiProviderSlot> FullSlots =
    [
        new(AiProviderCodes.Anthropic, true, 1),
        new(AiProviderCodes.OpenAi, true, 2),
        new(AiProviderCodes.Kimi, true, 3),
        new(AiProviderCodes.DeepSeek, true, 4),
    ];

    [Fact]
    public void Build_active_primero_aunque_no_sea_rank_1()
    {
        var queue = AiProviderQueue.Build(
            AiToolCodes.Ocr,
            activeProvider: AiProviderCodes.OpenAi,
            FullSlots,
            hasApiKey: _ => true);

        queue.Should().Equal(
            AiProviderCodes.OpenAi,
            AiProviderCodes.Anthropic,
            AiProviderCodes.Kimi);
    }

    [Fact]
    public void Build_omite_sin_api_key()
    {
        var queue = AiProviderQueue.Build(
            AiToolCodes.Ocr,
            AiProviderCodes.Anthropic,
            FullSlots,
            hasApiKey: code => code is AiProviderCodes.Anthropic or AiProviderCodes.Kimi);

        queue.Should().Equal(AiProviderCodes.Anthropic, AiProviderCodes.Kimi);
    }

    [Fact]
    public void Build_ocr_omite_deepseek_aunque_tenga_key()
    {
        var queue = AiProviderQueue.Build(
            AiToolCodes.Ocr,
            AiProviderCodes.Anthropic,
            FullSlots,
            hasApiKey: _ => true);

        queue.Should().NotContain(AiProviderCodes.DeepSeek);
        queue.Should().Equal(
            AiProviderCodes.Anthropic,
            AiProviderCodes.OpenAi,
            AiProviderCodes.Kimi);
    }

    [Fact]
    public void Build_dr_flit_incluye_deepseek()
    {
        var queue = AiProviderQueue.Build(
            AiToolCodes.DrFlit,
            AiProviderCodes.Anthropic,
            FullSlots,
            hasApiKey: _ => true);

        queue.Should().Equal(
            AiProviderCodes.Anthropic,
            AiProviderCodes.OpenAi,
            AiProviderCodes.Kimi,
            AiProviderCodes.DeepSeek);
    }

    [Fact]
    public void Build_omite_disabled()
    {
        IReadOnlyList<AiProviderSlot> slots =
        [
            new(AiProviderCodes.Anthropic, Enabled: false, PriorityRank: 1),
            new(AiProviderCodes.OpenAi, Enabled: true, PriorityRank: 2),
        ];

        var queue = AiProviderQueue.Build(
            AiToolCodes.Ocr,
            AiProviderCodes.Anthropic,
            slots,
            hasApiKey: _ => true);

        queue.Should().Equal(AiProviderCodes.OpenAi);
    }

    [Fact]
    public void Build_respeta_priority_rank()
    {
        IReadOnlyList<AiProviderSlot> slots =
        [
            new(AiProviderCodes.Kimi, true, 1),
            new(AiProviderCodes.OpenAi, true, 2),
            new(AiProviderCodes.Anthropic, true, 3),
        ];

        var queue = AiProviderQueue.Build(
            AiToolCodes.Ocr,
            activeProvider: AiProviderCodes.Anthropic,
            slots,
            hasApiKey: _ => true);

        queue.Should().Equal(
            AiProviderCodes.Anthropic,
            AiProviderCodes.Kimi,
            AiProviderCodes.OpenAi);
    }

    [Fact]
    public void Build_sin_candidatos_devuelve_vacio()
    {
        var queue = AiProviderQueue.Build(
            AiToolCodes.Ocr,
            AiProviderCodes.Anthropic,
            FullSlots,
            hasApiKey: _ => false);

        queue.Should().BeEmpty();
    }

    [Fact]
    public void Build_sin_slots_usa_default()
    {
        var queue = AiProviderQueue.Build(
            AiToolCodes.DrFlit,
            AiProviderCodes.Anthropic,
            Array.Empty<AiProviderSlot>(),
            hasApiKey: code => code == AiProviderCodes.Anthropic);

        queue.Should().Equal(AiProviderCodes.Anthropic);
    }
}
