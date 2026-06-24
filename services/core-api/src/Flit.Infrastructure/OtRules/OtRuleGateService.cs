using System.Text.Json;
using Flit.Admin.Application.OtRules;
using Flit.Admin.Domain.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>Evalúa reglas OT activas en el flujo operativo de trámites (HU #10221 RF06–08).</summary>
internal sealed class OtRuleGateService : IOtRuleGate
{
    private readonly FlitDbContext _context;
    private readonly IOtRuleRepository _ruleRepository;

    public OtRuleGateService(FlitDbContext context, IOtRuleRepository ruleRepository)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _ruleRepository = ruleRepository ?? throw new ArgumentNullException(nameof(ruleRepository));
    }

    public async Task<OtRuleGateResult> EvaluateSubmissionAsync(
        Guid? transitOfficeId,
        Guid procedureTypeId,
        string? procedureTypeCode,
        CancellationToken cancellationToken = default)
    {
        if (transitOfficeId is null || transitOfficeId == Guid.Empty)
        {
            return OtRuleGateResult.Allowed();
        }

        var otTenantId = await _context.TransitOfficeProfiles
            .AsNoTracking()
            .Where(p => p.TransitOfficeId == transitOfficeId.Value)
            .Select(p => (Guid?)p.TenantId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (otTenantId is null)
        {
            return OtRuleGateResult.Allowed();
        }

        var rules = await _ruleRepository
            .ListEnabledByTenantAsync(otTenantId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (rules.Count == 0)
        {
            return OtRuleGateResult.Allowed();
        }

        var typeCode = procedureTypeCode;
        if (string.IsNullOrWhiteSpace(typeCode))
        {
            typeCode = await _context.ProcedureTypes
                .AsNoTracking()
                .Where(pt => pt.Id == procedureTypeId)
                .Select(pt => pt.Code)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var context = BuildContext(typeCode);
        var matches = OtRuleEvaluator.EvaluateAll(rules, context);
        if (matches.Count == 0)
        {
            return OtRuleGateResult.Allowed();
        }

        var first = matches[0];
        return first.ActionType switch
        {
            OtRuleConstants.ActionBlock => OtRuleGateResult.Blocked(first.ActionType),
            OtRuleConstants.ActionBiometrics => OtRuleGateResult.BiometricRequired(),
            OtRuleConstants.ActionSpecialQueue => OtRuleGateResult.SpecialQueue(first.QueueName ?? "default"),
            _ => OtRuleGateResult.Allowed(),
        };
    }

    private static Dictionary<string, JsonElement> BuildContext(string? procedureTypeCode)
    {
        var json = JsonSerializer.Serialize(new
        {
            tipo_tramite = procedureTypeCode ?? string.Empty,
            deuda_pendiente = false,
        });

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}
