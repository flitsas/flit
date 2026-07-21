using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// FEATURE 05 — consulta RNMC desacoplada del pre-vuelo (RunRnmcConsultHandler). Corre en el paso final
/// por cada actor persona natural con su fecha de expedición, cuando aplica (opt-in de la compañía o
/// requisito del OT), y persiste el resultado en el field_value <c>rnmc_checks</c>.
/// </summary>
public sealed class RnmcConsultHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private sealed class StaticRegistry(Dictionary<string, IConsultationProvider> providers) : IConsultationProviderRegistry
    {
        public IConsultationProvider? Resolve(string providerKey) =>
            providers.TryGetValue(providerKey, out var p) ? p : null;
    }

    private sealed class StubProvider(string key, ConsultationResult result) : IConsultationProvider
    {
        public string Key => key;
        public ConsultationContext? LastContext { get; private set; }
        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            LastContext = ctx;
            return Task.FromResult(result);
        }
    }

    private sealed class StubRnmcPolicy(bool required) : IRnmcRequirementPolicy
    {
        public Task<bool> IsRnmcRequiredAsync(Guid tenantId, Guid? transitOfficeId, CancellationToken ct = default) =>
            Task.FromResult(required);
    }

    private sealed class StubRestrictionPolicy(params (string kind, bool enabled)[] settings) : IConsultationRestrictionPolicy
    {
        public Task<ConsultationRestrictions> GetAsync(Guid tenantId, Guid? transitOfficeId, CancellationToken ct = default) =>
            Task.FromResult(ConsultationRestrictions.FromSettings(
                settings.Select(s => new KeyValuePair<string, bool>(s.kind, s.enabled))));
    }

    private static ConsultationResult Rnmc(string status, string? message = null) =>
        new("verifik_rnmc", status == "warn" ? "yellow" : "green",
            [new ConsultationCheck("medidas_correctivas", "Medidas correctivas (Policía)", status, "verifik_rnmc", message)],
            []);

    private static ProcedureInstance Instance(string modalidad, params ProcedureInstanceActor[] actors)
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = "borrador",
            ModalidadEntrada = modalidad,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var a in actors)
            instance.Actors.Add(a);
        return instance;
    }

    private static ProcedureInstanceActor Actor(string actorType, string doc = "111") =>
        new() { ActorType = actorType, DocumentType = "CC", DocumentNumber = doc, FullName = "Persona", Email = "p@x.com" };

    private static ProcedureInstanceActor ActorNit(string actorType, string doc) =>
        new() { ActorType = actorType, DocumentType = "NIT", DocumentNumber = doc, FullName = "EMPRESA", Email = "e@x.com" };

    private RunRnmcConsultHandler Build(
        (string key, IConsultationProvider provider)[] providers,
        bool rnmcRequired = false,
        IConsultationRestrictionPolicy? restrictionPolicy = null)
    {
        var registry = new StaticRegistry(providers.ToDictionary(p => p.key, p => p.provider));
        return new RunRnmcConsultHandler(
            _repo, registry, new StubRnmcPolicy(rnmcRequired),
            restrictionPolicy ?? NullConsultationRestrictionPolicy.Instance);
    }

    [Fact] // Opt-in de la compañía → consulta comprador y vendedor (PN) y persiste rnmc_checks.
    public async Task Consulta_CompradorYVendedor_CuandoCompaniaActiva()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", Actor("comprador", "111"), Actor("vendedor", "222"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = Build(
            [("verifik_rnmc", new StubProvider("verifik_rnmc", Rnmc("ok", "Sin medidas correctivas registradas en el RNMC")))],
            rnmcRequired: false,
            restrictionPolicy: new StubRestrictionPolicy((ConsultationRestrictionKinds.Rnmc, true)));

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Should().Contain(c => c.Key == "rnmc_comprador_medidas_correctivas");
        result.Should().Contain(c => c.Key == "rnmc_vendedor_medidas_correctivas");
        // Persistido en el field_value rnmc_checks.
        instance.FieldValues.Should().Contain(f => f.FieldKey == "rnmc_checks" && f.ValueJson != null);
    }

    [Fact] // No aplica (ni opt-in ni requisito del OT) → no consulta, rnmc_checks vacío.
    public async Task NoConsulta_CuandoNoAplica()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", Actor("comprador", "111"), Actor("vendedor", "222"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var rnmc = new StubProvider("verifik_rnmc", Rnmc("ok"));
        var handler = Build([("verifik_rnmc", rnmc)], rnmcRequired: false);

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Should().BeEmpty();
        rnmc.LastContext.Should().BeNull();
    }

    [Fact] // Actor jurídico (NIT) no consulta RNMC; el comprador PN sí.
    public async Task JuridicoNoConsulta()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", Actor("comprador", "111"), ActorNit("vendedor", "900123456"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = Build(
            [("verifik_rnmc", new StubProvider("verifik_rnmc", Rnmc("ok")))],
            restrictionPolicy: new StubRestrictionPolicy((ConsultationRestrictionKinds.Rnmc, true)));

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Should().Contain(c => c.Key == "rnmc_comprador_medidas_correctivas");
        result.Should().NotContain(c => c.Key == "rnmc_vendedor_medidas_correctivas");
    }

    [Fact] // Matrícula: solo el comprador.
    public async Task Matricula_SoloComprador()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", Actor("comprador", "111"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = Build(
            [("verifik_rnmc", new StubProvider("verifik_rnmc", Rnmc("ok")))],
            restrictionPolicy: new StubRestrictionPolicy((ConsultationRestrictionKinds.Rnmc, true)));

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Should().ContainSingle(c => c.Key == "rnmc_comprador_medidas_correctivas");
    }

    [Fact] // El contexto RNMC lleva document_issue_date del actor.
    public async Task Contexto_IncluyeFechaDeExpedicion()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", Actor("comprador", "111"));
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "comprador_document_issue_date", ValueText = "01/02/2010", Source = "user" });
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var rnmc = new StubProvider("verifik_rnmc", Rnmc("ok"));
        var handler = Build([("verifik_rnmc", rnmc)], restrictionPolicy: new StubRestrictionPolicy((ConsultationRestrictionKinds.Rnmc, true)));

        var (_, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        rnmc.LastContext!.FieldValues.Should().Contain("owner_document_number", "111");
        rnmc.LastContext.FieldValues.Should().Contain("document_issue_date", "01/02/2010");
    }

    [Fact] // Una medida correctiva (warn) deja la señal rnmc_medida_pendiente=true.
    public async Task Medida_DejaSenalPendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", Actor("comprador", "111"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = Build(
            [("verifik_rnmc", new StubProvider("verifik_rnmc", Rnmc("warn", "2 medida(s)")))],
            restrictionPolicy: new StubRestrictionPolicy((ConsultationRestrictionKinds.Rnmc, true)));

        var (_, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        instance.FieldValues.Should().Contain(f => f.FieldKey == "rnmc_medida_pendiente" && f.ValueText == "true");
    }

    [Fact] // GetRnmcHandler devuelve los checks persistidos.
    public async Task GetRnmc_DevuelveLoPersistido()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", Actor("comprador", "111"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var run = Build(
            [("verifik_rnmc", new StubProvider("verifik_rnmc", Rnmc("ok", "Sin medidas correctivas registradas en el RNMC")))],
            restrictionPolicy: new StubRestrictionPolicy((ConsultationRestrictionKinds.Rnmc, true)));
        await run.HandleAsync(instance.Id, instance.TenantId, ct);

        var get = new GetRnmcHandler(_repo);
        var (result, error) = await get.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Should().Contain(c => c.Key == "rnmc_comprador_medidas_correctivas" && c.Status == "ok");
    }
}
