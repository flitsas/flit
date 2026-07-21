using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10542 + ajuste RUES — el checklist de un traspaso oculta el ítem <c>cedulas</c> cuando el
/// actor es persona natural (identidad digital) y también cuando es persona jurídica/NIT (el
/// documento de identidad lo cubre el certificado RUES autogenerado — supersede HU #10542 AC3).
/// Se conserva solo cuando no hay tipo de persona resuelto. Verificado de punta a punta a través
/// del <see cref="GetChecklistHandler"/>.
/// </summary>
public sealed class GetChecklistPersonTypeTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IChecklistCompanyParamsProvider _companyParams = Substitute.For<IChecklistCompanyParamsProvider>();
    private readonly GetChecklistHandler _handler;

    public GetChecklistPersonTypeTests()
    {
        _companyParams.GetForTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CompanyDocumentParam>>(new List<CompanyDocumentParam>()));
        _handler = new GetChecklistHandler(_repo, _companyParams);
    }

    private static ProcedureInstance Traspaso(Guid id, Guid tenant, params string?[] personTypes)
    {
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoTraspasoStandard,
            ModalidadEntrada = "traspaso",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var pt in personTypes)
        {
            instance.Actors.Add(new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ProcedureInstanceId = id,
                ProcedureEntityId = Guid.NewGuid(),
                ActorType = "vendedor",
                DocumentType = "CC",
                DocumentNumber = "999",
                FullName = "Pedro Vendedor",
                PersonType = pt,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        return instance;
    }

    [Fact]
    public async Task Checklist_PersonaNatural_NoOfreceCedula()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithChecklistGraphAsync(id, tenant, ct)
            .Returns(Traspaso(id, tenant, ActorPersonTypes.Natural));

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Items.Should().NotContain(i => i.Key == "cedulas");
        result.FaltanObligatorios.Should().NotContain("cedulas");
    }

    [Fact]
    public async Task Checklist_PersonaJuridica_OcultaCedula()
    {
        // Ajuste RUES: para actor NIT/persona jurídica el documento de identidad lo cubre el certificado
        // RUES autogenerado ⇒ ya no se ofrece 'cedulas' en el checklist de carga (supersede HU #10542 AC3).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithChecklistGraphAsync(id, tenant, ct)
            .Returns(Traspaso(id, tenant, ActorPersonTypes.Juridical));

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Items.Should().NotContain(i => i.Key == "cedulas");
        result.FaltanObligatorios.Should().NotContain("cedulas");
    }

    [Fact]
    public async Task Checklist_SinTipoPersona_ConservaCedula()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithChecklistGraphAsync(id, tenant, ct)
            .Returns(Traspaso(id, tenant, new string?[] { null }));

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Items.Should().Contain(i => i.Key == "cedulas");
    }
}
