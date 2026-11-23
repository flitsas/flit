using Flit.Admin.Domain.Companies.TransitOffices;
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
/// Bug #14661 — el directorio de mandatarios marca cuándo la rúbrica de su validación propia viene de una validación
/// MANUAL (<c>RubricaIdentidadManual</c>), para que el mandato le quite el fondo blanco antes de estamparla. Con el
/// repositorio real sobre InMemory, mismo montaje que <see cref="MandatarioIdentidadExclusivaConsumidoresTests"/>.
/// <para>Uso: <c>(await directorio.GetByIdAsync(id, ct))!.RubricaIdentidadManual</c>.</para>
/// </summary>
public sealed class Bug14661MandatarioFirmaManualDirectorioTests
{
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid Gestora = Guid.NewGuid();
    private static readonly Guid Signer = Guid.NewGuid();
    private const string Documento = "1020304050";
    private const string Rubrica = "identity/mandatario/firma.png";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<FlitDbContext> SeedAsync(string provider)
    {
        var ctx = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-bug14661-{Guid.NewGuid()}").Options);
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
        ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Gestora,
            PartyRole = BiometricRules.ParteMandatario,
            MandateSignerId = Signer,
            DocumentType = "CC",
            DocumentNumber = Documento,
            Status = BiometricEstados.Aprobado,
            Provider = provider,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = Now.AddHours(1),
            ValidatedAt = Now.AddDays(-2),
            ValidUntil = Now.AddDays(28),
            CertificateHash = "serie-1",
            SignatureImagePath = Rubrica,
            CreatedAt = Now.AddDays(-2),
        });
        await ctx.SaveChangesAsync(Ct);
        return ctx;
    }

    private static Task<Flit.Tramites.Domain.Integration.MandateSignerCandidate?> ConsultarAsync(FlitDbContext ctx) =>
        new MandateSignerDirectory(
                ctx,
                Substitute.For<ITransitOfficeOperationalStatusReader>(),
                new IdentityVigenciaPorDocumentoResolver(new ProcedureInstanceRepository(ctx)))
            .GetByIdAsync(Signer, Ct);

    [Fact]
    public async Task ValidacionPropiaManual_MarcaLaRubricaComoManual()
    {
        await using var ctx = await SeedAsync(BiometricProviders.Manual);

        var candidato = await ConsultarAsync(ctx);

        candidato.Should().NotBeNull();
        candidato!.RubricaIdentidadPath.Should().Be(Rubrica);
        candidato.RubricaIdentidadManual.Should().BeTrue();
    }

    [Fact]
    public async Task ValidacionPropiaKyverum_NoLaMarcaComoManual()
    {
        await using var ctx = await SeedAsync(BiometricProviders.Kyverum);

        var candidato = await ConsultarAsync(ctx);

        candidato.Should().NotBeNull();
        candidato!.RubricaIdentidadPath.Should().Be(Rubrica);
        candidato.RubricaIdentidadManual.Should().BeFalse();
    }
}
