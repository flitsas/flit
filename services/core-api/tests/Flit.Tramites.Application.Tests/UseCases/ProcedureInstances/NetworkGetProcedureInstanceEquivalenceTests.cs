using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Regresión del fallo de CI del PR #370 (Feature #12257 ∪ Bug #12526): el detalle consolidado de la red
/// (<see cref="NetworkGetProcedureInstanceHandler"/>) debe construir EXACTAMENTE el mismo
/// <see cref="ProcedureInstanceDetailDto"/> que el detalle propio (<see cref="GetProcedureInstanceHandler"/>),
/// incluidas las resoluciones batch de usuarios (autores de eventos y del historial). Ambos handlers
/// pasan por <c>GetProcedureInstanceHandler.BuildDetailAsync</c>: si alguien enriquece el detalle propio
/// sin tocar ese punto, este test lo ve porque compara el resultado completo y el número de consultas de
/// resolución que hizo cada handler.
/// Uso de ejemplo: <c>await new NetworkGetProcedureInstanceHandler(repo).HandleAsync(id, TenantScope.Group(P, [C1], GroupKind.Concesion))</c>.
/// </summary>
public sealed class NetworkGetProcedureInstanceEquivalenceTests
{
    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid Gestor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GestorNuevo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Detalle_de_red_y_detalle_propio_son_equivalentes_y_resuelven_los_mismos_usuarios()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = Substitute.For<IProcedureInstanceRepository>();
        var instance = InstanceOfC1();
        var scope = TenantScope.Group(P, [C1], GroupKind.Concesion);

        repo.GetByIdWithDetailsAsync(instance.Id, C1, ct).Returns(instance);
        repo.GetByIdWithDetailsAsync(instance.Id, scope, ct).Returns(instance);
        repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [C1] = "Cliente C1" });
        repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [Gestor] = "Gestor C1", [GestorNuevo] = "Gestor nuevo" });

        var (own, ownError) = await new GetProcedureInstanceHandler(repo).HandleAsync(instance.Id, C1, ct);
        var ownLookups = repo.ReceivedCalls().Count(c => c.GetMethodInfo().Name.StartsWith("GetUser", StringComparison.Ordinal));
        repo.ClearReceivedCalls();

        var (network, networkError) = await new NetworkGetProcedureInstanceHandler(repo).HandleAsync(instance.Id, scope, ct);
        var networkLookups = repo.ReceivedCalls().Count(c => c.GetMethodInfo().Name.StartsWith("GetUser", StringComparison.Ordinal));

        ownError.Should().BeNull();
        networkError.Should().BeNull();
        network!.TenantId.Should().Be(C1);
        network.TenantName.Should().Be("Cliente C1");
        network.Instance.Should().BeEquivalentTo(own);
        network.Instance.Events.Should().ContainSingle(e => e.CreatedByName == "Gestor C1" && e.NewAssignedToName == "Gestor nuevo");
        networkLookups.Should().Be(ownLookups, "la vista de red resuelve exactamente lo mismo que el detalle propio");
        ownLookups.Should().BeGreaterThan(0);
    }

    private static ProcedureInstance InstanceOfC1()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        return new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = id,
            TenantId = C1,
            ProcedureTypeId = ProcedureTypeFixture.Matricula.Id,
            ReferenceNumber = "TRM-2026-000777",
            Status = TramiteEstado.Entregado,
            CreatedAt = now.AddDays(-2),
            StatusHistory =
            {
                new ProcedureInstanceStatusHistory { Id = Guid.NewGuid(), ProcedureInstanceId = id, ToStatus = TramiteEstado.Borrador, ChangedAt = now.AddDays(-2), ChangedBy = Gestor },
                new ProcedureInstanceStatusHistory { Id = Guid.NewGuid(), ProcedureInstanceId = id, FromStatus = TramiteEstado.Borrador, ToStatus = TramiteEstado.Entregado, ChangedAt = now.AddDays(-1), ChangedBy = Gestor },
            },
            Events =
            {
                new ProcedureInstanceEvent
                {
                    Id = Guid.NewGuid(),
                    ProcedureInstanceId = id,
                    Tipo = "reasignar_gestor_admin",
                    CreatedAt = now.AddHours(-1),
                    CreatedBy = Gestor,
                    Payload = $$"""{"previous_assigned_to_user_id":"{{Gestor}}","new_assigned_to_user_id":"{{GestorNuevo}}"}""",
                },
            },
        };
    }
}
