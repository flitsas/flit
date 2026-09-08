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

public sealed class ImprontaManualStampReadinessTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private static ProcedureInstance Matricula()
    {
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        return new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FLIT-M",
            Status = TramiteEstado.Entregado,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcedureType = ProcedureTypeFixture.Matricula,
        };
    }

    private static void AddCompradorVigente(ProcedureInstance instance)
    {
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "1",
            FullName = "Comprador",
            Ordinal = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = "comprador",
            DocumentType = "CC",
            DocumentNumber = "1",
            Name = "Comprador",
            Status = BiometricEstados.Aprobado,
            CreatedAt = DateTimeOffset.UtcNow,
            ValidatedAt = DateTimeOffset.UtcNow,
            ValidUntil = DateTimeOffset.UtcNow.AddDays(5),
        });
    }

    [Fact]
    public async Task Matricula_SinPlaca_NoReady()
    {
        var instance = Matricula();
        AddCompradorVigente(instance);
        instance.PlateFlowStatus = PlateFlowStatus.Preasignado;

        var (ready, reason) = await ImprontaManualStampReadiness.EvaluateAsync(
            instance, _repo, ct: TestContext.Current.CancellationToken);

        ready.Should().BeFalse();
        reason.Should().Be(ImprontaManualStampReadiness.PlacaPendiente);
    }

    [Fact]
    public async Task Matricula_PreasignadoConPlacaVacia_NoReady()
    {
        var instance = Matricula();
        AddCompradorVigente(instance);
        instance.Plate = " ";
        instance.PlateFlowStatus = PlateFlowStatus.Preasignado;

        var (ready, reason) = await ImprontaManualStampReadiness.EvaluateAsync(
            instance, _repo, ct: TestContext.Current.CancellationToken);

        ready.Should().BeFalse();
        reason.Should().Be(ImprontaManualStampReadiness.PlacaPendiente);
    }

    [Fact]
    public async Task Matricula_PlacaAsignadaEIdentidad_Ready()
    {
        var instance = Matricula();
        AddCompradorVigente(instance);
        instance.Plate = "ABC123";
        instance.PlateFlowStatus = PlateFlowStatus.Asignado;

        var (ready, reason) = await ImprontaManualStampReadiness.EvaluateAsync(
            instance, _repo, ct: TestContext.Current.CancellationToken);

        ready.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public async Task Traspaso_SinIdentidad_NoReady()
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FLIT-T",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard),
        };
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "vendedor",
            DocumentType = "CC",
            DocumentNumber = "9",
            FullName = "V",
            Ordinal = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var (ready, reason) = await ImprontaManualStampReadiness.EvaluateAsync(
            instance, _repo, ct: TestContext.Current.CancellationToken);

        ready.Should().BeFalse();
        reason.Should().Be(ImprontaManualStampReadiness.PropietariosSinIdentidad);
    }
}
