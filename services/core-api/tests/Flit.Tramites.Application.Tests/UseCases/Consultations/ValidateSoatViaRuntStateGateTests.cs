using Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// ADR-0059 (HU #12597) — la validación del SOAT contra el RUNT es exclusiva del estado <c>asignado</c>
/// (antes: <c>entregado</c> + sub-estado <c>asignado</c>). Solo se prueba la puerta de estado: el resto
/// del handler consulta proveedores externos.
/// </summary>
public sealed class ValidateSoatViaRuntStateGateTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalog = Substitute.For<ICatalogRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ProcedureInstance Con(string status)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000200",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    [Theory]
    [InlineData("preasignacion")]
    [InlineData("entregado")]
    [InlineData("borrador")]
    public async Task FueraDeAsignado_InvalidState(string status)
    {
        var instance = Con(status);
        var sut = new ValidateSoatViaRuntHandler(_repo, _catalog, _registry);

        var (result, error) = await sut.HandleAsync(instance.Id, instance.TenantId, Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_state");
        await _catalog.DidNotReceive().GetConsultationTemplateByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnAsignado_PasaLaPuertaDeEstado()
    {
        var instance = Con(TramiteEstado.Asignado);
        _catalog.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", Arg.Any<CancellationToken>())
            .Returns((ConsultationTemplate?)null);
        var sut = new ValidateSoatViaRuntHandler(_repo, _catalog, _registry);

        var (_, error) = await sut.HandleAsync(instance.Id, instance.TenantId, Ct);

        // Pasó la puerta de estado: el siguiente fallo ya es el de la plantilla, no el del estado.
        error.Should().Be("template_not_found");
    }
}
