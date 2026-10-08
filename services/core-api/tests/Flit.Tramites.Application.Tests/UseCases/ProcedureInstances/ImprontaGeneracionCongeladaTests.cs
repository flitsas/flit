using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13402 (Feature #13399) — el trámite congela al crearse el parámetro «generar improntas» de la
/// compañía; cambios posteriores del parámetro no lo alteran, y la exigencia de la impronta al
/// radicar sigue gobernada por la matriz/checklist (SubmitGate sin cambios).
/// </summary>
public sealed class ImprontaGeneracionCongeladaTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly MutableGate _gate = new();
    private readonly CreateProcedureInstanceHandler _sut;
    private readonly List<ProcedureInstance> _creados = [];

    public ImprontaGeneracionCongeladaTests()
    {
        var tipo = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "MATRICULA_NUEVA",
            Name = "Matrícula nueva",
            Family = "matriculas",
            PublicationStatus = PublicationStatus.Published,
            WizardEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _typeRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(tipo);
        _repo.AddWithUniqueReferenceAsync(Arg.Any<ProcedureInstance>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var instance = call.Arg<ProcedureInstance>();
                instance.ReferenceNumber = "1";
                _creados.Add(instance);
                return Task.FromResult(AddProcedureInstanceOutcome.Created);
            });
        _sut = new CreateProcedureInstanceHandler(_repo, _typeRepo, familyCreationGate: _gate);
    }

    /// <summary>Gate cuyo parámetro de improntas puede cambiar entre dos altas (simula al Super Admin).</summary>
    private sealed class MutableGate : IProcedureFamilyCreationGate
    {
        public bool ImprontasHabilitadas { get; set; } = true;

        public Task<bool> IsFamilyBlockedAsync(Guid tenantId, string? procedureFamily, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> IsImprontaGenerationEnabledAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(ImprontasHabilitadas);
    }

    private static CreateProcedureInstanceRequest Request() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

    [Fact]
    public async Task Crear_ConParametroHabilitado_GuardaTrue()
    {
        // AC1
        var ct = TestContext.Current.CancellationToken;
        _gate.ImprontasHabilitadas = true;

        var (_, error) = await _sut.HandleAsync(Request(), ct);

        error.Should().BeNull();
        _creados.Single().ImprontaGeneracionHabilitada.Should().BeTrue();
    }

    [Fact]
    public async Task Crear_ConParametroDeshabilitado_GuardaFalse()
    {
        // AC2 (alta)
        var ct = TestContext.Current.CancellationToken;
        _gate.ImprontasHabilitadas = false;

        var (_, error) = await _sut.HandleAsync(Request(), ct);

        error.Should().BeNull();
        _creados.Single().ImprontaGeneracionHabilitada.Should().BeFalse();
    }

    [Fact]
    public async Task Deshabilitar_DespuesDeCrear_NoAfectaAlTramiteEnCurso()
    {
        // AC3 — el borrador creado con el parámetro habilitado conserva true aunque el Super Admin
        // lo deshabilite después; solo el trámite nuevo nace en false.
        var ct = TestContext.Current.CancellationToken;
        _gate.ImprontasHabilitadas = true;
        await _sut.HandleAsync(Request(), ct);

        _gate.ImprontasHabilitadas = false;
        await _sut.HandleAsync(Request(), ct);

        _creados[0].ImprontaGeneracionHabilitada.Should().BeTrue();
        _creados[1].ImprontaGeneracionHabilitada.Should().BeFalse();
    }

    [Fact]
    public async Task Reactivar_DespuesDeCrear_NoCambiaTramitesYaCreados()
    {
        // AC4 — el borrador creado con el parámetro deshabilitado conserva false al reactivarlo.
        var ct = TestContext.Current.CancellationToken;
        _gate.ImprontasHabilitadas = false;
        await _sut.HandleAsync(Request(), ct);

        _gate.ImprontasHabilitadas = true;
        await _sut.HandleAsync(Request(), ct);

        _creados[0].ImprontaGeneracionHabilitada.Should().BeFalse();
        _creados[1].ImprontaGeneracionHabilitada.Should().BeTrue();
    }

    [Fact]
    public async Task Crear_SinGateConfigurado_UsaElDefaultHistoricoTrue()
    {
        // AC8 (por código) — composiciones sin Admin y trámites previos a la migración: true.
        var ct = TestContext.Current.CancellationToken;
        var sinGate = new CreateProcedureInstanceHandler(_repo, _typeRepo);

        await sinGate.HandleAsync(Request(), ct);

        _creados.Single().ImprontaGeneracionHabilitada.Should().BeTrue();
        new ProcedureInstance().ImprontaGeneracionHabilitada.Should().BeTrue();
    }

    private static ProcedureInstance Matricula(bool generacionHabilitada) =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
            ImprontaGeneracionHabilitada = generacionHabilitada,
        };

    private static ProcedureInstanceAttachment Adjunto(ProcedureInstance instance, string tipo) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            StoragePath = $"{instance.Id:D}/{tipo}",
            Sha256 = "sha",
            SizeBytes = 10,
            Mimetype = "application/pdf",
        };

    [Fact]
    public void SubmitGate_ConGeneracionDeshabilitada_ExigeImprontaIgualQueConHabilitada()
    {
        // AC7 — la exigencia de la impronta al radicar no depende del flag congelado: sin adjunto
        // sigue pidiéndose (impronta_requerida) y con un adjunto subido a mano se satisface.
        var partes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var habilitada = Matricula(true);
        var deshabilitada = Matricula(false);

        SubmitGate.Evaluate(deshabilitada, partes).Should().Contain(SubmitGate.ImprontaRequerida);
        SubmitGate.Evaluate(deshabilitada, partes)
            .Should().BeEquivalentTo(SubmitGate.Evaluate(habilitada, partes));

        deshabilitada.Attachments.Add(Adjunto(deshabilitada, "impronta"));
        habilitada.Attachments.Add(Adjunto(habilitada, "impronta"));

        SubmitGate.Evaluate(deshabilitada, partes).Should().NotContain(SubmitGate.ImprontaRequerida);
        SubmitGate.Evaluate(deshabilitada, partes)
            .Should().BeEquivalentTo(SubmitGate.Evaluate(habilitada, partes));
    }
}
