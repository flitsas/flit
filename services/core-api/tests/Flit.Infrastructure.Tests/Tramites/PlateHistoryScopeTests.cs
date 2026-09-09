using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Tramites;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Flit.Infrastructure.Tests.Tramites;

/// <summary>
/// Historial operativo por placa (Feature #12189, HU #12192) — la decisión de ALCANCE y las
/// garantías del contrato, aisladas del acceso a datos.
///
/// <para>
/// Uso de ejemplo:
/// <code>
/// var alcance = PlateHistoryScope.Resolve(contextTenantId: tenantDelUsuario, isSuperAdmin: false);
/// var request = PlateHistoryScope.BuildRequest(alcance, PlacaNormalizer.Normalize(" abc123 "), 0, 20);
/// </code>
/// </para>
///
/// <para>
/// Lo que estas pruebas custodian es un fallo SILENCIOSO: el repositorio aplica el filtro de tenant
/// solo <c>if (tenantId is { } tid)</c>, así que un <c>null</c> que se cuele para un rol que no es
/// SuperAdmin no lanza ni devuelve 500 — devuelve trámites de otras compañías con aspecto de
/// resultado correcto. Por eso el alcance se afirma sobre el objeto que decide, y no solo sobre el
/// resultado de una consulta.
/// </para>
/// </summary>
public sealed class PlateHistoryScopeTests
{
    private const string HistorialPlacaSlug = "historial-placa.read";
    private static readonly Guid TenantUsuario = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ── Alcance por rol (decisión D1 del PO) ──────────────────────────────────────────────────

    [Fact]
    public void SuperAdmin_ObtieneAlcanceGlobal_SinFiltroDeCompania()
    {
        var alcance = PlateHistoryScope.Resolve(contextTenantId: null, isSuperAdmin: true);

        alcance.Allowed.Should().BeTrue();
        alcance.IsGlobal.Should().BeTrue();
        alcance.TenantId.Should().BeNull("null = todas las compañías, y SOLO el SuperAdmin puede pedirlo");
    }

    [Fact]
    public void SuperAdminConTenantEnContexto_SigueSiendoGlobal()
    {
        // Un X-Tenant-Id de acotación no convierte el historial en el de una sola empresa: el
        // criterio del PO para este módulo es "la placa en todas las compañías".
        var alcance = PlateHistoryScope.Resolve(contextTenantId: TenantUsuario, isSuperAdmin: true);

        alcance.IsGlobal.Should().BeTrue();
        alcance.TenantId.Should().BeNull();
    }

