using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// HU #13121 (Epic #13090, F1) + HU #13247 (Feature #13245) — la identidad de un mandatario es SOLO la de su validación
/// propia (party_role <c>mandatario</c> + referencia a su ficha), registrada en el tenant de la COMPAÑÍA que lo registró (el
/// módulo Identidad devuelve 403 a los usuarios del OT). La aprobación de una prevalidación, un comprador o un vendedor con la
/// misma cédula NO cuenta. PostgreSQL real. <para>Uso de ejemplo: con una validación propia aprobada en el tenant C2 y un
/// mandatario de C2 en Ot1, <c>new DbMandateSignerReader(ctx).GetByIdAsync(id)</c> devuelve <c>IdentityStatus == "valid"</c>.</para>
/// </summary>
public sealed class MandateSignerIdentityTenantIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Documento = "1020304055";

    private async Task<Guid> SeedSignerAsync(Guid companyTenantId)
    {
        await TransitNetworkSeed.SeedMarcaBlancaNetworkAsync(Fixture);
        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;
        var signerId = Guid.NewGuid();
        ctx.MandateSigners.Add(new Flit.Infrastructure.Persistence.Entities.Admin.MandateSigner
        {
            Id = signerId,
            TransitOfficeId = HierarchyScenario.Ot1,
            FullName = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = Documento,
            IntegrityHash = new string('a', 64),
            RegisteredAt = now,
            IsActive = true,
            CreatedAt = now,
        });
        ctx.MandateSignerTransitOffices.Add(new Flit.Infrastructure.Persistence.Entities.Admin.MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(), MandateSignerId = signerId, TransitOfficeId = HierarchyScenario.Ot1,
            IsActive = true, CreatedAt = now,
        });
        ctx.MandateSignerCompanies.Add(new Flit.Infrastructure.Persistence.Entities.Admin.MandateSignerCompany
        {
            Id = Guid.NewGuid(), MandateSignerId = signerId, TransitOfficeId = HierarchyScenario.Ot1,
            CompanyTenantId = companyTenantId, IsActive = true, CreatedAt = now,
        });
        await ctx.SaveChangesAsync();
        return signerId;
    }

    /// <summary>Validación PROPIA del mandatario: rol mandatario + su ficha, sin persona ni trámite (DDL 129).</summary>
    private async Task AddPropiaAprobadaAsync(Guid tenantId, Guid signerId)
    {
        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;
        ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PartyRole = BiometricRules.ParteMandatario,
            MandateSignerId = signerId,
            DocumentType = "CC",
            DocumentNumber = Documento,
            Status = BiometricEstados.Aprobado,
            Provider = BiometricProviders.Kyverum,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = now.AddHours(1),
            ValidatedAt = now.AddDays(-1),
            CreatedAt = now.AddDays(-1),
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>Prevalidación standalone del mismo documento (cuelga de su persona, ck_biometric_validation_anchor).</summary>
    private async Task AddPrevalidacionAprobadaAsync(Guid tenantId)
    {
        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;
        var personId = Guid.NewGuid();
        ctx.Persons.Add(new Person
        {
            Id = personId,
            TenantId = tenantId,
            DocumentType = "CC",
            DocumentNumber = Documento,
            FullName = "Ana Restrepo",
            Email = "ana@flit.test",
            CreatedAt = now,
        });
        await ctx.SaveChangesAsync();
        ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PersonId = personId,
            DocumentType = "CC",
            DocumentNumber = Documento,
            Status = BiometricEstados.Aprobado,
            Provider = BiometricProviders.Kyverum,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = now.AddHours(1),
            ValidatedAt = now.AddDays(-1),
            ValidUntil = now.AddDays(29),
            CreatedAt = now.AddDays(-1),
        });
        await ctx.SaveChangesAsync();
    }

    [PostgresFact]
    public async Task Validacion_propia_registrada_en_el_tenant_de_la_compania_llega_a_la_ficha_aunque_el_OT_tenga_otro_tenant()
    {
        var signerId = await SeedSignerAsync(HierarchyScenario.C2);
        await AddPropiaAprobadaAsync(HierarchyScenario.C2, signerId);

        await using var ctx = NewContext();
        var item = await new DbMandateSignerReader(ctx).GetByIdAsync(signerId);

        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
    }

    [PostgresFact]
    public async Task La_aprobacion_de_una_prevalidacion_con_el_mismo_documento_no_cuenta_para_el_mandatario()
    {
        // HU #13247 AC2: sin validación propia no hay firma válida, aunque el documento ya esté aprobado como prevalidación
        // en el mismo tenant de la compañía.
        var signerId = await SeedSignerAsync(HierarchyScenario.C2);
        await AddPrevalidacionAprobadaAsync(HierarchyScenario.C2);

        await using var ctx = NewContext();
        var item = await new DbMandateSignerReader(ctx).GetByIdAsync(signerId);

        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.None);
    }
}
