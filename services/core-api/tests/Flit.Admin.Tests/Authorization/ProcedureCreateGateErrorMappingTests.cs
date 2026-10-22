using Flit.Api.Endpoints.Tramites;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// Hallazgo de la validación de la épica #13090: crear un trámite en un organismo no habilitado para la compañía
/// respondía 500 (NullReferenceException) porque el endpoint no traducía los códigos del gate de creación.
/// </summary>
public sealed class ProcedureCreateGateErrorMappingTests
{
    [Theory]
    [InlineData(ProcedureRadicationDenialReasons.OtNotPermitted, "organismo")]
    [InlineData(ProcedureRadicationDenialReasons.TenantInactive, "inactiva")]
    [InlineData(ProcedureRadicationDenialReasons.NetworkInactive, "red")]
    public void CadaDenegacionDelGateSeTraduceA403ConMensaje(string codigo, string fragmento)
    {
        var result = ProcedureInstanceEndpoints.MapRadicationGateError(codigo);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(403);
        problem.ProblemDetails.Detail.Should().Contain(fragmento);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not_found")]
    [InlineData("COMPANY_RULE_VIOLATION")]
    public void UnCodigoQueNoEsDelGateNoSeTraduceAqui(string? codigo) =>
        ProcedureInstanceEndpoints.MapRadicationGateError(codigo).Should().BeNull();
}
