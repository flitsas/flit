using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #11752 (ADR-0050) — <c>MandateSignerDirectory</c> deja de leer
/// <c>admin.admin_identity_validations</c> y resuelve <c>IdentityVigente</c>/<c>CertificadoIdentidad</c>/
/// <c>IdentityValidUntil</c> contra el módulo Identidad (<c>tramites.procedure_instance_biometric_validations</c>),
/// en el tenant PROPIO del organismo de tránsito donde está registrado el mandatario — resuelto vía
/// <see cref="ITransitOfficeOperationalStatusReader"/>, el MISMO mecanismo que usaba el disparo admin ya
/// retirado.
///
/// <para>Uso de ejemplo:
/// <c>new MandateSignerDirectory(ctx, otStatusReader, identityResolver).GetCandidatesAsync(ot, empresa, null, ct)</c>
/// ⇒ <c>candidatos[0].IdentityVigente == true</c> cuando el resolver de Identidad devuelve
/// <c>aprobada_vigente</c> para el documento del mandatario en el tenant del OT.</para>
/// </summary>
public sealed class MandateSignerDirectoryIdentityVigenciaTests
{
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid OtTenant = Guid.NewGuid();
    private static readonly Guid Gestora = Guid.NewGuid();
    private static readonly Guid Signer = Guid.NewGuid();
    private const string Documento = "1020304050";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandate-identity-{Guid.NewGuid()}")
            .Options);

    private static async Task<FlitDbContext> SeedAsync()
    {
        var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = Signer,
            TransitOfficeId = Ot,
            FullName = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = Documento,
            IntegrityHash = new string('a', 64),
            RegisteredAt = now,
            IsActive = true,
            CreatedAt = now,
        });
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(),
            MandateSignerId = Signer,
            TransitOfficeId = Ot,
            CompanyTenantId = Gestora,
            IsActive = true,
            CreatedAt = now,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return ctx;
    }

    private static ITransitOfficeOperationalStatusReader ReaderConTenant(Guid? tenantId) =>
        StubReader(tenantId is { } t
            ? new TransitOfficeOperationalStatusItem { Id = Ot, HasTenant = true, TenantId = t }
            : null);

    private static ITransitOfficeOperationalStatusReader StubReader(TransitOfficeOperationalStatusItem? item)
    {
        var reader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        reader.GetByIdAsync(Ot, Arg.Any<CancellationToken>()).Returns(item);
        return reader;
    }

    /// <summary>
    /// HU #13247 — el repositorio devuelve SOLO las validaciones lanzadas para el mandatario (party_role mandatario + su
    /// ficha); la aprobación de un comprador, un vendedor o una prevalidación con el mismo documento nunca llega aquí.
    /// </summary>
    private static IProcedureInstanceRepository RepoStub(params ProcedureInstanceBiometricValidation[] propias)
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.ListMandatarioValidationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ProcedureInstanceBiometricValidation>)propias);
        return repo;
    }

    private static ProcedureInstanceBiometricValidation Aprobada(string numero = Documento) => new()
    {
        Status = BiometricEstados.Aprobado,
        PartyRole = BiometricRules.ParteMandatario,
        MandateSignerId = Signer,
        DocumentType = "CC",
        DocumentNumber = numero,
        ValidatedAt = Now.AddDays(-1),
        ValidUntil = Now.AddDays(29),
        CertificateHash = "hash-mandatario",
        CreatedAt = Now.AddDays(-1),
    };

    [Fact]
    public async Task GetCandidatesAsync_SinValidacionPropia_NoHaySello_AunqueOtroRolTengaAprobadoElMismoDocumento()
    {
        // HU #13247 AC2: la aprobación de un comprador/vendedor/prevalidación (que no llega al repositorio de mandatarios)
        // no estampa sello ni habilita la firma del mandatario.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(OtTenant), new IdentityVigenciaPorDocumentoResolver(RepoStub()));

        var candidatos = await directorio.GetCandidatesAsync(Ot, Gestora, null, ct);

        var candidato = candidatos.Should().ContainSingle().Subject;
        candidato.IdentityVigente.Should().BeFalse();
        candidato.CertificadoIdentidad.Should().BeNull();
    }

    [Fact]
    public async Task GetCandidatesAsync_ValidacionPropiaVigente_NoDependeDelTenantNiDeLasCompaniasVinculadas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(),
            MandateSignerId = Signer,
            TransitOfficeId = Ot,
            CompanyTenantId = Guid.NewGuid(),
            IsActive = true,
            CreatedAt = Now,
        });
        await ctx.SaveChangesAsync(ct);
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(null), new IdentityVigenciaPorDocumentoResolver(RepoStub(Aprobada())));

        var candidatos = await directorio.GetCandidatesAsync(Ot, Gestora, null, ct);

        var candidato = candidatos.Should().ContainSingle().Subject;
        candidato.IdentityVigente.Should().BeTrue();
        candidato.CertificadoIdentidad.Should().Be("hash-mandatario");
    }

    [Fact]
    public async Task GetCandidatesAsync_ConValidacionPropiaAprobada_MarcaVigente_SinFechaDeFin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(OtTenant), new IdentityVigenciaPorDocumentoResolver(RepoStub(Aprobada())));

        var candidatos = await directorio.GetCandidatesAsync(Ot, Gestora, null, ct);

        var candidato = candidatos.Should().ContainSingle().Subject;
        candidato.IdentityVigente.Should().BeTrue();
        candidato.CertificadoIdentidad.Should().Be("hash-mandatario");
        candidato.IdentityValidUntil.Should().BeNull("el mandatario no renueva su identidad (HU #13130b): no hay fecha de fin");
    }

    [Fact]
    public async Task GetCandidatesAsync_PropiaAprobadaDeUnDocumentoAnterior_NoQuedaVigente()
    {
        // HU #13247 AC3: tras cambiar el documento de la ficha, la aprobación del documento anterior no cuenta.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(OtTenant),
            new IdentityVigenciaPorDocumentoResolver(RepoStub(Aprobada(numero: "999000111"))));

        var candidatos = await directorio.GetCandidatesAsync(Ot, Gestora, null, ct);

        candidatos.Should().ContainSingle().Which.IdentityVigente.Should().BeFalse();
    }

    [Fact]
    public async Task GetCandidatesAsync_AprobadaHace40Dias_CuentaComoVigenteParaElMandatario()
    {
        // HU #13130b (decisión del PO, 01-oct): la ventana de 30 días del trámite no aplica al mandatario;
        // una aprobación propia basta mientras su vigencia propia esté activa.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var vieja = Aprobada();
        vieja.ValidatedAt = Now.AddDays(-40);
        vieja.ValidUntil = Now.AddDays(-10);
        vieja.CreatedAt = Now.AddDays(-40);
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(OtTenant), new IdentityVigenciaPorDocumentoResolver(RepoStub(vieja)));

        var candidatos = await directorio.GetCandidatesAsync(Ot, Gestora, null, ct);

        candidatos.Should().ContainSingle().Which.IdentityVigente.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_ResuelveIdentidadPorLaFichaDelMandatario()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(OtTenant), new IdentityVigenciaPorDocumentoResolver(RepoStub(Aprobada())));

        var signer = await directorio.GetByIdAsync(Signer, ct);

        signer.Should().NotBeNull();
        signer!.IdentityVigente.Should().BeTrue();
        signer.CertificadoIdentidad.Should().Be("hash-mandatario");
    }
}
