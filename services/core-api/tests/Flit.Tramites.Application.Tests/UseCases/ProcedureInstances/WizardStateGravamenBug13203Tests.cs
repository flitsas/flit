using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13203 — el gate de prenda (<c>Capabilities.HasPrendaGate</c>) lee la señal única de gravamen:
/// banderas RUNT afirmativas O <c>runt_gravamenes</c> con al menos una garantía.
/// Uso de ejemplo:
/// <code>var (dto, _) = await new GetWizardStateHandler(repo).HandleAsync(id, tenant, ct); dto!.Capabilities!.HasPrendaGate</code>
/// </summary>
public sealed class WizardStateGravamenBug13203Tests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private static ProcedureInstance Matricula(params (string Key, string? Text, string? Json)[] fv)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013203",
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

    private async Task<bool> HasPrendaGate(ProcedureInstance instance)
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, ct).Returns(instance);
        var (result, error) = await new GetWizardStateHandler(_repo).HandleAsync(instance.Id, instance.TenantId, ct);
        error.Should().BeNull();
        return result!.Capabilities!.HasPrendaGate;
    }

    [Fact]
    public async Task BanderasNo_ConGarantiaEnRuntGravamenes_ExigeDecisionDePrenda()
    {
        var instance = Matricula(
            ("runt_tiene_prendas", "NO", null),
            ("runt_tiene_gravamenes", "NO", null),
            ("runt_gravamenes", null, """[{"idPrenda":"1000001","nombreAcreedor":"BANCO DE PRUEBA S.A."}]"""));

        (await HasPrendaGate(instance)).Should().BeTrue();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{roto")]
    public async Task BanderasNo_ConDetalleVacioOInvalido_NoExigeDecisionDePrenda(string detalle)
    {
        var instance = Matricula(
            ("runt_tiene_prendas", "NO", null),
            ("runt_tiene_gravamenes", "NO", null),
            ("runt_gravamenes", null, detalle));

        (await HasPrendaGate(instance)).Should().BeFalse();
    }

    [Fact]
    public async Task Control_BanderaSi_ExigeDecisionDePrenda()
    {
        (await HasPrendaGate(Matricula(("runt_tiene_prendas", "SI", null)))).Should().BeTrue();
    }
}
