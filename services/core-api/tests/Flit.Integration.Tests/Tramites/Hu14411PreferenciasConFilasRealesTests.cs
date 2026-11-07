using System.Text.Json;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.MarcaBlanca;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #14411 (Feature #14408) — con filas reales en <c>admin.company_document_params</c>, el checklist sale solo de
/// la regla base: los servicios se resuelven del contenedor real de la API sobre PostgreSQL, sin fakes.
/// <para>Esta clase vive solo mientras la tabla exista: la HU #14412 la elimina junto con la tabla.</para>
/// <para>Uso de ejemplo: con una fila OCULTO sobre un obligatorio, <c>GetChecklistHandler</c> sigue devolviéndolo obligatorio.</para>
/// </summary>
public sealed class Hu14411PreferenciasConFilasRealesTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string TipoConImpronta = "REGRABAR_MOTOR_CHASIS";
    private const string TipoConImprontaOpcional = "MATRICULA_LEASING";
    private const string Impronta = "impronta";
    private const string Oculto = "OCULTO";
    private const string Opcional = "OPCIONAL";
    private const string Obligatorio = "OBLIGATORIO";

    private static readonly Guid Compania = new("5a5a5a5a-0001-4000-8000-000000014411");
    private static readonly Guid Gestor = new("5a5a5a5a-0003-4000-8000-000000014411");
    private static readonly Guid Tramite = new("5a5a5a5a-0002-4000-8000-000000014411");

    private MarcaBlancaApiFactory? _factory;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (PostgresAvailability.IsAvailable)
            _factory = new MarcaBlancaApiFactory(Fixture);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await base.DisposeAsync();
    }

    // ── AC1 ─────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_Fila_OCULTO_sobre_un_obligatorio_El_documento_aparece_y_es_obligatorio()
    {
        await SembrarAsync(TipoConImprontaOpcional);
        var objetivo = PrimerObligatorio(await ChecklistAsync());

        await FilaAsync(objetivo.DocTipo!, Oculto);

        var checklist = await ChecklistAsync();
        checklist.Items.Should().ContainSingle(i => i.Key == objetivo.Key).Which.Obligatorio.Should().BeTrue();
    }

    // ── AC2 ─────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_Fila_OPCIONAL_sobre_un_obligatorio_sin_adjunto_La_radicacion_se_bloquea_por_documentos()
    {
        await SembrarAsync(TipoConImprontaOpcional);
        var baseItems = await ChecklistAsync();
        var objetivo = PrimerObligatorio(baseItems);
        await MarcarManualAsync(baseItems.Items.Where(i => i.Obligatorio && i.Key != objetivo.Key));
        await FilaAsync(objetivo.DocTipo!, Opcional);

        var outcome = await RadicarAsync();

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.DocumentosIncompletos);
    }

    // ── AC3 ─────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_Fila_OBLIGATORIO_sobre_un_opcional_sin_adjunto_No_bloquea()
    {
        await SembrarAsync(TipoConImprontaOpcional);
        var baseItems = await ChecklistAsync();
        var objetivo = baseItems.Items.First(i => !i.Obligatorio && i.DocTipo is not null);
        await MarcarManualAsync(baseItems.Items.Where(i => i.Obligatorio));
        await FilaAsync(objetivo.DocTipo!, Obligatorio);

        var outcome = await RadicarAsync();

        outcome.ErrorCode.Should().NotBe(TramiteEstadoErrores.DocumentosIncompletos);
        (await ChecklistAsync()).FaltanObligatorios.Should().BeEmpty();
    }

    // ── AC4 ─────────────────────────────────────────────────────────────────────────────────────

    [PostgresTheory]
    [InlineData(Opcional)]
    [InlineData(Oculto)]
    public async Task AC4_Fila_sobre_impronta_obligatoria_en_la_base_Se_exige_la_impronta(string estado)
    {
        await SembrarAsync(TipoConImpronta);
        var baseItems = await ChecklistAsync();
        baseItems.Items.Should().ContainSingle(i => i.DocTipo == Impronta).Which.Obligatorio.Should().BeTrue();
        await MarcarManualAsync(baseItems.Items.Where(i => i.Obligatorio));
        await FilaAsync(Impronta, estado);

        var outcome = await RadicarAsync();

        outcome.ErrorDetail.Should().Contain("impronta");
    }

    [PostgresFact]
    public async Task AC4_Fila_OBLIGATORIO_sobre_impronta_opcional_en_la_base_No_se_exige()
    {
        await SembrarAsync(TipoConImprontaOpcional);
        var baseItems = await ChecklistAsync();
        baseItems.Items.Should().ContainSingle(i => i.DocTipo == Impronta).Which.Obligatorio.Should().BeFalse();
        await MarcarManualAsync(baseItems.Items.Where(i => i.Obligatorio));
        await FilaAsync(Impronta, Obligatorio);

        var outcome = await RadicarAsync();

        (outcome.ErrorDetail ?? string.Empty).Should().NotContain("impronta");
    }

    [PostgresFact]
    public async Task AC4_Tipo_cuya_base_no_incluye_impronta_No_se_exige_aunque_haya_fila()
    {
        await SembrarAsync(await TipoSinImprontaAsync());
        var baseItems = await ChecklistAsync();
        baseItems.Items.Should().NotContain(i => i.DocTipo == Impronta);
        await MarcarManualAsync(baseItems.Items.Where(i => i.Obligatorio));
        await FilaAsync(Impronta, Obligatorio);

        var outcome = await RadicarAsync();

        (outcome.ErrorDetail ?? string.Empty).Should().NotContain("impronta");
    }

    // ── AC5 / AC8 ───────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_AC8_Con_filas_Los_consumidores_coinciden_con_la_regla_base()
    {
        await SembrarAsync(TipoConImprontaOpcional);
        var baseItems = await ChecklistAsync();
        var objetivo = PrimerObligatorio(baseItems);
        await MarcarManualAsync(baseItems.Items.Where(i => i.Obligatorio && i.Key != objetivo.Key));
        await FilaAsync(objetivo.DocTipo!, Opcional);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var ct = TestContext.Current.CancellationToken;
        var repo = sp.GetRequiredService<IProcedureInstanceRepository>();
        var instancia = (await repo.GetByIdWithWizardGraphAsync(Tramite, Compania, ct))!;

        // Checklist del wizard y del detalle.
        (await ChecklistAsync()).Completo.Should().BeFalse();
        // Servicio común de finalización, radicación, wizard estático y consolidado.
        var completitud = await sp.GetRequiredService<ChecklistMatrixCompleteness>().TryComputeAsync(instancia, Compania, ct);
        completitud!.FaltanObligatorios.Should().Equal(objetivo.Key);
        // Finalización del borrador.
        FinalizeDraftGate.Evaluate(instancia, completitud.Completo).Should().Contain(FinalizeDraftGate.DocumentosIncompletos);
        // Búsqueda rápida: sigue calculando la completitud (AC8).
        var filtro = await sp.GetRequiredService<BusquedaRapidaResolver>().AplicarAsync(
            new ProcedureInstanceListFilter(),
            BusquedaRapida.SinDocumento,
            (_, _, _) => Task.FromResult<(IReadOnlyList<ProcedureInstance>, int)>(([instancia], 1)),
            ct: ct);
        filtro.IdsIncluidos.Should().Contain(Tramite);
    }

    // ── AC6 ─────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC6_Con_filas_El_wizard_dinamico_y_el_envio_exigen_el_mismo_documento()
    {
        await SembrarAsync(TipoConImprontaOpcional);
        var baseItems = await ChecklistAsync();
        var objetivo = PrimerObligatorio(baseItems);
        await MarcarManualAsync(baseItems.Items.Where(i => i.Obligatorio && i.Key != objetivo.Key));
        await FilaAsync(objetivo.DocTipo!, Opcional);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var (wizard, error) = await scope.ServiceProvider.GetRequiredService<GetWizardStateHandler>()
            .HandleAsync(Tramite, Compania, TestContext.Current.CancellationToken);
        var envio = await RadicarAsync();

        error.Should().BeNull();
        wizard!.Blockers.Should().Contain($"DOCUMENT_{objetivo.DocTipo!.ToUpperInvariant()}_REQUIRED");
        envio.ErrorCode.Should().Be(TramiteEstadoErrores.DocumentosIncompletos);
    }

    // ── AC7 ─────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC7_Sin_filas_Lista_obligatoriedad_y_orden_salen_de_la_matriz()
    {
        await SembrarAsync(TipoConImprontaOpcional);

        var checklist = await ChecklistAsync();

        await using var scope = _factory!.Services.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var instancia = (await scope.ServiceProvider.GetRequiredService<IProcedureInstanceRepository>()
            .GetByIdWithChecklistGraphAsync(Tramite, Compania, ct))!;
        var matriz = (await scope.ServiceProvider.GetRequiredService<IResolvedChecklistMatrixProvider>()
                .GetForAsync(instancia.ProcedureTypeId, instancia.TransitOfficeId, ct))
            .Where(d => !d.EsGeneradoSistema)
            .ToList();

        var esperados = matriz.Select(d => (d.Codigo, d.Obligatorio)).ToList();
        var obtenidos = checklist.Items.Select(i => (Codigo: i.DocTipo!, i.Obligatorio)).ToList();
        obtenidos.Should().NotBeEmpty();
        obtenidos.Should().Equal(esperados.Where(e => obtenidos.Any(o => o.Codigo == e.Codigo)),
            "las reglas condicionales pueden quitar ítems, pero la lista conserva el orden y la obligatoriedad de la matriz");
    }

    // ── Soporte ─────────────────────────────────────────────────────────────────────────────────

    private static ChecklistItemDto PrimerObligatorio(ChecklistResponse checklist) =>
        checklist.Items.First(i => i.Obligatorio && i.DocTipo is not null && i.DocTipo != Impronta);

    private async Task SembrarAsync(string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var buscar = new NpgsqlCommand("SELECT id FROM tramites.procedure_types WHERE code = @code", conn);
        buscar.Parameters.AddWithValue("code", tipo);
        var tipoId = (Guid)(await buscar.ExecuteScalarAsync(ct))!;

        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(Compania, "IT-14411", isGroupParent: false, parentId: null));
        await ctx.SaveChangesAsync(ct);
        ctx.Users.Add(new User
        {
            Id = Gestor,
            Email = "it-14411@flit.test",
            DisplayName = "Gestor sintético 14411",
            Status = "active",
            HomeTenantId = Compania,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(ct);
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = Tramite,
            TenantId = Compania,
            ProcedureTypeId = tipoId,
            ReferenceNumber = "IT-14411",
            Status = TramiteEstado.Borrador,
            Vin = "SINTVIN14411",
            CreatedByUserId = Gestor,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(ct);
    }

    private async Task<string> TipoSinImprontaAsync()
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT pt.code
              FROM tramites.procedure_types pt
             WHERE EXISTS (SELECT 1 FROM tramites.procedure_document_requirements r WHERE r.procedure_type_id = pt.id)
               AND NOT EXISTS (
                   SELECT 1
                     FROM tramites.procedure_document_requirements r
                     JOIN tramites.document_types d ON d.id = r.document_type_id
                    WHERE r.procedure_type_id = pt.id AND d.code = 'impronta')
             ORDER BY pt.code
             LIMIT 1
            """, conn);
        return (string)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task FilaAsync(string documento, string estado)
    {
        await using var ctx = NewContext();
        ctx.CompanyDocumentParams.Add(new CompanyDocumentParamEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Compania,
            DocumentTypeCode = documento,
            State = estado,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Da por satisfechos los ítems con la marca manual del checklist, para aislar el documento bajo prueba.</summary>
    private async Task MarcarManualAsync(IEnumerable<ChecklistItemDto> items)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE tramites.procedure_instances SET checklist_estado = @estado::jsonb WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("estado", JsonSerializer.Serialize(items.ToDictionary(i => i.Key, _ => true)));
        cmd.Parameters.AddWithValue("id", Tramite);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<ChecklistResponse> ChecklistAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var (result, error) = await scope.ServiceProvider.GetRequiredService<GetChecklistHandler>()
            .HandleAsync(Tramite, Compania, TestContext.Current.CancellationToken);
        error.Should().BeNull();
        return result!;
    }

    private async Task<TramiteTransitionOutcome> RadicarAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITramiteLifecycleService>().TransitionAsync(
            new TramiteTransitionCommand(Tramite, Compania, TramiteEstado.Preparado, null, Gestor),
            TestContext.Current.CancellationToken);
    }
}
