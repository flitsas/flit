using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Domain.Plataforma.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Ai;

/// <summary>
/// ADR-0091 / contrato §4 (DELTA P1=a) — <c>diagnostico_dictamen</c> es una herramienta conocida que solo admite
/// <c>anthropic</c>: la matriz de capacidades, la pantalla («No aplica» para los demás), guardar (no se puede habilitar un
/// proveedor no capaz) y activar.
/// </summary>
/// <remarks>Uso de ejemplo: <c>AiProviderCapabilities.SupportsTool("diagnostico_dictamen", "anthropic")</c>.</remarks>
public sealed class AiDiagnosticoDictamenCatalogoTests
{
    private const string Tool = AiToolCodes.DiagnosticoDictamen;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AiToolProviderSettings Fila(string tool = Tool, bool deepseekHabilitado = false) => new(
        Guid.CreateVersion7(), tool, AiProviderCodes.Anthropic, true,
        [
            new AiProviderSlot(AiProviderCodes.Anthropic, true, 1),
            new AiProviderSlot(AiProviderCodes.OpenAi, false, 2),
            new AiProviderSlot(AiProviderCodes.Kimi, false, 3),
            new AiProviderSlot(AiProviderCodes.DeepSeek, deepseekHabilitado, 4),
        ],
        RowVersion: 3, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null, UpdatedBy: null);

    private static List<AiProviderSlotCommand> Slots(bool openAi = false, bool deepseek = false) =>
    [
        new(AiProviderCodes.Anthropic, true, 1),
        new(AiProviderCodes.OpenAi, openAi, 2),
        new(AiProviderCodes.Kimi, false, 3),
        new(AiProviderCodes.DeepSeek, deepseek, 4),
    ];

    [Theory]
    [InlineData("diagnostico_dictamen")]
    [InlineData("DIAGNOSTICO_DICTAMEN")]
    [InlineData(" diagnostico_dictamen ")]
    public void ElCatalogo_ConoceYNormalizaLaHerramienta(string codigo)
    {
        AiToolCodes.IsKnown(codigo.Trim()).Should().BeTrue();
        AiToolCodes.Normalize(codigo).Should().Be(Tool);
    }

    [Theory]
    [InlineData(AiProviderCodes.Anthropic, true)]
    [InlineData(AiProviderCodes.OpenAi, false)]
    [InlineData(AiProviderCodes.Kimi, false)]
    [InlineData(AiProviderCodes.DeepSeek, false)]
    public void Capacidades_DiagnosticoDictamen_SoloAnthropic(string proveedor, bool esperado) =>
        AiProviderCapabilities.SupportsTool(Tool, proveedor).Should().Be(esperado);

    [Fact]
    public void Capacidades_DeOcrYDrFlit_NoCambian()
    {
        AiProviderCapabilities.SupportsTool(AiToolCodes.Ocr, AiProviderCodes.OpenAi).Should().BeTrue();
        AiProviderCapabilities.SupportsTool(AiToolCodes.Ocr, AiProviderCodes.DeepSeek).Should().BeFalse();
        AiProviderCapabilities.SupportsTool(AiToolCodes.DrFlit, AiProviderCodes.DeepSeek).Should().BeTrue();
    }

    [Fact]
    public async Task LaPantalla_MuestraLosOtrosComoNoAplica()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns(Fila());
        var probe = Substitute.For<IAiApiKeyEnvironmentProbe>();

        var view = await new GetAiToolSettingsHandler(repo, probe).HandleAsync(Tool, Ct);

        view.Should().NotBeNull();
        view!.Providers.Single(p => p.ProviderCode == AiProviderCodes.Anthropic).Capable.Should().BeTrue();
        view.Providers.Where(p => p.ProviderCode != AiProviderCodes.Anthropic).Should().OnlyContain(p => !p.Capable);
    }

    [Fact]
    public async Task Guardar_HabilitandoUnProveedorNoCapaz_SeRechaza_SinEscribir()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns(Fila());
        var audit = Substitute.For<IAdminAuditWriter>();
        var handler = new SaveAiToolSettingsHandler(repo, Substitute.For<IAiApiKeyEnvironmentProbe>(), audit);

        var r = await handler.HandleAsync(Tool, new SaveAiToolSettingsCommand(true, Slots(openAi: true), Guid.CreateVersion7()), Ct);

        r.IsValid.Should().BeFalse();
        r.ErrorCode.Should().Be("proveedor_no_aplica");
        await repo.DidNotReceiveWithAnyArgs().SaveSettingsAsync(default!, default, default!, default, Ct);
        await audit.DidNotReceiveWithAnyArgs().WriteAsync(default!, Ct);
    }

    [Fact]
    public async Task Guardar_SoloConAnthropicHabilitado_Pasa()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns(Fila());
        repo.SaveSettingsAsync(Tool, Arg.Any<bool>(), Arg.Any<IReadOnlyList<AiProviderSlot>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(Fila());
        var handler = new SaveAiToolSettingsHandler(repo, Substitute.For<IAiApiKeyEnvironmentProbe>(), Substitute.For<IAdminAuditWriter>());

        var r = await handler.HandleAsync(Tool, new SaveAiToolSettingsCommand(false, Slots(), Guid.CreateVersion7()), Ct);

        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Guardar_Ocr_ConDeepseekYaHabilitadoDeLaSemilla_SigueSinRechazo()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(AiToolCodes.Ocr, Arg.Any<CancellationToken>()).Returns(Fila(AiToolCodes.Ocr, deepseekHabilitado: true));
        repo.SaveSettingsAsync(AiToolCodes.Ocr, Arg.Any<bool>(), Arg.Any<IReadOnlyList<AiProviderSlot>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Fila(AiToolCodes.Ocr, deepseekHabilitado: true));
        var handler = new SaveAiToolSettingsHandler(repo, Substitute.For<IAiApiKeyEnvironmentProbe>(), Substitute.For<IAdminAuditWriter>());

        var r = await handler.HandleAsync(AiToolCodes.Ocr, new SaveAiToolSettingsCommand(true, Slots(deepseek: true), null), Ct);

        r.IsValid.Should().BeTrue("la semilla de ocr trae deepseek habilitado: mantenerlo no es «habilitar»");
    }

    [Fact]
    public async Task Activar_UnProveedorNoCapaz_DevuelveProviderNotCapable()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns(Fila());
        var probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
        probe.HasApiKey(Arg.Any<string>()).Returns(true);

        var r = await new ActivateAiProviderHandler(repo, probe, Substitute.For<IAdminAuditWriter>())
            .HandleAsync(Tool, new ActivateAiProviderCommand(AiProviderCodes.OpenAi, null, Guid.CreateVersion7()), Ct);

        r.Outcome.Should().Be(ActivateAiProviderOutcome.ProviderNotCapable);
    }
}
