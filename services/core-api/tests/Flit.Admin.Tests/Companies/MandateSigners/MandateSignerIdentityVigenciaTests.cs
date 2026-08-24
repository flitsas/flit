using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #11765 (ADR-0050) — <c>DbMandateSignerReader</c> (la FICHA admin de gestión de mandatarios; NO
/// <c>MandateSignerDirectory</c>, que es la ruta de RADICACIÓN y ya migró en la HU #11752) deja de leer
/// <c>admin.admin_identity_validations</c> y resuelve <c>IdentityStatus</c>/<c>IdentityValidUntil</c>
/// contra el módulo Identidad, en el tenant PROPIO del organismo donde está registrado el mandatario
/// (<see cref="ITransitOfficeOperationalStatusReader"/>) — mismo mecanismo que el directorio.
/// </summary>
public sealed class MandateSignerIdentityVigenciaTests
{
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid OtTenant = Guid.NewGuid();
    private static readonly Guid Signer = Guid.NewGuid();
    private const string Documento = "1020304050";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandate-reader-identity-{Guid.NewGuid()}")
            .Options);

    private static async Task<FlitDbContext> SeedAsync()
    {
        var ctx = NewContext();
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = Signer,
            TransitOfficeId = Ot,
            FullName = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = Documento,
            IntegrityHash = new string('a', 64),
            RegisteredAt = Now,
            IsActive = true,
            CreatedAt = Now,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return ctx;
    }

    private static ITransitOfficeOperationalStatusReader ReaderConTenant(Guid officeId, Guid? tenantId)
    {
        var reader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        var item = tenantId is { } t
            ? new TransitOfficeOperationalStatusItem { Id = officeId, HasTenant = true, TenantId = t }
            : null;
        reader.GetByIdAsync(officeId, Arg.Any<CancellationToken>()).Returns(item);
        return reader;
    }

    [Fact]
    public async Task GetByIdAsync_ConAprobadaVigenteEnElModuloIdentidad_MarcaValidYExponeVigencia()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            DocumentType = "CC",
            DocumentNumber = Documento,
            Status = BiometricEstados.Aprobado,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "hash",
            ExpiresAt = Now.AddHours(1),
            ValidatedAt = Now.AddDays(-1),
            ValidUntil = Now.AddDays(29),
            CreatedAt = Now.AddDays(-1),
        });
        await ctx.SaveChangesAsync(ct);

        var reader = new DbMandateSignerReader(ctx, ReaderConTenant(Ot, OtTenant));
        var item = await reader.GetByIdAsync(Signer, ct);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
        item.IdentityValidUntil.Should().Be(Now.AddDays(29));
    }

    [Fact]
    public async Task GetByIdAsync_ConValidacionEnCurso_MarcaPending()
    {
        // Es el AC central de la ola: prevalidar en Identidad (queda "en_curso" hasta que Kyverum
        // resuelva) debe reflejarse aquí como "en curso", no seguir mostrando "sin validar".
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            DocumentType = "CC",
            DocumentNumber = Documento,
            Status = BiometricEstados.EnProceso,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "hash",
            ExpiresAt = Now.AddHours(1),
            CreatedAt = Now.AddDays(-1),
        });
        await ctx.SaveChangesAsync(ct);

        var reader = new DbMandateSignerReader(ctx, ReaderConTenant(Ot, OtTenant));
        var item = await reader.GetByIdAsync(Signer, ct);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Pending);
        item.IdentityValidUntil.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_OtSinTenant_QuedaSinValidarYNoConsultaIdentidad()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var identityRepo = Substitute.For<IProcedureInstanceRepository>();
        var reader = new DbMandateSignerReader(
            ctx, ReaderConTenant(Ot, null), new IdentityVigenciaPorDocumentoResolver(identityRepo));

        var item = await reader.GetByIdAsync(Signer, ct);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.None);
        await identityRepo.DidNotReceive().ListLatestBiometricValidationsByPersonsAsync(
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyCollection<(string DocumentTypeNorm, string DocumentNumberNorm)>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListByOtAsync_DosMandatariosDelMismoOrganismo_ResuelveEnUnaSolaConsultaBatch()
    {
        // AC de lote (HU #11765): dos mandatarios del MISMO organismo con documentos distintos se
        // resuelven con UNA sola llamada batch al resolver de Identidad, no una por fila.
        var ct = TestContext.Current.CancellationToken;
        var otroSigner = Guid.NewGuid();
        const string otroDocumento = "9998887776";
        await using var ctx = await SeedAsync();
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = otroSigner,
            TransitOfficeId = Ot,
            FullName = "Carlos Pérez",
            DocumentType = "CC",
            DocumentNumber = otroDocumento,
            IntegrityHash = new string('b', 64),
            RegisteredAt = Now,
            IsActive = true,
            CreatedAt = Now,
        });
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(),
            MandateSignerId = Signer,
            TransitOfficeId = Ot,
            IsActive = true,
            CreatedAt = Now,
        });
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(),
            MandateSignerId = otroSigner,
            TransitOfficeId = Ot,
            IsActive = true,
            CreatedAt = Now,
        });
        await ctx.SaveChangesAsync(ct);

        var identityRepo = Substitute.For<IProcedureInstanceRepository>();
        identityRepo.ListLatestBiometricValidationsByPersonsAsync(
                OtTenant,
                Arg.Any<IReadOnlyCollection<(string DocumentTypeNorm, string DocumentNumberNorm)>>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var reader = new DbMandateSignerReader(
            ctx, ReaderConTenant(Ot, OtTenant), new IdentityVigenciaPorDocumentoResolver(identityRepo));

        var items = await reader.ListByOtAsync(Ot, ct);

        items.Should().HaveCount(2);
        await identityRepo.Received(1).ListLatestBiometricValidationsByPersonsAsync(
            OtTenant,
            Arg.Any<IReadOnlyCollection<(string DocumentTypeNorm, string DocumentNumberNorm)>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NingunaConsultaVaHaciaAdminIdentityValidations()
    {
        // Guardrail explícito del AC de la HU #11765: no debe quedar ninguna lectura de
        // admin.admin_identity_validations en la ficha admin de mandatarios.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.AdminIdentityValidations.Add(new AdminIdentityValidationEntity
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            SubjectType = "mandate_signer",
            SubjectRef = Signer,
            Name = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = Documento,
            Email = "sin-correo@flit.local",
            Status = "aprobado",
            ValidUntil = Now.AddDays(60),
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await ctx.SaveChangesAsync(ct);

        var reader = new DbMandateSignerReader(ctx, ReaderConTenant(Ot, OtTenant));
        var item = await reader.GetByIdAsync(Signer, ct);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.None,
            "el rótulo admin ya no debe leer la fila de la tabla abandonada");
    }
}
