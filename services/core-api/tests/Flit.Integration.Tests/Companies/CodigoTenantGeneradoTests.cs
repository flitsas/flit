using System.Text.RegularExpressions;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Companies.Create;
using Flit.Admin.Domain.Companies.TransitOffices.Create;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Companies;

/// <summary>
/// HU #13605 (Feature #13602, Épica #13517) — el código del tenant lo asigna Postgres al crear una compañía, una
/// compañía hija o el tenant de un OT: consecutivo <c>COMP-XXXX</c> de <c>identity.tenant_code_seq</c>, leído de vuelta
/// por EF. Corre sobre la base efímera con TODAS las migraciones aplicadas (incluida la 136 de la HU #13604).
/// <para>
/// Uso de ejemplo: <c>await new CreateCompanyHandler(new CompanyWriteRepository(ctx)).HandleAsync(cmd)</c> sin
/// <c>code</c> → <c>result.Company.Code</c> = <c>COMP-0001</c> (o el siguiente de la secuencia).
/// </para>
/// </summary>
public sealed partial class CodigoTenantGeneradoTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    [GeneratedRegex("^COMP-([0-9]{4,})$")]
    private static partial Regex Consecutivo();

    private static long Numero(string code)
    {
        var match = Consecutivo().Match(code);
        match.Success.Should().BeTrue($"«{code}» debe tener el formato COMP-XXXX");
        return long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<CreateCompanyResult> CrearCompaniaAsync(string razonSocial, string nit, string tenantType = "RENTING")
    {
        await using var ctx = NewContext();
        return await new CreateCompanyHandler(new CompanyWriteRepository(ctx)).HandleAsync(
            new CreateCompanyCommand { Request = new CreateCompanyRequest(razonSocial, nit, tenantType, true) },
            TestContext.Current.CancellationToken);
    }

    // ── AC1 — positivo ───────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_LaCompaniaNuevaRecibeElSiguienteConsecutivoYLaRespuestaLoIncluye()
    {
        var primera = await CrearCompaniaAsync("Renting Uno S.A.S.", "905000001-1");
        var segunda = await CrearCompaniaAsync("Renting Dos S.A.S.", "905000002-2");

        primera.IsValid.Should().BeTrue();
        segunda.IsValid.Should().BeTrue();
        Numero(segunda.Company!.Code).Should().Be(Numero(primera.Company!.Code) + 1, "la secuencia avanza de uno en uno");

        // El código de la respuesta es el que quedó en la fila (EF lo leyó de vuelta tras el INSERT).
        await using var check = NewContext();
        (await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == segunda.Company.Id, TestContext.Current.CancellationToken))
            .Code.Should().Be(segunda.Company.Code);
    }

    // ── AC2 — compañía hija y OT ─────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_LaCompaniaHijaYElTenantDeOtUsanLaMismaSecuencia()
    {
        var ct = TestContext.Current.CancellationToken;
        var cabeza = await CrearCompaniaAsync("Concesión Cabeza S.A.S.", "905000003-3", "CONCESION");
        cabeza.IsValid.Should().BeTrue();

        string codigoHija;
        await using (var ctx = NewContext())
        {
            var hija = await new CompanyHierarchyRepository(ctx).CreateChildAsync(
                new NewChildCompany("Hija S.A.S.", "905000004-4", "CONCESIONARIO", true, cabeza.Company!.Id, null), ct);
            codigoHija = hija.Code;
        }

        var oficina = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.TransitOffices.Add(new TransitOffice
            {
                Id = oficina,
                Code = "9900100",
                Name = "SECRETARIA DE MOVILIDAD DE PRUEBA 13605",
                DepartmentCode = "99",
                CityCode = "99001",
                IsActive = true,
            });
            await ctx.SaveChangesAsync(ct);
        }

        string codigoOt;
        await using (var ctx = NewContext())
        {
            var ot = await new TransitOfficeTenantWriteRepository(ctx, NullAuditContextAccessor.Instance).CreateAsync(
                new NewTransitOffice(oficina, "OT Prueba 13605", "905000005-5", "dashboard", null), ct);
            codigoOt = ot.Code;
        }

        Numero(codigoHija).Should().Be(Numero(cabeza.Company.Code) + 1);
        Numero(codigoOt).Should().Be(Numero(codigoHija) + 1);
    }

    // ── AC3 — concurrencia ───────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_CreacionesSimultaneasObtienenCodigosDistintosYConsecutivos()
    {
        const int total = 8;
        var resultados = await Task.WhenAll(Enumerable.Range(1, total)
            .Select(i => CrearCompaniaAsync($"Simultánea {i} S.A.S.", $"9051000{i:D2}-{i % 10}")));

        resultados.Should().OnlyContain(r => r.IsValid);
        var numeros = resultados.Select(r => Numero(r.Company!.Code)).Order().ToList();

        numeros.Should().OnlyHaveUniqueItems();
        (numeros[^1] - numeros[0]).Should().Be(total - 1, "sin huecos ni repetidos cuando ninguna falla");
    }

    // ── AC5 — creación fallida ───────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_UnaCreacionRechazadaNoCreaLaCompania()
    {
        var ct = TestContext.Current.CancellationToken;
        var original = await CrearCompaniaAsync("Original S.A.S.", "905000006-6");
        original.IsValid.Should().BeTrue();

        var duplicada = await CrearCompaniaAsync("Duplicada S.A.S.", "905000006-6");

        duplicada.IsValid.Should().BeFalse();
        duplicada.Errors.Should().Contain(e => e.Field == "nit");
        await using var check = NewContext();
        (await check.Tenants.AsNoTracking().CountAsync(t => t.TaxId == "905000006-6", ct)).Should().Be(1);

        // La siguiente creación válida sigue la numeración (un salto sería aceptable; aquí no lo hay porque el
        // rechazo ocurre en la validación, antes de pedir un número a la secuencia).
        var siguiente = await CrearCompaniaAsync("Siguiente S.A.S.", "905000007-7");
        Numero(siguiente.Company!.Code).Should().BeGreaterThan(Numero(original.Company!.Code));
    }
}
