using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class SubmitProcedureInstanceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeGrantGate _grantGate = Substitute.For<ITransitOfficeGrantGate>();
    private readonly SubmitProcedureInstanceHandler _sut;

    public SubmitProcedureInstanceTests()
    {
        // Por defecto, cualquier OT se considera habilitado (la restricción se ejercita
        // explícitamente en los tests que la cubren).
        _grantGate
            .IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _sut = new SubmitProcedureInstanceHandler(
            _repo,
            _typeRepo,
            NullProcedureStateChangeNotifier.Instance,
            NullOtRuleGate.Instance,
            _grantGate);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = "matricula_inicial",
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoMatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow
        };

    /// <summary>Instancia matrícula que satisface TODOS los gates de radicado (happy path).</summary>
    private static ProcedureInstance FullyGated(Guid id, Guid tenantId)
    {
        var i = Instance(id, tenantId, ProcedureInstanceStatus.Draft);
        // Documentos obligatorios matrícula: factura + aduana + impronta.
        foreach (var t in new[] { "factura", "aduana", "impronta", "fur" })
        {
            i.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                Tipo = t,
                StoragePath = $"p/{t}",
                UploadedAt = DateTimeOffset.UtcNow,
            });
        }
        // Biométrica del comprador aprobada.
        i.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Parte = "comprador",
            Estado = BiometricEstados.Aprobado,
            Nombre = "X",
            TipoDoc = "CC",
            Documento = "1",
            Email = "x@y.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        // Organismo de tránsito seleccionado.
        i.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });
        return i;
    }

    private static ProcedureType PublishedType(Guid id) =>
        new()
        {
            Id = id,
            Code = "X",
            Name = "X",
            Family = "matriculas",
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task HandleAsync_NotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct)
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_AlreadySubmitted_ReturnsNotDraft()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, ct)
            .Returns(Instance(id, tenantId, ProcedureInstanceStatus.Submitted));

        var (result, error) = await _sut.HandleAsync(id, tenantId, ct);

        error.Should().Be("not_draft");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_DraftAndPublished_TransitionsToSubmitted()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, ct).Returns(instance);
        _typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).Returns(PublishedType(instance.ProcedureTypeId));

        var (result, error) = await _sut.HandleAsync(id, tenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Status.Should().Be(ProcedureInstanceStatus.Submitted);
        result.SubmittedAt.Should().NotBeNull();
        instance.Status.Should().Be(ProcedureInstanceStatus.Submitted);
        instance.SubmittedAt.Should().NotBeNull();
        instance.StatusHistory.Should().ContainSingle(h =>
            h.FromStatus == ProcedureInstanceStatus.Draft && h.ToStatus == ProcedureInstanceStatus.Submitted);
        // El status_history NUEVO se marca Added explícito → INSERT (PK store-generated con Id ya seteado).
        _repo.Received(1).Add(Arg.Any<ProcedureInstanceStatusHistory>());
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    private static readonly Guid BogotaOfficeId =
        Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");

    private static void SeleccionarOt(ProcedureInstance instance, Guid officeId)
    {
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "transit_office_id",
            ValueText = officeId.ToString(),
            Source = "user",
        });
    }

    [Fact]
    public async Task HandleAsync_OrganismoNoHabilitado_BloqueaSinRadicar()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        SeleccionarOt(instance, BogotaOfficeId);
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, ct).Returns(instance);
        _typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).Returns(PublishedType(instance.ProcedureTypeId));
        _grantGate.IsEnabledForTenantAsync(tenantId, BogotaOfficeId, Arg.Any<CancellationToken>())
            .Returns(false); // OT NO habilitado para la empresa

        var (result, error) = await _sut.HandleAsync(id, tenantId, ct);

        error.Should().Be("organismo_no_habilitado");
        result.Should().BeNull();
        instance.Status.Should().Be(ProcedureInstanceStatus.Draft); // no transiciona
        await _repo.DidNotReceive().SaveChangesAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_OrganismoHabilitado_PromueveTransitOfficeIdYRadica()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        SeleccionarOt(instance, BogotaOfficeId);
        instance.TransitOfficeId.Should().BeNull(); // el FUR solo persistía field_values
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, ct).Returns(instance);
        _typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).Returns(PublishedType(instance.ProcedureTypeId));
        _grantGate.IsEnabledForTenantAsync(tenantId, BogotaOfficeId, Arg.Any<CancellationToken>())
            .Returns(true);

        var (result, error) = await _sut.HandleAsync(id, tenantId, ct);

        error.Should().BeNull();
        // El id se promueve a la columna para el motor de reglas OT y los listados.
        instance.TransitOfficeId.Should().Be(BogotaOfficeId);
        instance.Status.Should().Be(ProcedureInstanceStatus.Submitted);
    }

    [Fact]
    public async Task HandleAsync_DocumentosIncompletos_ReturnsGateError()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        instance.Attachments.Clear(); // sin docs ni FUR → primer gate que falla es documentos_incompletos
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, ct).Returns(instance);
        _typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).Returns(PublishedType(instance.ProcedureTypeId));

        var (result, error) = await _sut.HandleAsync(id, tenantId, ct);

        error.Should().Be("documentos_incompletos");
        result.Should().BeNull();
        instance.Status.Should().Be(ProcedureInstanceStatus.Draft);
    }

    [Fact]
    public async Task HandleAsync_IdentidadRequerida_ReturnsGateError()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        instance.BiometricValidations.Clear(); // docs+fur+organismo ok, falta biométrica
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, ct).Returns(instance);
        _typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).Returns(PublishedType(instance.ProcedureTypeId));

        var (_, error) = await _sut.HandleAsync(id, tenantId, ct);

        error.Should().Be("identidad_requerida");
    }

    [Fact]
    public async Task HandleAsync_SinFur_TransitionsToSubmitted()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "fur"));
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, ct).Returns(instance);
        _typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).Returns(PublishedType(instance.ProcedureTypeId));

        var (result, error) = await _sut.HandleAsync(id, tenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Status.Should().Be(ProcedureInstanceStatus.Submitted);
    }

    [Fact]
    public async Task HandleAsync_SinOrganismo_TransitionsToSubmitted()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        instance.FieldValues.Clear();
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, ct).Returns(instance);
        _typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).Returns(PublishedType(instance.ProcedureTypeId));

        var (result, error) = await _sut.HandleAsync(id, tenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Status.Should().Be(ProcedureInstanceStatus.Submitted);
    }
}
