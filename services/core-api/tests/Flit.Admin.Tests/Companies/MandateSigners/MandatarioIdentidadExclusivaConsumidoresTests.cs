using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13247 (Feature #13245, Épica #13090) AC4 — los consumidores de la identidad del mandatario (la ficha admin
/// <c>DbMandateSignerReader</c> y el directorio de trámites <c>MandateSignerDirectory</c>, del que cuelgan el gate de F4, la
/// prelación y el sello del contrato) usan la MISMA variante exclusiva y devuelven el mismo estado, con el repositorio real
/// (no un doble): solo cuenta la validación lanzada para el mandatario. <para>Uso: ficha <c>valid</c> ⇔ directorio
/// <c>IdentityVigente</c>.</para>
/// </summary>
public sealed class MandatarioIdentidadExclusivaConsumidoresTests
{
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid Gestora = Guid.NewGuid();
    private static readonly Guid Signer = Guid.NewGuid();
    private const string Documento = "1020304050";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<FlitDbContext> SeedAsync(params ProcedureInstanceBiometricValidation[] validaciones)
    {
        var ctx = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-identidad-exclusiva-{Guid.NewGuid()}").Options);
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = Signer, TransitOfficeId = Ot, FullName = "Ana Restrepo", DocumentType = "CC", DocumentNumber = Documento,
            IntegrityHash = new string('a', 64), RegisteredAt = Now, IsActive = true, CreatedAt = Now,
            SignerModel = "natural", SignatureMethod = "biometria",
        });
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(), MandateSignerId = Signer, TransitOfficeId = Ot, IsActive = true, CreatedAt = Now,
        });
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(), MandateSignerId = Signer, TransitOfficeId = Ot, CompanyTenantId = Gestora,
            IsActive = true, CreatedAt = Now,
        });
        ctx.ProcedureInstanceBiometricValidations.AddRange(validaciones);
        await ctx.SaveChangesAsync(Ct);
        return ctx;
    }

    private static ProcedureInstanceBiometricValidation Aprobada(string? rol, Guid? signer, string estado = BiometricEstados.Aprobado) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Gestora,
            PartyRole = signer is null ? rol : BiometricRules.ParteMandatario,
            MandateSignerId = signer,
            DocumentType = "CC",
            DocumentNumber = Documento,
            Status = estado,
            Provider = BiometricProviders.Kyverum,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = Now.AddHours(1),
            ValidatedAt = estado == BiometricEstados.Aprobado ? Now.AddDays(-45) : null,
            ValidUntil = estado == BiometricEstados.Aprobado ? Now.AddDays(-15) : null,
            CertificateHash = "serie-1",
            CreatedAt = Now.AddDays(-45),
        };

    private static async Task<(string Ficha, bool Directorio, string? Certificado)> ConsultarAsync(FlitDbContext ctx)
    {
        var resolver = new IdentityVigenciaPorDocumentoResolver(new ProcedureInstanceRepository(ctx));
        var otStatus = Substitute.For<ITransitOfficeOperationalStatusReader>();
        var ficha = await new DbMandateSignerReader(ctx, otStatus, resolver).GetByIdAsync(Signer, Ct);
        var directorio = await new MandateSignerDirectory(ctx, otStatus, resolver).GetByIdAsync(Signer, Ct);
        return (ficha!.IdentityStatus, directorio!.IdentityVigente, directorio.CertificadoIdentidad);
    }

    [Fact]
    public async Task ConValidacionPropiaAprobada_FichaYDirectorioCoinciden_SinVentanaDe30Dias()
    {
        await using var ctx = await SeedAsync(Aprobada(null, Signer)); // aprobada hace 45 días

        var (ficha, directorio, certificado) = await ConsultarAsync(ctx);

        ficha.Should().Be(AdminIdentityVigencia.Valid);
        directorio.Should().BeTrue();
        certificado.Should().Be("serie-1");
    }

    [Theory]
    [InlineData("comprador")]
    [InlineData("vendedor")]
    [InlineData(null)]
    public async Task ConLaAprobacionDeOtroRolConElMismoDocumento_FichaYDirectorioCoinciden_SinFirmaValida(string? rol)
    {
        await using var ctx = await SeedAsync(Aprobada(rol, null));

        var (ficha, directorio, certificado) = await ConsultarAsync(ctx);

        ficha.Should().Be(AdminIdentityVigencia.None);
        directorio.Should().BeFalse();
        certificado.Should().BeNull();
    }

    [Fact]
    public async Task ConUnaNuevaEnCursoSobreLaAprobada_FichaYDirectorioCoinciden_LaAnteriorDejaDeContar()
    {
        var nueva = Aprobada(null, Signer, BiometricEstados.EnProceso);
        nueva.CreatedAt = Now.AddHours(-1);
        await using var ctx = await SeedAsync(Aprobada(null, Signer), nueva);

        var (ficha, directorio, _) = await ConsultarAsync(ctx);

        ficha.Should().Be(AdminIdentityVigencia.Pending);
        directorio.Should().BeFalse();
    }
}
