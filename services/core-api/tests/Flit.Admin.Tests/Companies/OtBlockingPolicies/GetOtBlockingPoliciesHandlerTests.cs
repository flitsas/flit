using Flit.Admin.Application.Companies.TransitOffices.GetOtBlockingPolicies;
using Flit.Admin.Domain.Companies.TransitOffices;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.OtBlockingPolicies;

/// <summary>
/// Tests del caso de uso de lectura de políticas de bloqueo por OT (FEATURE 05): proyecta las filas
/// del repositorio a la respuesta del contrato. Tabla dispersa: lista vacía si no hay filas.
/// </summary>
public sealed class GetOtBlockingPoliciesHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Office = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");

    [Fact]
    public async Task SinFilas_DevuelveVacio()
    {
        var repository = Substitute.For<IOtBlockingPolicyRepository>();
        repository.ListAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OtBlockingPolicyItem>>([]));
        var handler = new GetOtBlockingPoliciesHandler(repository);

        var result = await handler.HandleAsync(
            new GetOtBlockingPoliciesQuery { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ConFilas_ProyectaCadaCampo()
    {
        var repository = Substitute.For<IOtBlockingPolicyRepository>();
        repository.ListAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OtBlockingPolicyItem>>(
            [
                new OtBlockingPolicyItem(Office, BlockingCriteria.Soat, false),
                new OtBlockingPolicyItem(Office, BlockingCriteria.Fines, true),
            ]));
        var handler = new GetOtBlockingPoliciesHandler(repository);

        var result = await handler.HandleAsync(
            new GetOtBlockingPoliciesQuery { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo(new[]
        {
            new OtBlockingPolicyResponse(Office, BlockingCriteria.Soat, false),
            new OtBlockingPolicyResponse(Office, BlockingCriteria.Fines, true),
        });
    }
}
