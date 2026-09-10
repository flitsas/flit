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

        public Task<IReadOnlyList<ProcedureInstancePrenda>> GetVigentesAsync(Guid instanceId, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProcedureInstancePrenda>>(
                Rows.Where(r => r.ProcedureInstanceId == instanceId && r.TenantId == tenantId && r.Estado == PrendaEstado.Vigente)
                    .ToList());

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
                ProcedureType = ProcedureTypeFixture.Matricula,
                Id = id,
                TenantId = tenantId,
                ProcedureTypeId = Guid.NewGuid(),
                ReferenceNumber = "TRM-2026-000001",
                Status = status,
                CreatedAt = DateTimeOffset.UtcNow,
            });

    // ── CF-06 (HU #10881) — "omitir" contra un OT que exige el certificado ──────────────────────

    private static IPrendaDocumentRequirementPolicy PolicyQueExige(bool required)
    {
        var policy = Substitute.For<IPrendaDocumentRequirementPolicy>();
        policy.IsRequiredAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(required);
        return policy;
    }

    /// <summary>
    /// El agujero que dejaba la regla del organismo evadible: con el override activo, "omitir"
    /// satisfacía a la vez el gate de gravámenes y el del OT, así que el gestor radicaba sin el
    /// certificado eligiendo "asumo el riesgo". Se corta AL ELEGIR, no al radicar.
    /// </summary>
    [Fact]
    public async Task Registrar_omitir_con_ot_que_exige_certificado_se_rechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        InstanceExists(id, tenantId);
        var registrar = new RegistrarPrendaHandler(_instances, _prendas, PolicyQueExige(true));

        var (result, error) = await registrar.HandleAsync(
            id, tenantId, new RegistrarPrendaInput(PrendaDecision.Omitir), null, ct);

        error.Should().Be(RegistrarPrendaHandler.OmitirNoAdmitidoError);
        result.Should().BeNull();
        _prendas.Rows.Should().BeEmpty("una decisión rechazada no puede quedar persistida");
    }

    /// <summary>Con el opt-out del OT vigente, "asumo el riesgo" sigue siendo una elección legítima.</summary>
    [Fact]
    public async Task Registrar_omitir_con_certificado_opcional_se_acepta()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        InstanceExists(id, tenantId);
        var registrar = new RegistrarPrendaHandler(_instances, _prendas, PolicyQueExige(false));

        var (result, error) = await registrar.HandleAsync(
            id, tenantId, new RegistrarPrendaInput(PrendaDecision.Omitir), null, ct);

        error.Should().BeNull();
        result!.Decision.Should().Be(PrendaDecision.Omitir);
    }

    /// <summary>
    /// La regla es de "omitir", no del override: las decisiones que gestionan la prenda —y
    /// <c>sin_prenda</c>, que declara que no hay— se guardan igual con el certificado exigido.
    /// </summary>
    [Theory]
    [InlineData(PrendaDecision.Registrar)]
    [InlineData(PrendaDecision.Levantar)]
    [InlineData(PrendaDecision.SinPrenda)]
    public async Task Registrar_otras_decisiones_no_las_toca_el_override(string decision)
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId) = (Guid.NewGuid(), Guid.NewGuid());
        InstanceExists(id, tenantId);
        var registrar = new RegistrarPrendaHandler(_instances, _prendas, PolicyQueExige(true));

        var (result, error) = await registrar.HandleAsync(
            id, tenantId, new RegistrarPrendaInput(decision), null, ct);

        error.Should().BeNull();
        result!.Decision.Should().Be(decision);
    }

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

    // ── AC4 (ADR-0055, HU #12129) — GetPrendaVigenteHandler devuelve 0-2 PrendaDto, no PrendaDto? ──

    [Fact]
    public async Task GetVigente_devuelve_la_ultima_decision_registrada()
    {
        // Matrícula (permiteComplementaria=false): "levantar" reemplaza a "solicitar" AUNQUE cambie
        // de familia (constitución→levantamiento) — comportamiento histórico intacto (AC3/AC7): a lo
        // sumo UNA vigente, así que la colección sigue trayendo un único elemento.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        InstanceExists(id, tenant);

        await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("solicitar"), null, ct);
        await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("levantar"), null, ct);

        var vigentes = await _get.HandleAsync(id, tenant, ct);

        vigentes.Should().ContainSingle();
        vigentes[0].Decision.Should().Be("levantar");
        vigentes[0].Estado.Should().Be(PrendaEstado.Vigente);
    }

    [Fact]
    public async Task GetVigente_sin_prenda_registrada_devuelve_coleccion_vacia()
    {
        var ct = TestContext.Current.CancellationToken;
        var vigentes = await _get.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);
        vigentes.Should().BeEmpty();
    }

    /// <summary>
    /// AC1/AC2/AC4 (ADR-0055) — PRENDA_INSCRIPCION admite constitución + levantamiento vigentes a la
    /// vez: GetPrendaVigenteHandler devuelve las DOS, cada una con su propio acreedor, y una nueva
    /// decisión de la MISMA familia solo re-versiona esa familia, sin afectar la otra.
    /// </summary>
    [Fact]
    public async Task GetVigente_con_accion_complementaria_devuelve_las_dos_familias_vigentes()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _instances.GetByIdAsync(id, tenant, Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstance
            {
                ProcedureType = ProcedureTypeFixture.PrendaInscripcion,
                Id = id,
                TenantId = tenant,
                ProcedureTypeId = Guid.NewGuid(),
                ReferenceNumber = "TRM-2026-000002",
                Status = TramiteEstado.Borrador,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("registrar", "Banco Nuevo", "900111222"), null, ct);
        await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("levantar", "Banco Viejo", "900333444"), null, ct);

        var vigentes = await _get.HandleAsync(id, tenant, ct);

        vigentes.Should().HaveCount(2, "AC1: ambos hechos quedan vigentes a la vez");
        vigentes.Should().Contain(p => p.Decision == "registrar" && p.AcreedorNombre == "Banco Nuevo");
        vigentes.Should().Contain(p => p.Decision == "levantar" && p.AcreedorNombre == "Banco Viejo");

        // AC2: re-versionar SOLO la constitución no toca el levantamiento vigente.
        await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("registrar", "Banco Nuevo 2", "900555666"), null, ct);
        var trasReversion = await _get.HandleAsync(id, tenant, ct);

        trasReversion.Should().HaveCount(2);
        trasReversion.Should().Contain(p => p.Decision == "registrar" && p.AcreedorNombre == "Banco Nuevo 2");
        trasReversion.Should().Contain(p => p.Decision == "levantar" && p.AcreedorNombre == "Banco Viejo",
            "la familia de levantamiento no debe verse afectada por el re-versionado de la constitución");
        _prendas.Rows.Count(r => r.Estado == PrendaEstado.Reemplazada).Should().Be(1,
            "solo la vigente de la MISMA familia (constitución) se reemplaza");
    }

    /// <summary>AC3 — Matrícula/Traspaso rechazan explícitamente una segunda decisión vigente (regla de negocio, no solo el índice de BD).</summary>
    [Theory]
    [InlineData("MATRICULA_NUEVA")]
    [InlineData("TRASPASO_STANDARD")]
    public async Task Registrar_en_tipo_no_dual_con_dos_vigentes_anomalas_se_rechaza(string code)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _instances.GetByIdAsync(id, tenant, Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstance
            {
                ProcedureType = ProcedureTypeFixture.For(code),
                Id = id,
                TenantId = tenant,
                ProcedureTypeId = Guid.NewGuid(),
                ReferenceNumber = "TRM-2026-000003",
                Status = TramiteEstado.Borrador,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        // Estado anómalo (no debería ocurrir bajo flujo normal): dos vigentes de familias distintas
        // ya persistidas para un tipo que NO admite la acción complementaria.
        _prendas.Rows.Add(new ProcedureInstancePrenda
        {
            Id = Guid.NewGuid(), TenantId = tenant, ProcedureInstanceId = id,
            Decision = "registrar", Estado = PrendaEstado.Vigente, AccionFamilia = "constitucion",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _prendas.Rows.Add(new ProcedureInstancePrenda
        {
            Id = Guid.NewGuid(), TenantId = tenant, ProcedureInstanceId = id,
            Decision = "levantar", Estado = PrendaEstado.Vigente, AccionFamilia = "levantamiento",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var (result, error) = await _registrar.HandleAsync(id, tenant, new RegistrarPrendaInput("sin_prenda"), null, ct);

        error.Should().Be(RegistrarPrendaHandler.SegundaDecisionVigenteNoAdmitidaError);
        result.Should().BeNull();
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
