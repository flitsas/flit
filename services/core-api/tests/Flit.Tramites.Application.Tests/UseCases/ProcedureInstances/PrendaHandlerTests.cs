using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

// HU #10594 (IT-3) — comando base de prenda: registro de la decisión vigente y versionado
// (una nueva vigente reemplaza a la anterior, historial completo, invariante 0..1 vigente).
public sealed class PrendaHandlerTests
{
    private readonly IProcedureInstanceRepository _instances = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakePrendaRepo _prendas = new();
    private readonly RegistrarPrendaHandler _registrar;
    private readonly GetPrendaVigenteHandler _get;

    public PrendaHandlerTests()
    {
        _registrar = new RegistrarPrendaHandler(_instances, _prendas);
        _get = new GetPrendaVigenteHandler(_prendas);
    }

    /// <summary>Repo de prenda en memoria: refleja el versionado real (mutaciones + inserciones).</summary>
    private sealed class FakePrendaRepo : IProcedureInstancePrendaRepository
    {
        public List<ProcedureInstancePrenda> Rows { get; } = [];

        public Task<ProcedureInstancePrenda?> GetVigenteAsync(Guid instanceId, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(r =>
                r.ProcedureInstanceId == instanceId && r.TenantId == tenantId && r.Estado == PrendaEstado.Vigente));

        public Task<IReadOnlyList<ProcedureInstancePrenda>> ListByInstanceAsync(Guid instanceId, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProcedureInstancePrenda>>(
                Rows.Where(r => r.ProcedureInstanceId == instanceId && r.TenantId == tenantId)
                    .OrderByDescending(r => r.CreatedAt).ToList());

        public Task AddAsync(ProcedureInstancePrenda prenda, CancellationToken ct = default)
        {
            if (prenda.Id == Guid.Empty)
                prenda.Id = Guid.NewGuid();
            Rows.Add(prenda);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private void InstanceExists(Guid id, Guid tenantId, string status = TramiteEstado.Borrador) =>
        _instances.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstance
            {
                Id = id,
                TenantId = tenantId,
                ProcedureTypeId = Guid.NewGuid(),
                ReferenceNumber = "TRM-2026-000001",
                Status = status,
                CreatedAt = DateTimeOffset.UtcNow,
            });

    [Fact]
    public async Task Registrar_decision_invalida_devuelve_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var (result, error) = await _registrar.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), new RegistrarPrendaInput("no_existe"), null, ct);

        error.Should().Be("prenda_decision_invalida");
        result.Should().BeNull();
        _prendas.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Registrar_instancia_inexistente_devuelve_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _instances.GetByIdAsync(id, tenant, Arg.Any<CancellationToken>()).Returns((ProcedureInstance?)null);

        var (result, error) = await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("registrar"), null, ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Registrar_crea_una_fila_vigente_con_los_datos_del_acreedor()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        InstanceExists(id, tenant);

        var (result, error) = await _registrar.HandleAsync(
            id, tenant, new RegistrarPrendaInput("registrar", "Banco XYZ", "900123456"), null, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Decision.Should().Be("registrar");
        result.Estado.Should().Be(PrendaEstado.Vigente);
        result.AcreedorNombre.Should().Be("Banco XYZ");
        result.AcreedorDocumento.Should().Be("900123456");

        _prendas.Rows.Should().ContainSingle();
        _prendas.Rows[0].TenantId.Should().Be(tenant);
        _prendas.Rows[0].ProcedureInstanceId.Should().Be(id);
    }

    [Fact]
    public async Task Registrar_de_nuevo_versiona_la_anterior_a_reemplazada()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        InstanceExists(id, tenant);

        await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("sin_prenda"), null, ct);
        var (result, error) = await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("registrar", "Banco XYZ"), null, ct);

        error.Should().BeNull();
        result!.Decision.Should().Be("registrar");

        // Historial completo (2 filas) con exactamente UNA vigente = la nueva decisión.
        _prendas.Rows.Should().HaveCount(2);
        _prendas.Rows.Count(r => r.Estado == PrendaEstado.Vigente).Should().Be(1);
        _prendas.Rows.Single(r => r.Estado == PrendaEstado.Vigente).Decision.Should().Be("registrar");
        _prendas.Rows.Single(r => r.Estado == PrendaEstado.Reemplazada).Decision.Should().Be("sin_prenda");
    }

    [Fact]
    public async Task GetVigente_devuelve_la_ultima_decision_registrada()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        InstanceExists(id, tenant);

        await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("solicitar"), null, ct);
        await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("levantar"), null, ct);

        var vigente = await _get.HandleAsync(id, tenant, ct);

        vigente.Should().NotBeNull();
        vigente!.Decision.Should().Be("levantar");
        vigente.Estado.Should().Be(PrendaEstado.Vigente);
    }

    [Fact]
    public async Task GetVigente_sin_prenda_registrada_devuelve_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var vigente = await _get.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);
        vigente.Should().BeNull();
    }

    // ── R17 (HU #10599) — modificación post-registro versionada + auditoría ──────

    [Fact]
    public async Task Modificar_post_registro_versiona_y_audita()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        InstanceExists(id, tenant, TramiteEstado.Entregado); // ya registrado, no final

        await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("sin_prenda"), null, ct);
        var (result, error) = await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("registrar", "Banco XYZ"), null, ct);

        error.Should().BeNull();
        result!.Decision.Should().Be("registrar");
        _prendas.Rows.Count(r => r.Estado == PrendaEstado.Vigente).Should().Be(1);

        // La modificación (segunda llamada, con vigente previa) registra el evento de auditoría.
        await _instances.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "prenda_modificada" && e.ProcedureInstanceId == id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Modificar_en_estado_final_se_bloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        InstanceExists(id, tenant, TramiteEstado.Aprobado); // estado final

        var (result, error) = await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("registrar"), null, ct);

        error.Should().Be(TramiteEstadoErrores.EstadoFinal);
        result.Should().BeNull();
        _prendas.Rows.Should().BeEmpty();
    }
}
