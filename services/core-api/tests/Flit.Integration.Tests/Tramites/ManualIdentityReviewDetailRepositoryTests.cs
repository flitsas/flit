using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13297 (Feature #13282 C2, Épica #13202) — detalle e imágenes cross-tenant de validaciones manuales contra PostgreSQL
/// real: el ciclo actual decide qué imágenes y qué consentimiento se ven, el revisor se resuelve a su nombre visible y lo que no
/// es del flujo manual no se encuentra.
/// <para>Uso: <c>await new ManualIdentityReviewReadRepository(ctx).GetDetailAsync(id, ct)</c>.</para>
/// </summary>
public sealed class ManualIdentityReviewDetailRepositoryTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid TenantA = new("c1000000-0000-7000-8000-0000000013a1");
    private static readonly Guid Revisor = new("c4000000-0000-7000-8000-0000000013a2");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<Guid> SeedAsync(
        string status, string provider = BiometricProviders.Manual, bool conRutas = true, DateTimeOffset? consentAt = null,
        Guid? reviewedBy = null, string? rejection = null, string? approvalOrigin = null)
    {
        await using var ctx = NewContext();
        if (!ctx.Tenants.Any(t => t.Id == TenantA))
        {
            ctx.Tenants.Add(TenantSeed.New(TenantA, "IT-DET-A", false, null));
            await ctx.SaveChangesAsync(Ct);
            ctx.Users.Add(new User
            {
                Id = Revisor, Email = "it-13297@flit.test", DisplayName = "Revisor sintético 13297", Status = "active",
                HomeTenantId = TenantA, CreatedAt = Now,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        var person = new Person
        {
            Id = Guid.NewGuid(), TenantId = TenantA, DocumentType = "CC", DocumentNumber = $"8{Random.Shared.NextInt64(1_000_000, 9_999_999)}",
            FullName = "Persona detalle", Email = "d@flit.test", PersonType = PersonTypes.Natural, CreatedAt = Now,
        };
        ctx.Persons.Add(person);
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = TenantA, PersonId = person.Id, Name = person.FullName, DocumentType = "CC",
            DocumentNumber = person.DocumentNumber, Email = person.Email, RegisteredEmail = person.Email, Status = status,
            Provider = provider, TokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            ExpiresAt = Now.AddHours(5), CreatedAt = Now.AddDays(-1),
            ManualActivatedAt = Now.AddHours(-3), ManualActivatedBy = Revisor,
            ConsentAt = consentAt, ConsentTextVersion = consentAt is null ? null : "v1",
            ReviewedBy = reviewedBy, ReviewedAt = reviewedBy is null ? null : Now.AddHours(-1),
            RejectionReasonCode = rejection, ApprovalOrigin = approvalOrigin,
            FacePhotoPath = conRutas ? "p-rostro" : null, IdFrontPhotoPath = conRutas ? "p-anverso" : null,
            IdBackPhotoPath = conRutas ? "p-reverso" : null, SignatureImagePath = conRutas ? "p-firma" : null,
            SignatureImageSha256 = conRutas ? new string('a', 64) : null,
        };
        ctx.ProcedureInstanceBiometricValidations.Add(v);
        await ctx.SaveChangesAsync(Ct);
        return v.Id;
    }

    private async Task<Flit.Tramites.Domain.Repositories.ManualIdentityReviewDetailRow?> DetailAsync(Guid id)
    {
        await using var ctx = NewContext();
        return await new ManualIdentityReviewReadRepository(ctx).GetDetailAsync(id, Ct);
    }

    private async Task<Flit.Tramites.Domain.Repositories.ManualIdentityImageRef?> ImageAsync(Guid id, string kind)
    {
        await using var ctx = NewContext();
        return await new ManualIdentityReviewReadRepository(ctx).GetImageRefAsync(id, kind, Ct);
    }

    [PostgresFact]
    public async Task Pendiente_trae_las_4_imagenes_el_consentimiento_del_ciclo_y_el_nombre_de_la_compania()
    {
        var id = await SeedAsync(BiometricEstados.PendienteRevisionManual, consentAt: Now.AddHours(-2));

        var d = await DetailAsync(id);

        d.Should().NotBeNull();
        d!.TenantName.Should().Be("Cliente de integración IT-DET-A");
        (d.HasRostro, d.HasAnverso, d.HasReverso, d.HasFirma).Should().Be((true, true, true, true));
        d.ConsentAt.Should().NotBeNull();
        d.ConsentTextVersion.Should().Be("v1");
        d.LinkExpiresAt.Should().BeNull();
    }

    [PostgresFact]
    public async Task ManualActivo_con_rutas_y_consentimiento_de_un_ciclo_anterior_no_muestra_ni_imagenes_ni_consentimiento()
    {
        // Consentimiento anterior a la activación actual (hace 4 h < activado hace 3 h): ciclo viejo.
        var id = await SeedAsync(
            BiometricEstados.ManualActivo, consentAt: Now.AddHours(-4), reviewedBy: Revisor, rejection: "imagen_borrosa");

        var d = await DetailAsync(id);

        (d!.HasRostro, d.HasAnverso, d.HasReverso, d.HasFirma).Should().Be((false, false, false, false));
        d.ConsentAt.Should().BeNull();
        d.RejectionReasonCode.Should().Be("imagen_borrosa");
        d.ReviewedBy.Should().Be("Revisor sintético 13297", "se resuelve al nombre visible del usuario");
        d.LinkExpiresAt.Should().NotBeNull();
        (await ImageAsync(id, "rostro"))!.StoragePath.Should().BeNull();
    }

    [PostgresTheory]
    [InlineData("rostro", "p-rostro")]
    [InlineData("anverso", "p-anverso")]
    [InlineData("reverso", "p-reverso")]
    [InlineData("firma", "p-firma")]
    [InlineData("otro", null)]
    public async Task La_referencia_de_imagen_mapea_el_kind_a_su_ruta_del_ciclo_actual(string kind, string? ruta)
    {
        var id = await SeedAsync(BiometricEstados.PendienteRevisionManual, consentAt: Now.AddHours(-2));

        var r = await ImageAsync(id, kind);

        r.Should().NotBeNull();
        r!.StoragePath.Should().Be(ruta);
        r.TenantId.Should().Be(TenantA);
    }

    [PostgresFact]
    public async Task Lo_que_no_es_del_flujo_manual_no_se_encuentra()
    {
        var kyverum = await SeedAsync(BiometricEstados.EnProceso, BiometricProviders.Kyverum);
        var automatica = await SeedAsync(BiometricEstados.Aprobado, approvalOrigin: BiometricApprovalOrigins.Automatica);

        (await DetailAsync(kyverum)).Should().BeNull();
        (await DetailAsync(automatica)).Should().BeNull();
        (await ImageAsync(kyverum, "rostro")).Should().BeNull();
        (await DetailAsync(Guid.NewGuid())).Should().BeNull();
    }

    [PostgresFact]
    public async Task Aprobada_manualmente_mantiene_imagenes_visibles()
    {
        var id = await SeedAsync(
            BiometricEstados.Aprobado, consentAt: Now.AddHours(-2), reviewedBy: Revisor, approvalOrigin: BiometricApprovalOrigins.Manual);

        var d = await DetailAsync(id);

        (d!.HasRostro && d.HasFirma).Should().BeTrue();
        d.LinkExpiresAt.Should().BeNull();
    }
}
