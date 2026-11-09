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
/// HU #14411 (Feature #14408) — el checklist de una compañía sale solo de la regla base: los servicios se resuelven del
/// contenedor real de la API sobre PostgreSQL, sin fakes.
/// <para>Las pruebas con filas de preferencias salieron con la tabla (HU #14412); siguen en el commit de la HU #14411.</para>
/// <para>Uso de ejemplo: <c>GetChecklistHandler</c> devuelve la lista, la obligatoriedad y el orden de la matriz.</para>
/// </summary>
public sealed class Hu14411ChecklistSinPreferenciasTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string TipoConImprontaOpcional = "MATRICULA_LEASING";

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

    private async Task<ChecklistResponse> ChecklistAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var (result, error) = await scope.ServiceProvider.GetRequiredService<GetChecklistHandler>()
            .HandleAsync(Tramite, Compania, TestContext.Current.CancellationToken);
        error.Should().BeNull();
        return result!;
    }
}
