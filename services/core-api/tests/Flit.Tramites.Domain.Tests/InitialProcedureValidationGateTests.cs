using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// FEATURE-08 / HU-BE-02 (CFD-03) — gate puro de validaciones iniciales configurables.
/// Cubre BE-02-AC-03 (companyRule), AC-04 (otOperability), AC-05 (duplicidad) y AC-06 (sin flags).
/// </summary>
public sealed class InitialProcedureValidationGateTests
{
    private static ProcedureTypeGateProfile Profile(
        bool company = false, bool ot = false, bool duplicate = false) =>
        new()
        {
            ValidateCompanyRule = company,
            ValidateOtOperability = ot,
            ValidateDuplicateProcedure = duplicate,
        };

    private static InitialProcedureValidationContext Ctx(
        bool? company = null, bool? ot = null, bool? duplicate = null) =>
        new(company, ot, duplicate);

    [Fact]
    public void AllFlagsOff_NeverBlocks()
    {
        // BE-02-AC-06: sin validaciones activas, cualquier contexto pasa.
        var result = InitialProcedureValidationGate.Evaluate(
            Profile(), Ctx(company: false, ot: false, duplicate: true));

        result.Ok.Should().BeTrue();
        result.Code.Should().BeNull();
    }

    [Fact]
    public void CompanyRule_Unsatisfied_BlocksWithCode()
    {
        // BE-02-AC-03
        var result = InitialProcedureValidationGate.Evaluate(
            Profile(company: true), Ctx(company: false));

        result.Ok.Should().BeFalse();
        result.Code.Should().Be(InitialProcedureValidationGate.CompanyRuleViolation);
    }

    [Fact]
    public void OtOperability_NotAuthorized_BlocksWithCode()
    {
        // BE-02-AC-04
        var result = InitialProcedureValidationGate.Evaluate(
            Profile(ot: true), Ctx(ot: false));

        result.Ok.Should().BeFalse();
        result.Code.Should().Be(InitialProcedureValidationGate.OtNotAuthorized);
    }

    [Fact]
    public void DuplicateProcedure_Exists_BlocksWithConflictCode()
    {
        // BE-02-AC-05
        var result = InitialProcedureValidationGate.Evaluate(
            Profile(duplicate: true), Ctx(duplicate: true));

        result.Ok.Should().BeFalse();
        result.Code.Should().Be(InitialProcedureValidationGate.DuplicateActiveProcedure);
    }

    [Fact]
    public void FlagOn_ButSignalUnknown_DoesNotBlock()
    {
        // Señal null (no evaluable en creación, p. ej. sin OT o sin placa/VIN todavía) → no bloquea.
        var result = InitialProcedureValidationGate.Evaluate(
            Profile(company: true, ot: true, duplicate: true),
            Ctx(company: null, ot: null, duplicate: null));

        result.Ok.Should().BeTrue();
    }

    [Fact]
    public void FlagOn_AndSignalSatisfied_DoesNotBlock()
    {
        var result = InitialProcedureValidationGate.Evaluate(
            Profile(company: true, ot: true, duplicate: true),
            Ctx(company: true, ot: true, duplicate: false));

        result.Ok.Should().BeTrue();
    }

    [Fact]
    public void CompanyRule_HasPrecedenceOverOtAndDuplicate()
    {
        var result = InitialProcedureValidationGate.Evaluate(
            Profile(company: true, ot: true, duplicate: true),
            Ctx(company: false, ot: false, duplicate: true));

        result.Code.Should().Be(InitialProcedureValidationGate.CompanyRuleViolation);
    }
}
