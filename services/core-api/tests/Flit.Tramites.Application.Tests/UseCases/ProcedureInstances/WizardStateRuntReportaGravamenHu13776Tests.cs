using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13776 — el estado del asistente decide con la señal única de gravamen (banderas, garantías,
/// dato prendario y limitaciones a la propiedad) y la expone en <c>runtReportaGravamen</c>.
/// Datos ficticios (Ley 1581): «ACREEDOR DE PRUEBA S.A.S.», NIT 900000001.
/// Uso de ejemplo:
/// <code>var (dto, _) = await new GetWizardStateHandler(repo).HandleAsync(id, tenant, ct); dto!.RuntReportaGravamen</code>
/// </summary>
public sealed class WizardStateRuntReportaGravamenHu13776Tests
{
    private const string UnaLimitacion =
        """[{"tipoLimitacion":"PRENDA","aFavorDe":"ACREEDOR DE PRUEBA S.A.S.","documento":"900000001"}]""";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private static ProcedureInstance Matricula(params (string Key, string? Text, string? Json)[] fv)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013776",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var (key, text, json) in fv)
        {
            instance.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = instance.TenantId,
                ProcedureInstanceId = instance.Id,
                FieldKey = key,
                ValueText = text,
                ValueJson = json,
                Source = "consulta",
            });
        }

        return instance;
    }

    private async Task<WizardStateDto> Estado(ProcedureInstance instance)
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, ct).Returns(instance);
        var (result, error) = await new GetWizardStateHandler(_repo).HandleAsync(instance.Id, instance.TenantId, ct);
        error.Should().BeNull();
        return result!;
    }

    public static TheoryData<string, bool> Casos => new()
    {
        { "limitaciones", true },
        { "prendario", true },
        { "ninguno", false },
    };

    private static ProcedureInstance Caso(string caso) => caso switch
    {
        "limitaciones" => Matricula(
            ("runt_tiene_prendas", "NO", null), ("runt_tiene_gravamenes", "NO", null), ("runt_gravamenes", null, "[]"),
            ("runt_limitaciones_propiedad", null, UnaLimitacion)),
        "prendario" => Matricula(
            ("runt_tiene_prendas", "NO", null), ("runt_tiene_gravamenes", "NO", null), ("runt_gravamenes", null, "[]"),
            ("runt_prendario", "SI", null)),
        "ninguno" => Matricula(
            ("runt_tiene_prendas", "NO", null), ("runt_tiene_gravamenes", "NO", null), ("runt_gravamenes", null, "[]"),
            ("runt_prendario", "NO", null), ("runt_limitaciones_propiedad", null, "[]")),
        _ => throw new ArgumentOutOfRangeException(nameof(caso)),
    };

    // ── AC6 — la exigencia de decisión de prenda (gate) usa la señal única ───────────────────────

    [Theory]
    [MemberData(nameof(Casos))]
    public async Task AC6_SoloLimitacionesOPrendario_ExigeDecisionDePrenda(string caso, bool esperado)
    {
        var dto = await Estado(Caso(caso));

        dto.Capabilities!.HasPrendaGate.Should().Be(esperado);
    }

    // ── AC7 — runtReportaGravamen en el estado del asistente ─────────────────────────────────────

    [Theory]
    [MemberData(nameof(Casos))]
    public async Task AC7_EstadoDelAsistente_ExponeRuntReportaGravamenEnCamelCase(string caso, bool esperado)
    {
        var dto = await Estado(Caso(caso));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, Web));
        json.RootElement.GetProperty("runtReportaGravamen").GetBoolean().Should().Be(esperado);
    }

    [Fact]
    public void AC7_OpenApi_DocumentaRuntReportaGravamenYPrendaOmitAllowed()
    {
        var yaml = Yaml();

        yaml.Should().Contain("  /api/v1/tramites/instances/{id}/wizard:");
        var bloque = Schema(yaml, "WizardState");
        bloque.Should().MatchRegex(@"runtReportaGravamen:\s*\n\s*type: boolean");
        bloque.Should().MatchRegex(@"prendaOmitAllowed:\s*\n\s*type: boolean");
    }

    private static string Yaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "openapi", "core-api.v1.yaml")))
            dir = dir.Parent;
        dir.Should().NotBeNull("el contrato OpenAPI debe encontrarse desde el directorio de tests");
        return File.ReadAllText(Path.Combine(dir!.FullName, "contracts", "openapi", "core-api.v1.yaml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Schema(string yaml, string nombre)
    {
        var m = Regex.Match(yaml, "^    " + nombre + ":\\n((?:(?:      .*)?\\n)+)", RegexOptions.Multiline);
        m.Success.Should().BeTrue($"components.schemas.{nombre} debe existir en core-api.v1.yaml");
        return m.Groups[1].Value;
    }
}
