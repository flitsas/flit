using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Routing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Ai;

/// <summary>
/// <c>auto_return_to_primary</c>: tras un failover la herramienta queda en un respaldo. Antes se quedaba ahí
/// hasta que alguien la cambiara a mano (QA, 2026-11-26: Anthropic volvió y el OCR siguió en OpenAI). Ahora,
/// como mucho una vez por intervalo, el router intenta primero con el principal; si responde, vuelve a ser
/// el activo.
/// </summary>
public sealed class AiAutoReturnRouterTests
{
    private DateTimeOffset _ahora = new(2026, 11, 26, 23, 0, 0, TimeSpan.Zero);

    private readonly IAiToolProviderSettingsRepository _repo = Substitute.For<IAiToolProviderSettingsRepository>();
    private readonly IAiVisionProvider _anthropic = Proveedor(AiProviderCodes.Anthropic);
    private readonly IAiVisionProvider _openai = Proveedor(AiProviderCodes.OpenAi);
    private readonly AiAutoReturnGate _gate;

    public AiAutoReturnRouterTests() => _gate = new AiAutoReturnGate(TimeSpan.FromMinutes(5), () => _ahora);

    private static IAiVisionProvider Proveedor(string codigo)
    {
        var p = Substitute.For<IAiVisionProvider>();
        p.ProviderCode.Returns(codigo);
        return p;
    }

    private static void Responde(IAiVisionProvider p, AiProviderCallResult r) =>
        p.SendVisionAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<AiVisionPerfil>())
            .Returns(r);

    private Task<int> LlamadasA(IAiVisionProvider p) =>
        Task.FromResult(p.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAiVisionProvider.SendVisionAsync)));

    private void Ajustes(string activo, bool autoReturn = true) =>
        _repo.GetByToolCodeAsync(AiToolCodes.Ocr, Arg.Any<CancellationToken>()).Returns(new AiToolProviderSettings(
            Guid.CreateVersion7(),
            AiToolCodes.Ocr,
            activo,
            autoReturn,
            [new AiProviderSlot(AiProviderCodes.Anthropic, true, 1), new AiProviderSlot(AiProviderCodes.OpenAi, true, 2)],
            RowVersion: 1,
            CreatedAt: _ahora,
            UpdatedAt: null,
            UpdatedBy: null));

    private Task<AiRouterResult> Ocr()
    {
        var keys = Substitute.For<IAiApiKeyEnvironmentProbe>();
        keys.HasApiKey(Arg.Any<string>()).Returns(true);
        var router = new AiProviderRouter(
            _repo, keys, [_anthropic, _openai], Array.Empty<IAiChatProvider>(),
            Substitute.For<IAdminAuditWriter>(), NullLogger<AiProviderRouter>.Instance,
            autoReturnGate: _gate);
        return router.InvokeVisionAsync(AiToolCodes.Ocr, "AAAA", "image/png", "P", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task En_respaldo_y_el_principal_responde_vuelve_al_principal()
    {
        Ajustes(activo: AiProviderCodes.OpenAi);
        Responde(_anthropic, AiProviderCallResult.Success("{}"));
        Responde(_openai, AiProviderCallResult.Success("{}"));

        var r = await Ocr();

        r.ProviderCode.Should().Be(AiProviderCodes.Anthropic);
        (await LlamadasA(_openai)).Should().Be(0, "el principal respondió: no hace falta el respaldo");
        await _repo.Received(1).RecordAutoReturnAsync(
            AiToolCodes.Ocr, AiProviderCodes.OpenAi, AiProviderCodes.Anthropic, Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().RecordFailoverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Principal_sigue_caido_responde_el_respaldo_sin_registrar_failover()
    {
        Ajustes(activo: AiProviderCodes.OpenAi);
        Responde(_anthropic, AiProviderCallResult.Fail(401, "authentication_error"));
        Responde(_openai, AiProviderCallResult.Success("{}"));

        var r = await Ocr();

        r.ProviderCode.Should().Be(AiProviderCodes.OpenAi);
        (await LlamadasA(_anthropic)).Should().Be(1);
        await _repo.DidNotReceive().RecordFailoverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().RecordAutoReturnAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prueba_el_principal_como_mucho_una_vez_por_intervalo()
    {
        Ajustes(activo: AiProviderCodes.OpenAi);
        Responde(_anthropic, AiProviderCallResult.Fail(401, "authentication_error"));
        Responde(_openai, AiProviderCallResult.Success("{}"));

        await Ocr();
        _ahora += TimeSpan.FromMinutes(4);
        await Ocr();
        await Ocr();
        (await LlamadasA(_anthropic)).Should().Be(1, "dentro del intervalo se va directo al respaldo");
        (await LlamadasA(_openai)).Should().Be(3);

        _ahora += TimeSpan.FromMinutes(1);
        await Ocr();
        (await LlamadasA(_anthropic)).Should().Be(2, "cumplido el intervalo se vuelve a probar");
    }

    [Fact]
    public async Task Sin_auto_return_nunca_prueba_el_principal()
    {
        Ajustes(activo: AiProviderCodes.OpenAi, autoReturn: false);
        Responde(_anthropic, AiProviderCallResult.Success("{}"));
        Responde(_openai, AiProviderCallResult.Success("{}"));

        (await Ocr()).ProviderCode.Should().Be(AiProviderCodes.OpenAi);
        (await LlamadasA(_anthropic)).Should().Be(0);
    }

    [Fact]
    public async Task Con_el_principal_activo_no_hay_nada_que_probar()
    {
        Ajustes(activo: AiProviderCodes.Anthropic);
        Responde(_anthropic, AiProviderCallResult.Success("{}"));

        (await Ocr()).ProviderCode.Should().Be(AiProviderCodes.Anthropic);
        await _repo.DidNotReceive().RecordAutoReturnAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Justo_despues_de_un_failover_no_vuelve_a_probar_el_principal()
    {
        // Failover: anthropic cae y responde openai.
        Ajustes(activo: AiProviderCodes.Anthropic);
        Responde(_anthropic, AiProviderCallResult.Fail(401, "authentication_error"));
        Responde(_openai, AiProviderCallResult.Success("{}"));
        await Ocr();
        await _repo.Received(1).RecordFailoverAsync(
            AiToolCodes.Ocr, AiProviderCodes.Anthropic, AiProviderCodes.OpenAi, Arg.Any<string?>(), Arg.Any<CancellationToken>());

        // La siguiente petición ya ve openai activo: no debe pagar otro 401 enseguida.
        Ajustes(activo: AiProviderCodes.OpenAi);
        _ahora += TimeSpan.FromSeconds(30);
        await Ocr();

        (await LlamadasA(_anthropic)).Should().Be(1, "solo el intento del failover");
    }
}
