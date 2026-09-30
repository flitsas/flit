using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.Chat;
using Flit.DrFlit.Application.Manual;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.DrFlit.Application.Tests.Chat;

/// <summary>
/// HU #12919 — ensamblador del chat: tope diario, manual, LLM, validación y degradación.
/// Uso de ejemplo:
/// <code>
/// var assistant = new DrFlitAssistant(model, counter, catalogProvider, settings, logger);
/// var result = await assistant.AskAsync(new DrFlitChatRequest(tenantId, userId, "¿cómo creo un trámite?", []), ct);
/// </code>
/// </summary>
public sealed class DrFlitAssistantTests
{
    private static readonly Guid TenantA = Guid.Parse("0199a000-0000-7000-8000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("0199a000-0000-7000-8000-00000000000b");
    private static readonly Guid User = Guid.Parse("0199a000-0000-7000-8000-000000000001");

    private static readonly DrFlitManualCatalog Catalog = new(
    [
        new DrFlitManualArticle("crear-tramite", "Crear un trámite", "/manual/crear-tramite", "Abre el wizard."),
    ]);

    private const string ValidDuda = """{"intent":"duda","reply":"Abre el wizard.","citedSlugs":["crear-tramite"]}""";

    private readonly IDrFlitChatModel _model = Substitute.For<IDrFlitChatModel>();
    private readonly IDrFlitUsageCounter _counter = Substitute.For<IDrFlitUsageCounter>();
    private readonly IDrFlitManualCatalogProvider _catalog = Substitute.For<IDrFlitManualCatalogProvider>();
    private readonly IDrFlitChatSettings _settings = Substitute.For<IDrFlitChatSettings>();

    public DrFlitAssistantTests()
    {
        _settings.DailyMessageLimit.Returns(30);
        _settings.Enabled.Returns(true);
        _catalog.GetCatalog().Returns(Catalog);
        _model.CompleteAsync(default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(new DrFlitModelCallResult(DrFlitModelCallStatus.Ok, ValidDuda, new DrFlitTokenUsage(1, 2, 3, 4)));
    }

    private DrFlitAssistant Assistant() => new(_model, _counter, _catalog, _settings, NullLogger<DrFlitAssistant>.Instance);

    private static DrFlitChatRequest Ask(Guid? tenant = null, IReadOnlyList<DrFlitTurn>? history = null) =>
        new(tenant ?? TenantA, User, "¿cómo creo un trámite?", history ?? []);

    private void CounterAllows(int usedAfter) =>
        _counter.TryConsumeAsync(default, default, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(new DrFlitUsageConsumption(true, usedAfter));

    // ── AC1 — bajo el tope: consume, llama al LLM y reporta el uso ───────────────────────

    [Fact]
    public async Task AC1_BajoElTope_ConsumeLlamaAlLlmYDevuelveUso()
    {
        CounterAllows(usedAfter: 7);

        var result = await Assistant().AskAsync(Ask(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(DrFlitChatStatus.Ok);
        result.Intent.Should().Be(DrFlitIntent.Duda);
        result.Reply.Should().Be("Abre el wizard.");
        result.Citations.Select(c => c.Slug).Should().Equal("crear-tramite");
        result.MessagesUsedToday.Should().Be(7);
        result.DailyLimit.Should().Be(30);
        await _counter.Received(1).TryConsumeAsync(TenantA, User, 30, TestContext.Current.CancellationToken);
        await _model.Received(1).CompleteAsync(
            Catalog.GetSystemPrompt(DrFlitManualProfile.Gestor), Arg.Any<IReadOnlyList<DrFlitTurn>>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC1_ElTopeSaleDeLaConfiguracion()
    {
        _settings.DailyMessageLimit.Returns(5);
        CounterAllows(usedAfter: 1);

        var result = await Assistant().AskAsync(Ask(), TestContext.Current.CancellationToken);

        result.DailyLimit.Should().Be(5);
        await _counter.Received(1).TryConsumeAsync(TenantA, User, 5, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC1_ConversacionEmpiezaEnUsuarioYTerminaEnElMensajeNuevo()
    {
        CounterAllows(1);
        IReadOnlyList<DrFlitTurn>? sent = null;
        _model.CompleteAsync(default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(ci =>
            {
                sent = ci.ArgAt<IReadOnlyList<DrFlitTurn>>(1);
                return new DrFlitModelCallResult(DrFlitModelCallStatus.Ok, ValidDuda);
            });
        var history = new[]
        {
            new DrFlitTurn(DrFlitTurnRole.Assistant, "¡Hola! Soy DR. FLIT."),
            new DrFlitTurn(DrFlitTurnRole.User, "hola"),
            new DrFlitTurn(DrFlitTurnRole.Assistant, "¿en qué te ayudo?"),
        };

        await Assistant().AskAsync(Ask(history: history), TestContext.Current.CancellationToken);

        sent!.Select(t => t.Role).Should().Equal(DrFlitTurnRole.User, DrFlitTurnRole.Assistant, DrFlitTurnRole.User);
        sent![^1].Text.Should().Be("¿cómo creo un trámite?");
    }

    // ── AC2 — tope alcanzado: no llama al LLM ───────────────────────────────────────────

    [Fact]
    public async Task AC2_TopeAlcanzado_NoLlamaAlLlmYDevuelveRateLimited()
    {
        _counter.TryConsumeAsync(default, default, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(new DrFlitUsageConsumption(false, 30));

        var result = await Assistant().AskAsync(Ask(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(DrFlitChatStatus.RateLimited);
        result.MessagesUsedToday.Should().Be(result.DailyLimit).And.Be(30);
        result.Intent.Should().BeNull();
        result.Citations.Should().BeEmpty();
        result.Reply.Should().Be(DrFlitAssistant.RateLimitedReply);
        await _model.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    // ── AC3 — aislamiento: el contador se consulta por el tenant de la petición ────────

    [Fact]
    public async Task AC3_ElContadorSeConsultaPorElTenantDeLaPeticion()
    {
        CounterAllows(1);

        await Assistant().AskAsync(Ask(TenantA), TestContext.Current.CancellationToken);
        await Assistant().AskAsync(Ask(TenantB), TestContext.Current.CancellationToken);

        await _counter.Received(1).TryConsumeAsync(TenantA, User, 30, TestContext.Current.CancellationToken);
        await _counter.Received(1).TryConsumeAsync(TenantB, User, 30, TestContext.Current.CancellationToken);
    }

    // ── Degradación (§7.1) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LlmApagado_DegradaSinConsumirTope()
    {
        _settings.Enabled.Returns(false);
        _counter.GetUsedTodayAsync(TenantA, User, TestContext.Current.CancellationToken).Returns(4);

        var result = await Assistant().AskAsync(Ask(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(DrFlitChatStatus.Degraded);
        result.MessagesUsedToday.Should().Be(4);
        result.Reply.Should().Be(DrFlitAssistant.DegradedReply);
        await _counter.DidNotReceiveWithAnyArgs().TryConsumeAsync(default, default, default, TestContext.Current.CancellationToken);
        await _model.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SinCatalogo_DegradaSinConsumirTope()
    {
        _catalog.GetCatalog().Returns((DrFlitManualCatalog?)null);

        var result = await Assistant().AskAsync(Ask(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(DrFlitChatStatus.Degraded);
        await _counter.DidNotReceiveWithAnyArgs().TryConsumeAsync(default, default, default, TestContext.Current.CancellationToken);
        await _model.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(DrFlitModelCallStatus.Failed, null)]
    [InlineData(DrFlitModelCallStatus.Disabled, null)]
    [InlineData(DrFlitModelCallStatus.Ok, "no soy JSON")]
    [InlineData(DrFlitModelCallStatus.Ok, """{"intent":"duda","reply":"Según X.","citedSlugs":["inventado"]}""")]
    public async Task ModeloFallaOFueraDeContrato_Degrada(DrFlitModelCallStatus status, string? text)
    {
        CounterAllows(3);
        _model.CompleteAsync(default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(new DrFlitModelCallResult(status, text));

        var result = await Assistant().AskAsync(Ask(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(DrFlitChatStatus.Degraded);
        result.Intent.Should().BeNull();
        result.Citations.Should().BeEmpty();
        result.MessagesUsedToday.Should().Be(3, "el mensaje ya se consumió: pudo haber costo en el proveedor");
    }

    [Fact]
    public async Task HU12927_GestionConTarget_LlegaAlResultado()
    {
        CounterAllows(2);
        _model.CompleteAsync(default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(new DrFlitModelCallResult(
                DrFlitModelCallStatus.Ok, """{"intent":"gestion","reply":"Te llevo a buscar por placa.","gestionTarget":"placa"}"""));

        var result = await Assistant().AskAsync(Ask(), TestContext.Current.CancellationToken);

        result.Intent.Should().Be(DrFlitIntent.Gestion);
        result.GestionTarget.Should().Be("placa");
    }

    [Fact]
    public void StatusWire_CoincideConElContrato()
    {
        DrFlitChatStatus.Ok.ToWire().Should().Be("ok");
        DrFlitChatStatus.Degraded.ToWire().Should().Be("degraded");
        DrFlitChatStatus.RateLimited.ToWire().Should().Be("rate_limited");
    }

    [Fact]
    public void Catalogo_IndexaPorSlugYArmaElPromptUnaVez()
    {
        var catalog = new DrFlitManualCatalog(
        [
            new DrFlitManualArticle("a", "A", "/manual/a", "x"),
            new DrFlitManualArticle("a", "A duplicado", "/manual/a", "y"),
        ]);

        catalog.BySlug.Should().ContainKey("a").WhoseValue.Title.Should().Be("A");
        // HU #13023 AC2 — misma instancia por perfil: el bloque idéntico entre llamadas es lo que hace
        // que la caché de Anthropic registre cache_read en la segunda llamada de la variante.
        catalog.GetSystemPrompt(DrFlitManualProfile.Gestor).Should().BeSameAs(catalog.GetSystemPrompt(DrFlitManualProfile.Gestor));
        catalog.GetSystemPrompt(DrFlitManualProfile.Gestor).Instructions.Should().Be(DrFlitPromptBuilder.Instructions);
    }

    // ── HU #13023 — manual acotado a la audiencia del perfil ─────────────────────────────

    private static readonly DrFlitManualCatalog CatalogoPorAudiencia = new(
    [
        new DrFlitManualArticle("comun", "Común", "/manual/comun", "para todos", Audience: "Todos"),
        new DrFlitManualArticle("solo-gestor", "Solo gestor", "/manual/solo-gestor", "wizard", Audience: "Gestor"),
        new DrFlitManualArticle("solo-ot", "Solo OT", "/manual/solo-ot", "bandeja", Audience: "Organismo de Tránsito"),
        new DrFlitManualArticle("solo-admin", "Solo admin", "/manual/solo-admin", "consola", Audience: "Admin de Compañía"),
        new DrFlitManualArticle("solo-sa", "Solo SA", "/manual/solo-sa", "plataforma", Audience: "Super Admin"),
    ]);

    [Fact]
    public void HU13023_AC1_VarianteGestor_SoloLlevaGestorYTodos()
    {
        var manual = CatalogoPorAudiencia.GetSystemPrompt(DrFlitManualProfile.Gestor).ManualBlock;

        manual.Should().Contain("slug: comun").And.Contain("slug: solo-gestor");
        manual.Should().NotContain("slug: solo-ot").And.NotContain("slug: solo-admin").And.NotContain("slug: solo-sa");
    }

    [Fact]
    public void HU13023_AC1_VarianteOt_NoVeElFlujoDelGestor()
    {
        var manual = CatalogoPorAudiencia.GetSystemPrompt(DrFlitManualProfile.OtAdmin).ManualBlock;

        manual.Should().Contain("slug: comun").And.Contain("slug: solo-ot");
        manual.Should().NotContain("slug: solo-gestor");
    }

    [Fact]
    public void HU13023_SuperAdmin_VeTodasLasAudiencias()
    {
        var manual = CatalogoPorAudiencia.GetSystemPrompt(DrFlitManualProfile.SuperAdmin).ManualBlock;

        manual.Should().Contain("slug: comun").And.Contain("slug: solo-gestor")
            .And.Contain("slug: solo-ot").And.Contain("slug: solo-admin").And.Contain("slug: solo-sa");
    }

    [Fact]
    public async Task HU13023_AC3_CitaDeOtraAudiencia_NoFalla_SeValidaContraElCatalogoCompleto()
    {
        // El modelo (variante OT) cita un slug que existe en el catálogo completo pero es de audiencia
        // Gestor: la respuesta NO degrada por eso.
        _catalog.GetCatalog().Returns(CatalogoPorAudiencia);
        _model.CompleteAsync(default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(new DrFlitModelCallResult(
                DrFlitModelCallStatus.Ok,
                """{"intent":"duda","reply":"ok","citedSlugs":["solo-gestor"]}""",
                new DrFlitTokenUsage(1, 2, 3, 4)));
        CounterAllows(usedAfter: 1);

        var request = new DrFlitChatRequest(TenantA, User, "¿cómo?", [], DrFlitManualProfile.OtAdmin);
        var result = await Assistant().AskAsync(request, TestContext.Current.CancellationToken);

        result.Status.Should().Be(DrFlitChatStatus.Ok);
        result.Citations.Select(c => c.Slug).Should().Equal("solo-gestor");
        await _model.Received(1).CompleteAsync(
            CatalogoPorAudiencia.GetSystemPrompt(DrFlitManualProfile.OtAdmin),
            Arg.Any<IReadOnlyList<DrFlitTurn>>(),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public void HU13023_AC4_PerfilSinArticulosPropios_LlevaAlMenosTodos()
    {
        var catalog = new DrFlitManualCatalog(
        [
            new DrFlitManualArticle("comun", "Común", "/manual/comun", "para todos", Audience: "Todos"),
            new DrFlitManualArticle("solo-gestor", "Solo gestor", "/manual/solo-gestor", "wizard", Audience: "Gestor"),
        ]);

        var manual = catalog.GetSystemPrompt(DrFlitManualProfile.OtAdmin).ManualBlock;

        manual.Should().Contain("slug: comun", "un perfil sin artículos propios conserva los de Todos y el chat sigue operativo");
        manual.Should().NotContain("slug: solo-gestor");
    }

    [Fact]
    public void HU13023_ArticuloSinAudiencia_EsVisibleParaTodosLosPerfiles()
    {
        var catalog = new DrFlitManualCatalog(
        [
            new DrFlitManualArticle("legado", "Legado", "/manual/legado", "sin audiencia declarada"),
        ]);

        foreach (var perfil in Enum.GetValues<DrFlitManualProfile>())
            catalog.GetSystemPrompt(perfil).ManualBlock.Should().Contain("slug: legado");
    }
}