    [Fact]
    public void AdminCompany_QuedaAcotadoASuPropiaCompania()
    {
        var alcance = PlateHistoryScope.Resolve(contextTenantId: TenantUsuario, isSuperAdmin: false);

        alcance.Allowed.Should().BeTrue();
        alcance.IsGlobal.Should().BeFalse();
        alcance.TenantId.Should().Be(TenantUsuario);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void NoSuperAdminSinCompaniaResoluble_SeRechazaCon403_NuncaConAlcanceGlobal(string? tenant)
    {
        Guid? contextTenantId = tenant is null ? null : Guid.Parse(tenant);

        var alcance = PlateHistoryScope.Resolve(contextTenantId, isSuperAdmin: false);

        alcance.Allowed.Should().BeFalse("ante la duda el alcance se cierra, no se abre");
        alcance.IsGlobal.Should().BeFalse();
        alcance.StatusCode.Should().Be(403);
        alcance.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NingunAlcanceNoSuperAdmin_PuedeProducirTenantNull()
    {
        // Barrido de las combinaciones posibles: el único null autorizado es el del SuperAdmin.
        foreach (var (tenantId, isSuperAdmin) in new (Guid?, bool)[]
                 {
                     (null, false), (TenantUsuario, false), (Guid.Empty, false), (TenantUsuario, true),
                 })
        {
            var alcance = PlateHistoryScope.Resolve(tenantId, isSuperAdmin);
            if (alcance is { Allowed: true, TenantId: null })
                isSuperAdmin.Should().BeTrue(
                    "solo un SuperAdmin puede recibir un alcance sin filtro de compañía");
        }
    }

    // ── Contrato de la consulta que arma el endpoint ──────────────────────────────────────────

    [Fact]
    public void BuildRequest_FijaOrdenCronologicoDescendente_SinDependerDelCliente()
    {
        var alcance = PlateHistoryScope.Resolve(TenantUsuario, isSuperAdmin: false);

        var request = PlateHistoryScope.BuildRequest(alcance, "ABC123", skip: null, take: null);

        request.SortBy.Should().Be("createdAt");
        request.SortDescending.Should().BeTrue();
        ProcedureInstanceSortFields.Resolve(request.SortBy)
            .Should().Be(ProcedureInstanceSortBy.CreatedAt, "el valor debe estar en la whitelist");
    }

    [Fact]
    public void BuildRequest_PropagaElTenantDelAlcance()
    {
        var acotado = PlateHistoryScope.BuildRequest(
            PlateHistoryScope.Resolve(TenantUsuario, isSuperAdmin: false), "ABC123", 0, 20);
        var global = PlateHistoryScope.BuildRequest(
            PlateHistoryScope.Resolve(null, isSuperAdmin: true), "ABC123", 0, 20);

        acotado.TenantId.Should().Be(TenantUsuario);
        global.TenantId.Should().BeNull();
    }

    [Theory]
    [InlineData(null, ListProcedureInstancesHandler.MaxItems)]
    [InlineData(0, ListProcedureInstancesHandler.MaxItems)]
    [InlineData(-5, ListProcedureInstancesHandler.MaxItems)]
    [InlineData(ListProcedureInstancesHandler.MaxItems + 1, ListProcedureInstancesHandler.MaxItems)]
    [InlineData(25, 25)]
    public void BuildRequest_AcotaElTamanoDePagina(int? take, int esperado)
    {
        var request = PlateHistoryScope.BuildRequest(
            PlateHistoryScope.Resolve(TenantUsuario, false), "ABC123", skip: null, take: take);

        request.Take.Should().Be(esperado);
    }

    [Fact]
    public void BuildRequest_SkipNegativo_SeTrataComoCero()
    {
        var request = PlateHistoryScope.BuildRequest(
            PlateHistoryScope.Resolve(TenantUsuario, false), "ABC123", skip: -10, take: 20);

        request.Skip.Should().Be(0);
    }

    [Fact]
    public void BuildRequest_ConAlcanceRechazado_Lanza()
    {
        var rechazado = PlateHistoryScope.Resolve(contextTenantId: null, isSuperAdmin: false);

        var acto = () => PlateHistoryScope.BuildRequest(rechazado, "ABC123", 0, 20);

        acto.Should().Throw<InvalidOperationException>(
            "un alcance no autorizado nunca puede convertirse en consulta");
    }

    // ── Normalización de la placa en el SERVIDOR ──────────────────────────────────────────────

    [Theory]
    [InlineData("ABC123")]
    [InlineData("abc123")]
    [InlineData("  abc123  ")]
    [InlineData("AbC123 ")]
    public void PlacaNormalizada_SiempreEnMayusculasYSinEspacios(string entrada)
    {
        PlacaNormalizer.NormalizeOrNull(entrada).Should().Be("ABC123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PlacaAusente_SeDistingueDeUnaPlacaVacia(string? entrada)
    {
        // null (y no "") para que el endpoint pueda responder 400 en vez de filtrar por cadena
        // vacía, que traería TODO el listado de la compañía disfrazado de historial.
        PlacaNormalizer.NormalizeOrNull(entrada).Should().BeNull();
    }

    // ── Permiso historial-placa.read (sin el permiso → 403) ───────────────────────────────────

    private static AuthorizationHandlerContext ContextoDe(params Claim[] claims) =>
        new([new PermissionRequirement(HistorialPlacaSlug)],
            new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            resource: null);

    [Fact]
    public async Task SinElPermiso_NoSeSatisfaceElRequirement_ElPipelineResponde403()
    {
        var contexto = ContextoDe(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "AdminCompany"),
            new Claim("permissions", "tramites.read"));

        await new PermissionAuthorizationHandler().HandleAsync(contexto);

        contexto.HasSucceeded.Should().BeFalse(
            "sin historial-placa.read el endpoint responde 403, no un historial recortado");
    }

    [Fact]
    public async Task ConElPermiso_SeSatisfaceElRequirement()
    {
        var contexto = ContextoDe(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "Radicador"),
            new Claim("permissions", HistorialPlacaSlug));

        await new PermissionAuthorizationHandler().HandleAsync(contexto);

        contexto.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task SuperAdminSinElSlug_PasaPorBypassDeRol()
    {
        var contexto = ContextoDe(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "SuperAdmin"));

        await new PermissionAuthorizationHandler().HandleAsync(contexto);

        contexto.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Anonimo_NoSatisfaceElRequirement_ElPipelineResponde401()
    {
        var contexto = new AuthorizationHandlerContext(
            [new PermissionRequirement(HistorialPlacaSlug)],
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null);

        await new PermissionAuthorizationHandler().HandleAsync(contexto);

        contexto.HasSucceeded.Should().BeFalse();
    }
}
