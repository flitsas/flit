using Flit.Admin.Domain.CompanyDocumentParams;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #10521 (RF31) — el puente mapea la persistencia Admin (estado como texto) a los VO del
/// dominio de trámites e ignora los estados no reconocidos.
/// </summary>
public sealed class ChecklistCompanyParamsProviderTests
{
    private sealed class FakeRepo(params CompanyDocumentParamItem[] items) : ICompanyDocumentParamRepository
    {
        public Task<IReadOnlyList<CompanyDocumentParamItem>> ListByTenantAsync(
            Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CompanyDocumentParamItem>>(items);

        public Task<CompanyDocumentParamItem> UpsertAsync(
            Guid tenantId, string documentTypeCode, string state, Guid? userId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static CompanyDocumentParamItem Item(string code, string state) =>
        new(Guid.NewGuid(), Guid.NewGuid(), code, state);

    [Fact]
    public async Task MapeaLosTresEstados_EIgnoraDesconocidos()
    {
        var provider = new ChecklistCompanyParamsProvider(new FakeRepo(
            Item("soat", CompanyDocumentParamStates.Obligatorio),
            Item("cepd", CompanyDocumentParamStates.Oculto),
            Item("rtm", CompanyDocumentParamStates.Opcional),
            Item("otro", "ESTADO_INVALIDO")));

        var result = await provider.GetForTenantAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().HaveCount(3);
        result.Should().ContainEquivalentOf(new CompanyDocumentParam("soat", CompanyDocumentParamState.Obligatorio));
        result.Should().ContainEquivalentOf(new CompanyDocumentParam("cepd", CompanyDocumentParamState.Oculto));
        result.Should().ContainEquivalentOf(new CompanyDocumentParam("rtm", CompanyDocumentParamState.Opcional));
        result.Should().NotContain(p => p.DocTipo == "otro");
    }

    [Fact]
    public async Task SinParametros_DevuelveVacio()
    {
        var provider = new ChecklistCompanyParamsProvider(new FakeRepo());
        var result = await provider.GetForTenantAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        result.Should().BeEmpty();
    }
}
