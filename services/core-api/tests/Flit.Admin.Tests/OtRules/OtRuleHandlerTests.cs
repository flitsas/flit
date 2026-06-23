using System.Text.Json;
using Flit.Admin.Application.OtRules;
using Flit.Admin.Application.OtRules.CreateOtRule;
using Flit.Admin.Application.OtRules.UpdateOtRule;
using Flit.Admin.Domain.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtRules;

/// <summary>Tests motor de reglas OT AND/OR (HU #10221) — AC1–AC6.</summary>
public sealed class OtRuleHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ChangedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task AC1_CreateRule_PersistsInFeatureFlagsWithRulePrefixAndEnabled()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);

        var handler = new CreateOtRuleHandler(new OtRuleRepository(ctx));
        var result = await handler.HandleAsync(new CreateOtRuleCommand
        {
            TenantId = TenantA,
            CreatedBy = ChangedBy,
            Request = new CreateOtRuleRequest
            {
                Name = "Bloqueo por deuda",
                Conditions =
                [
                    new OtRuleConditionRequest
                    {
                        Field = "deuda_pendiente",
                        Op = OtRuleConstants.OpEquals,
                        Value = JsonDocument.Parse("true").RootElement,
                    },
                    new OtRuleConditionRequest
                    {
                        Field = "tipo_tramite",
                        Op = OtRuleConstants.OpIn,
                        Value = JsonDocument.Parse("[\"matricula\"]").RootElement,
                    },
                ],
                Logic = OtRuleConstants.LogicAnd,
                Action = new OtRuleActionRequest { Type = OtRuleConstants.ActionBlock },
            },
        });

        result.Status.Should().Be(CreateOtRuleStatus.Created);
        result.Rule!.IsEnabled.Should().BeTrue();
        result.Rule.Name.Should().Be("Bloqueo por deuda");

        await using var verify = NewContext(db);
        var entity = await verify.OtFeatureFlags.SingleAsync();
        entity.FlagKey.Should().StartWith(OtRuleConstants.FlagKeyPrefix);
        entity.IsEnabled.Should().BeTrue();
        entity.Config.Should().Contain("Bloqueo por deuda");
        entity.Config.Should().Contain("bloquear");
    }

    [Fact]
    public void AC2_EvaluateAndRule_PositiveMatchReturnsBlockAction()
    {
        var rule = BuildDebtBlockRule(isEnabled: true);
        var context = BuildContext(
            ("deuda_pendiente", JsonDocument.Parse("true").RootElement),
            ("tipo_tramite", JsonDocument.Parse("\"matricula\"").RootElement));

        var result = OtRuleEvaluator.Evaluate(rule, context);

        result.Matched.Should().BeTrue();
        result.ActionType.Should().Be(OtRuleConstants.ActionBlock);
    }

    [Fact]
    public void AC3_EvaluateAndRule_NegativeWhenOneConditionFails()
    {
        var rule = BuildDebtBlockRule(isEnabled: true);
        var context = BuildContext(
            ("deuda_pendiente", JsonDocument.Parse("false").RootElement),
            ("tipo_tramite", JsonDocument.Parse("\"matricula\"").RootElement));

        var result = OtRuleEvaluator.Evaluate(rule, context);

        result.Matched.Should().BeFalse();
        result.ActionType.Should().BeNull();
    }

    [Fact]
    public async Task AC4_DisableRule_HotSwapExcludesFromEvaluation()
    {
        var db = NewDbName();
        var ruleId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.OtFeatureFlags.Add(new OtFeatureFlagEntity
            {
                Id = ruleId,
                TenantId = TenantA,
                FlagKey = $"{OtRuleConstants.FlagKeyPrefix}{ruleId}",
                IsEnabled = true,
                Config = """{"name":"Bloqueo por deuda","conditions":[{"field":"deuda_pendiente","op":"eq","value":true}],"logic":"AND","action":{"type":"bloquear"}}""",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext(db);
        var updateHandler = new UpdateOtRuleHandler(new OtRuleRepository(ctx));
        var updateResult = await updateHandler.HandleAsync(new UpdateOtRuleCommand
        {
            TenantId = TenantA,
            RuleId = ruleId,
            ChangedBy = ChangedBy,
            Request = new UpdateOtRuleRequest { IsEnabled = false },
        });

        updateResult.Status.Should().Be(UpdateOtRuleStatus.Updated);
        updateResult.Rule!.IsEnabled.Should().BeFalse();

        var repo = new OtRuleRepository(ctx);
        var enabled = await repo.ListEnabledByTenantAsync(TenantA);
        enabled.Should().BeEmpty();
    }

    [Fact]
    public async Task AC5_CreateSpecialQueueRule_PersistsQueueNameInConfig()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);

        var handler = new CreateOtRuleHandler(new OtRuleRepository(ctx));
        var result = await handler.HandleAsync(new CreateOtRuleCommand
        {
            TenantId = TenantA,
            Request = new CreateOtRuleRequest
            {
                Name = "Cola prioritaria",
                Conditions =
                [
                    new OtRuleConditionRequest
                    {
                        Field = "prioridad",
                        Op = OtRuleConstants.OpEquals,
                        Value = JsonDocument.Parse("\"alta\"").RootElement,
                    },
                ],
                Logic = OtRuleConstants.LogicAnd,
                Action = new OtRuleActionRequest
                {
                    Type = OtRuleConstants.ActionSpecialQueue,
                    QueueName = "prioritaria",
                },
            },
        });

        result.Status.Should().Be(CreateOtRuleStatus.Created);

        await using var verify = NewContext(db);
        var entity = await verify.OtFeatureFlags.SingleAsync();
        entity.Config.Should().Contain("prioritaria");
        entity.Config.Should().Contain("cola_especial");
    }

    [Fact]
    public void AC6_EvaluateOrRule_MatchesWhenOnlyOneConditionTrue()
    {
        var rule = new OtRule
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            IsEnabled = true,
            Name = "OR rule",
            Logic = OtRuleConstants.LogicOr,
            Conditions =
            [
                new OtRuleCondition
                {
                    Field = "deuda_pendiente",
                    Op = OtRuleConstants.OpEquals,
                    ValueJson = "true",
                },
                new OtRuleCondition
                {
                    Field = "tipo_tramite",
                    Op = OtRuleConstants.OpEquals,
                    ValueJson = "\"matricula\"",
                },
            ],
            Action = new OtRuleAction { Type = OtRuleConstants.ActionBiometrics },
        };

        var context = BuildContext(
            ("deuda_pendiente", JsonDocument.Parse("false").RootElement),
            ("tipo_tramite", JsonDocument.Parse("\"matricula\"").RootElement));

        var result = OtRuleEvaluator.Evaluate(rule, context);

        result.Matched.Should().BeTrue();
        result.ActionType.Should().Be(OtRuleConstants.ActionBiometrics);
    }

    private static OtRule BuildDebtBlockRule(bool isEnabled) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantA,
        IsEnabled = isEnabled,
        Name = "Bloqueo por deuda",
        Logic = OtRuleConstants.LogicAnd,
        Conditions =
        [
            new OtRuleCondition
            {
                Field = "deuda_pendiente",
                Op = OtRuleConstants.OpEquals,
                ValueJson = "true",
            },
            new OtRuleCondition
            {
                Field = "tipo_tramite",
                Op = OtRuleConstants.OpIn,
                ValueJson = "[\"matricula\"]",
            },
        ],
        Action = new OtRuleAction { Type = OtRuleConstants.ActionBlock },
    };

    private static Dictionary<string, JsonElement> BuildContext(
        params (string Key, JsonElement Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value);

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
