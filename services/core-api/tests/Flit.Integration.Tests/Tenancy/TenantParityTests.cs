using System.Reflection;
using Flit.Admin.Application.Companies.NotificationDeliveryLogs;
using Flit.Admin.Domain.Auditing;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.CompanyDocumentParams;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtMetrics;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries.Metrics;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Domain.Auth;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tenancy;

/// <summary>
/// HU #12322 — suite de PARIDAD sobre <see cref="HierarchyScenario"/>:
/// <list type="bullet">
///   <item>AC1 — el cliente aislado S (sin padre ni hijos) obtiene de cada consulta exactamente sus
///   filas de siempre, con el filtro legado (<c>tenantId</c>) y con <c>TenantScope.Single(S)</c>
///   (mismos ids, mismo orden, mismo conteo), y ningún DTO de respuesta incorpora campos nuevos.</item>
///   <item>AC5 — SuperAdmin (<c>tenantId = null</c> / <c>TenantScope.All</c>) sigue viendo filas de
///   P, C1, C2, X y S: el conteo total del escenario.</item>
/// </list>
/// <para>
/// Uso de ejemplo: <c>CoveredQueries.RunAsync("Q01", ctx, HierarchyScenario.S)</c> debe devolver los
/// dos trámites de S; <c>CoveredQueries.RunAsync("Q01", ctx, null)</c> los diez del escenario.
/// </para>
/// </summary>
public sealed class TenantParityTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    public static IEnumerable<TheoryDataRow<string>> ConsultasConTenant() =>
        CoveredQueries.All.Where(q => !q.TenantOnly).Select(q => new TheoryDataRow<string>(q.Id));

    public static IEnumerable<TheoryDataRow<string>> ConsultasGlobales() =>
        CoveredQueries.All.Where(q => q.SupportsGlobal).Select(q => new TheoryDataRow<string>(q.Id));

    /// <summary>AC1 — cliente sin jerarquía: cada consulta devuelve exactamente sus filas propias.</summary>
    [PostgresTheory]
    [MemberData(nameof(ConsultasConTenant))]
    public async Task Cliente_aislado_obtiene_exactamente_lo_suyo(string queryId)
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var query = CoveredQueries.Get(queryId);

        await using var ctx = NewContext();
        var outcome = await CoveredQueries.RunAsync(queryId, ctx, HierarchyScenario.S);

        if (outcome.RowIds is { } rows)
        {
            rows.Should().HaveCount(query.OwnRows, $"{query} para S");
            rows.Where(id => HierarchyScenario.OwnerOf(id) != HierarchyScenario.S).Should().BeEmpty($"{query} para S no puede traer filas ajenas");
        }
        else
        {
            outcome.Scalar.Should().Be(query.OwnRows, $"{query} para S");
        }
    }

    /// <summary>AC5 — SuperAdmin: las consultas con modo global agregan los cinco clientes (P, C1, C2, X, S).</summary>
    [PostgresTheory]
    [MemberData(nameof(ConsultasGlobales))]
    public async Task SuperAdmin_sigue_viendo_todos_los_clientes(string queryId)
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var query = CoveredQueries.Get(queryId);
        var total = query.OwnRows * HierarchyScenario.Clients.Count;

        await using var ctx = NewContext();
        var outcome = await CoveredQueries.RunAsync(queryId, ctx, reader: null);

        if (outcome.RowIds is { } rows)
        {
            rows.Should().HaveCount(query.Id == "Q04" ? HierarchyScenario.Clients.Count : total, $"{query} global");
            rows.Select(HierarchyScenario.OwnerOf).Distinct().Should().BeEquivalentTo(HierarchyScenario.Clients,
                $"{query} global debe incluir filas de los cinco clientes");
        }
        else
        {
            outcome.Scalar.Should().Be(total, $"{query} global");
        }
    }

    /// <summary>
    /// AC1 — el filtro legado (<c>tenantId = S</c>) y <c>TenantScope.Single(S)</c> sobre la misma
    /// consulta base producen los mismos ids en el mismo orden (Q01 y Q02 ≡ Q27).
    /// </summary>
    [PostgresFact]
    public async Task Filtro_legado_y_TenantScope_Single_son_equivalentes()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);

        var legado = (await repo.ListWithSummaryGraphAsync(HierarchyScenario.S, 100, CancellationToken.None)).Select(p => p.Id).ToList();
        var (filtrado, total) = await repo.ListWithSummaryGraphFilteredAsync(
            HierarchyScenario.S, 0, 100, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.Default, SortDirection.Descending, CancellationToken.None);

        var conScope = await ctx.ProcedureInstances.AsNoTracking()
            .WhereTenantInScope(TenantScope.Single(HierarchyScenario.S), p => p.TenantId)
            .Where(p => p.DeletedAt == null)
            .OrderByDescending(p => p.Prioritario)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => p.Id)
            .ToListAsync();

        legado.Should().Equal(conScope, "mismos ids y mismo orden");
        filtrado.Select(p => p.Id).Should().BeEquivalentTo(conScope);
        total.Should().Be(conScope.Count);
        legado.Should().HaveCount(2);
    }

    /// <summary>AC5 — <c>TenantScope.All</c> y <c>tenantId = null</c> producen el mismo universo.</summary>
    [PostgresFact]
    public async Task TenantScope_All_equivale_a_tenantId_null()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();

        var legado = (await new ProcedureInstanceRepository(ctx).ListWithSummaryGraphAsync(null, 100, CancellationToken.None)).Select(p => p.Id).ToList();
        var conScope = await ctx.ProcedureInstances.AsNoTracking()
            .WhereTenantInScope(TenantScope.All(), p => p.TenantId)
            .Where(p => p.DeletedAt == null)
            .OrderByDescending(p => p.Prioritario)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => p.Id)
            .ToListAsync();

        legado.Should().Equal(conScope).And.HaveCount(10);
    }

    /// <summary>AC5 — SuperAdmin conserva la gestión de configuración de cualquier cliente (lee la ficha de los cinco).</summary>
    [PostgresFact]
    public async Task SuperAdmin_lee_la_ficha_de_cualquier_cliente()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new CompanyReadRepository(ctx);

        foreach (var tenant in HierarchyScenario.Clients)
            (await repo.GetByIdAsync(tenant))!.Id.Should().Be(tenant);

        var listado = await repo.ListAsync(new CompanyListFilter { Page = 1, PageSize = 100 });
        listado.Items.Select(i => i.Id).Should().Contain(HierarchyScenario.Clients);
    }

    /// <summary>
    /// AC1 — «ninguna respuesta incorpora campos nuevos»: snapshot por reflexión de las propiedades
    /// públicas de los DTOs de respuesta de las consultas cubiertas. <c>ParentTenantId</c> /
    /// <c>IsGroupParent</c> viven en la entidad <c>Tenant</c>, no en estos contratos. Si un DTO gana
    /// una propiedad, esta prueba falla y hay que decidir a conciencia (no ajustar la lista sin más).
    /// </summary>
    public static IEnumerable<TheoryDataRow<Type, string>> ContratosDeRespuesta()
    {
        yield return new(typeof(CompanyListItem), "Id Nit RazonSocial Code TenantType IsTransitOffice EstadoActivo FechaCreacion RowVersion");
        yield return new(typeof(InstanceSummaryDto), "Id ReferenceNumber Modalidad Estado Placa Vin VehiculoMarca VehiculoLinea CompradorNombre CompradorDocumento OrganismoTransito PasoActual TotalPasos CreatedAt TenantId CompaniaNombre DraftFinalizedAt IdentityValidationStatus SignaturePending CanSubmit Prioritario VendedorNombre VendedorDocumento SubsanacionActiva SubsanacionCount UltimoRechazoMotivo IsPaused PausedObservation Origin PlateFlowStatus UpdatedAt GestorNombre Fuente FirmaVendedorEstado FirmaCompradorEstado ConsolidadoAttachmentId TipoNombre TipoCodigo PasoNombre TienePrenda TieneTransformacion");
        yield return new(typeof(OtClientProcedure), "Id ClientTenantId ProcedureTypeId ProcedureTypeName ClientTenantName ReferenceNumber Status Familia PlateFlowStatus PlateAssignedAt PlateUpdatedAt SoatEstado PlatePreferredLastDigit SoatPagado ImpuestoDepartamentalPagado TransitOfficeId CreatedAt SubmittedAt Prioritario Actors Placa Vin VendedorNombre CompradorNombre GestorNombre Marca Linea Modelo Color Clase Servicio Combustible Carroceria Cilindraje Capacidad Ejes EstadoVehiculo NumeroMotor NumeroChasis NumeroSerie RuntSnapshot TransformacionesDeclaradas Comercial Prenda");
        yield return new(typeof(CompanyQueryRowDto), "ProcedureInstanceId ReferenceNumber Placa Vin TransitOfficeId TransitOfficeName CompaniaId CompaniaNombre ProcedureTypeId ProcedureTypeName Status Prioritario SubsanacionActiva SubsanacionCount Comprador Vendedor TienePrenda AcreedorPrenda TieneLicenciaTransito Transformaciones EsLeasing MetodoPago TipoTraspaso RadicadoPor CreadoEn EnviadoEn CerradoEn AprobadoEn ActualizadoEn DiasHastaEnvio DiasEnOrganismo Devoluciones");
        yield return new(typeof(ProcedureDetailDto), "Id ReferenceNumber ProcedureTypeName Category Status CreatedByDisplayName SubmittedAt CompletedAt");
        yield return new(typeof(DetailedProcedureRowDto), "Id ReferenceNumber ProcedureTypeName Category Status CreatedByDisplayName SubmittedAt CompletedAt PersonDocument PersonFullName HasTransformation TransformationDetail IsLeasing PaymentType TransferType");
        yield return new(typeof(AdminAuditLogEntry), "Id TenantId TenantType Module EntityName Operation Result ErrorCode ChangedBy TargetEntityType TargetEntityId ClientIp ChangedAt ChangedByName ChangedByEmail TargetName FieldName OldValue NewValue");
        yield return new(typeof(NotificationDeliveryLogRecord), "Id TemplateKey Channel Recipient Result FailureReason DurationMs OccurredAt");
        yield return new(typeof(CompanyDocumentParamItem), "Id TenantId DocumentTypeCode State");
        yield return new(typeof(PendingInvitationSummary), "InvitationId Email FullName CreatedAt");
        yield return new(typeof(TopProducerDto), "UserId DisplayName SubmittedCount ApprovedCount RejectedCount");
        yield return new(typeof(OtClientCompanyOptionDto), "TenantId Name");
        yield return new(typeof(TramitesFilterOptions), "Organismos Tipos MetodosPago Companias");
        yield return new(typeof(PlacaTramiteExistente), "Id Estado Placa Vin FechaRegistro SubsanacionActiva");
        yield return new(typeof(CategoryMetricsDto), "Category Total ByStatus");
        yield return new(typeof(MonthlyTrendPointDto), "Year Month Category Total");
        yield return new(typeof(LiveOverviewDataDto), "Today StuckCount PendingIdentityValidations IntegrationsLastHour LastActivityAt");
        yield return new(typeof(FunnelCoreDto), "States Anulados RechazadosVigentes");
    }

    [Theory]
    [MemberData(nameof(ContratosDeRespuesta))]
    public void Los_DTOs_de_respuesta_no_incorporan_campos_nuevos(Type dto, string expected)
    {
        var actual = dto.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name);

        string.Join(' ', actual).Should().Be(expected, $"{dto.Name} no debe exponer campos nuevos de jerarquía");
        actual.Should().NotContain(["ParentTenantId", "IsGroupParent", "ReadTenantIds"]);
    }
}
