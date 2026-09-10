using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Tramites.Domain.RuntConfirmation;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.RuntConfirmation;

/// <summary>
/// Los catálogos del dominio y los CHECK del DDL deben decir lo mismo: un valor nuevo en código que
/// no esté en el CHECK revienta al grabar el intento (pasó con <c>vin_plate</c> en la primera corrida real).
/// </summary>
public sealed partial class RuntConfirmationDdlCatalogParityTests
{
    private static readonly string Ddl = EmbeddedDdl.LoadUp("107-F12276-confirmacion-runt.sql");

    [Fact]
    public void ElCheckDeQueryKind_AdmiteTodasLasFormasDeConsultarDelDominio() =>
        ValoresDelCheck("ck_runt_confirmation_attempts_query_kind").Should().BeEquivalentTo(RuntConfirmationQueryKinds.All);

    [Fact]
    public void ElCheckDeVerdict_AdmiteTodosLosVeredictosDelDominio() =>
        ValoresDelCheck("ck_runt_confirmation_attempts_verdict").Should().BeEquivalentTo(RuntConfirmationVerdictCodes.All);

    private static string[] ValoresDelCheck(string constraint)
    {
        var m = CheckRegex().Matches(Ddl).Single(x => x.Groups["name"].Value == constraint);
        return m.Groups["v"].Captures.Select(c => c.Value).ToArray();
    }

    [GeneratedRegex(@"CONSTRAINT (?<name>ck_\w+) CHECK \(\w+ IN \((?:\s*'(?<v>[a-z_]+)'\s*,?)+\)\)")]
    private static partial Regex CheckRegex();
}
