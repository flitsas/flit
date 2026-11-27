using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// Failover automático de IA contra PostgreSQL real. La alerta (<c>admin.ai_failover_alerts</c>) apunta por FK
/// a su transición, pero el modelo EF no las relaciona: en un solo SaveChanges EF insertaba la alerta primero y
/// el motor la rechazaba (23503 <c>fk_ai_failover_alerts_transition</c>). En QA, 2026-11-26: el OCR sí pasó a
/// OpenAI, pero el activo siguió en anthropic y no quedó ni transición ni alerta.
/// </summary>
public sealed class AiFailoverPersistenciaPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    [PostgresFact]
    public async Task Failover_deja_activo_transicion_y_alerta_enlazadas()
    {
        var ct = TestContext.Current.CancellationToken;
        await SembrarOcrAsync(ct);

        // Contexto con reintento, como el de producto: la transacción tiene que ir dentro de la estrategia.
        await using (var db = NewRetryingContext())
        {
            await new AiToolProviderSettingsRepository(db)
                .RecordFailoverAsync(AiToolCodes.Ocr, AiProviderCodes.Anthropic, AiProviderCodes.OpenAi, "invalid x-api-key", ct);
        }

        await using var ctx = NewContext();
        var ajustes = await ctx.AiToolProviderSettings.AsNoTracking().SingleAsync(x => x.ToolCode == AiToolCodes.Ocr, ct);
        ajustes.ActiveProvider.Should().Be(AiProviderCodes.OpenAi);

        var transicion = await ctx.AiProviderTransitions.AsNoTracking().SingleAsync(ct);
        transicion.Reason.Should().Be("failover_auto");
        transicion.FromProvider.Should().Be(AiProviderCodes.Anthropic);
        transicion.ToProvider.Should().Be(AiProviderCodes.OpenAi);
        transicion.Detail.Should().Be("invalid x-api-key");

        var alerta = await ctx.AiFailoverAlerts.AsNoTracking().SingleAsync(ct);
        alerta.TransitionId.Should().Be(transicion.Id);
        alerta.AckedAt.Should().BeNull();
    }

    [PostgresFact]
    public async Task Dos_failovers_seguidos_dejan_dos_alertas()
    {
        var ct = TestContext.Current.CancellationToken;
        await SembrarOcrAsync(ct);

        // El mismo contexto (scoped en la request) recibe los dos: lo guardado del primero no se reinserta.
        await using (var db = NewRetryingContext())
        {
            var repo = new AiToolProviderSettingsRepository(db);
            await repo.RecordFailoverAsync(AiToolCodes.Ocr, AiProviderCodes.Anthropic, AiProviderCodes.OpenAi, null, ct);
            await repo.RecordFailoverAsync(AiToolCodes.Ocr, AiProviderCodes.OpenAi, AiProviderCodes.Kimi, null, ct);
        }

        await using var ctx = NewContext();
        (await ctx.AiToolProviderSettings.AsNoTracking().SingleAsync(x => x.ToolCode == AiToolCodes.Ocr, ct))
            .ActiveProvider.Should().Be(AiProviderCodes.Kimi);
        (await ctx.AiProviderTransitions.CountAsync(ct)).Should().Be(2);
        (await ctx.AiFailoverAlerts.CountAsync(ct)).Should().Be(2);
    }

    [PostgresFact]
    public async Task Regreso_automatico_vuelve_al_principal_con_transicion_y_sin_alerta()
    {
        var ct = TestContext.Current.CancellationToken;
        await SembrarOcrAsync(ct);

        await using (var db = NewRetryingContext())
        {
            var repo = new AiToolProviderSettingsRepository(db);
            await repo.RecordFailoverAsync(AiToolCodes.Ocr, AiProviderCodes.Anthropic, AiProviderCodes.OpenAi, null, ct);
            await repo.RecordAutoReturnAsync(AiToolCodes.Ocr, AiProviderCodes.OpenAi, AiProviderCodes.Anthropic, ct);
        }

        await using var ctx = NewContext();
        (await ctx.AiToolProviderSettings.AsNoTracking().SingleAsync(x => x.ToolCode == AiToolCodes.Ocr, ct))
            .ActiveProvider.Should().Be(AiProviderCodes.Anthropic);
        var transiciones = await ctx.AiProviderTransitions.AsNoTracking().OrderBy(t => t.OccurredAt).ToListAsync(ct);
        transiciones.Select(t => t.Reason).Should().Equal("failover_auto", "auto_return");
        (await ctx.AiFailoverAlerts.CountAsync(ct)).Should().Be(1, "solo el failover deja alerta");
    }

    [PostgresFact]
    public async Task Regreso_automatico_no_pisa_un_cambio_hecho_entretanto()
    {
        var ct = TestContext.Current.CancellationToken;
        await SembrarOcrAsync(ct, activo: AiProviderCodes.Kimi);

        // El router creía que el activo era openai, pero alguien ya lo había puesto en kimi.
        await using (var db = NewRetryingContext())
        {
            await new AiToolProviderSettingsRepository(db)
                .RecordAutoReturnAsync(AiToolCodes.Ocr, AiProviderCodes.OpenAi, AiProviderCodes.Anthropic, ct);
        }

        await using var ctx = NewContext();
        (await ctx.AiToolProviderSettings.AsNoTracking().SingleAsync(x => x.ToolCode == AiToolCodes.Ocr, ct))
            .ActiveProvider.Should().Be(AiProviderCodes.Kimi);
        (await ctx.AiProviderTransitions.CountAsync(ct)).Should().Be(0);
    }

    private async Task SembrarOcrAsync(CancellationToken ct, string activo = AiProviderCodes.Anthropic)
    {
        await using var ctx = NewContext();
        ctx.AiToolProviderSettings.Add(new AiToolProviderSettingsRow
        {
            Id = Guid.CreateVersion7(),
            ToolCode = AiToolCodes.Ocr,
            ActiveProvider = activo,
            AutoReturnToPrimary = true,
            ProvidersJson = """[{"enabled":true,"priorityRank":1,"providerCode":"anthropic"},{"enabled":true,"priorityRank":2,"providerCode":"openai"},{"enabled":true,"priorityRank":3,"providerCode":"kimi"}]""",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(ct);
    }
}
