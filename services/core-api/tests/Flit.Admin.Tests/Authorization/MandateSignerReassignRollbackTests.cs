using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;


using Flit.Tramites.Domain.Entities;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13137 (Feature #13115) — sobre PostgreSQL y el endpoint real: la baja reasigna los trámites radicados sin
/// aprobar (sin tocar borradores ni aprobados) y, si la reasignación falla, se revierte toda la transacción.
/// </summary>
public sealed class MandateSignerReassignRollbackTests(WebApplicationFactory<Program> factory)
    : MandateSignerEndpointsTestBase(factory)
{
    private readonly List<Guid> _procedureIds = [];

    [Fact]
    public async Task HU13137_AC1_sobre_PostgreSQL_reasigna_los_radicados_y_no_toca_borrador_ni_aprobado()
    {
        var x = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        var deLaCompania = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        var entregado = await SeedProcedureAsync(x, "entregado");
        var borrador = await SeedProcedureAsync(x, "borrador");
        var aprobado = await SeedProcedureAsync(x, "aprobado");
        AuthenticateOt();

        var response = await _client.DeleteAsync(Hub(x, "?confirmarImpacto=true"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        response.Headers.GetValues("X-Mandatario-Reasignados").Single().Should().Be("1");
        response.Headers.GetValues("X-Mandatario-Pendientes-Decision-OT").Single().Should().Be("0");

        await using var db = NewDb();
        var filas = await db.ProcedureInstances.AsNoTracking()
            .Where(p => _procedureIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.MandateSignerId, Ct);
        filas[entregado].Should().Be(deLaCompania, "el radicado sin aprobar se reasigna con la prelación");
        filas[borrador].Should().Be(x, "el borrador se recalcula solo");
        filas[aprobado].Should().Be(x, "el aprobado conserva quién firmó");
    }

    [Fact]
    public async Task HU13137_AC4_sobre_PostgreSQL_si_nadie_resuelve_queda_en_nulo_y_el_conteo_lo_informa()
    {
        var x = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        var entregado = await SeedProcedureAsync(x, "entregado");
        AuthenticateOt();

        var response = await _client.DeleteAsync(Hub(x, "?confirmarImpacto=true"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        response.Headers.GetValues("X-Mandatario-Reasignados").Single().Should().Be("0");
        response.Headers.GetValues("X-Mandatario-Pendientes-Decision-OT").Single().Should().Be("1");
        await using var db = NewDb();
        (await db.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == entregado, Ct))
            .MandateSignerId.Should().BeNull("el OT decide al aprobar");
    }

    public override void Dispose()
    {
        // Los trámites sembrados referencian al tenant y al mandatario: se borran antes que la base.
        using (var db = NewDb())
        {
            db.ProcedureInstances.Where(p => _procedureIds.Contains(p.Id)).ExecuteDelete();
        }

        base.Dispose();
    }

    private async Task<Guid> SeedProcedureAsync(Guid signerId, string status)
    {
        await using var db = NewDb();
        var typeId = await db.ProcedureTypes.AsNoTracking().Select(t => t.Id).FirstAsync(Ct);
        var id = Guid.NewGuid();
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = _companyA,
            ProcedureTypeId = typeId,
            ReferenceNumber = $"T13137-{id:N}"[..24],
            Status = status,
            TransitOfficeId = _officeA,
            MandateSignerId = signerId,
            CreatedByUserId = _companyAUser,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
        _procedureIds.Add(id);
        return id;
    }

    [Fact]
    public async Task HU13137_AC7_si_falla_la_reasignacion_se_revierte_toda_la_transaccion_y_el_mandatario_sigue_activo()
    {
        var signer = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        await SeedDefaultsAsync(signer);

        using var fallido = _factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IMandateSignerProcedureReassigner>();
            services.AddScoped<IMandateSignerProcedureReassigner, ReasignadorQueFalla>();
        }));
        var client = fallido.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("ot_admin", _otTenant, _otAdminUser, AdminAuthorization.TransitOfficeEntityType));

        HttpStatusCode? status = null;
        try
        {
            status = (await client.DeleteAsync(Hub(signer, "?confirmarImpacto=true"), Ct)).StatusCode;
        }
        catch (InvalidOperationException)
        {
            // TestServer propaga la excepción no controlada: equivale al 500 de producción.
        }

        if (status is { } code)
        {
            ((int)code).Should().BeGreaterThanOrEqualTo(500);
        }

        ReasignadorQueFalla.Invocaciones.Should().BeGreaterThan(0, "la baja llegó hasta la reasignación y falló ahí");

        await AssertSignerUntouchedAsync(signer);
        await using var db = NewDb();
        (await db.CompanyOtMandateRules.AsNoTracking().SingleAsync(r => r.TransitOfficeId == _officeA, Ct))
            .DefaultMandateSignerId.Should().Be(signer, "el retiro de defaults se revierte con la baja");
        (await db.MandateSignerCompanies.AsNoTracking().Where(c => c.MandateSignerId == signer).ToListAsync(Ct))
            .Should().OnlyContain(c => c.IsActive);
        (await db.TenantConfigAuditLogs.CountAsync(l => l.TargetEntityId == signer, Ct)).Should().Be(0);
    }

    private sealed class ReasignadorQueFalla : IMandateSignerProcedureReassigner
    {
        public static int Invocaciones;

        public Task<MandateSignerReassignmentResult> ReassignAsync(
            Guid mandateSignerId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Invocaciones);
            throw new InvalidOperationException("falla simulada al reasignar un trámite");
        }
    }
}
