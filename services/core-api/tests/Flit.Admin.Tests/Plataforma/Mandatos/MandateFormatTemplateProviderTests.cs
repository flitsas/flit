using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #13172 (Feature #13118, Épica #13090) — el proveedor lee de la base la plantilla que debe usar el contrato de un
/// formato: la vigente, o la versión fijada por un contrato ya emitido. Base en memoria; solo lectura.
/// </summary>
public sealed class MandateFormatTemplateProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(FlitDbContext Db, MandateFormatTemplateProvider Provider)> NewAsync(int current = 2)
    {
        var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"mandate-format-provider-{Guid.NewGuid()}").Options);
        var settingId = Guid.NewGuid();
        db.MandateFormatSettings.Add(new MandateFormatSettingEntity
        {
            Id = settingId, FormatCode = "bello", DisplayName = "Bello", AssignmentMode = "signer",
            CurrentVersion = current, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.MandateFormatSettings.Add(new MandateFormatSettingEntity
        {
            Id = Guid.NewGuid(), FormatCode = "generico", DisplayName = "Genérico", AssignmentMode = "signer",
            CurrentVersion = 0, CreatedAt = DateTimeOffset.UtcNow,
        });
        for (var n = 1; n <= 3; n++)
        {
            db.MandateFormatVersions.Add(new MandateFormatVersionEntity
            {
                Id = Guid.NewGuid(), FormatSettingId = settingId, VersionNumber = n, Body = $"Cuerpo {n}",
                BodySha256 = new string('a', 64), CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(Ct);
        return (db, new MandateFormatTemplateProvider(db));
    }

    [Fact]
    public async Task SinVersionFijada_DevuelveLaVigente_NoLaUltimaInsertada()
    {
        var (db, provider) = await NewAsync(current: 2);
        await using var _ = db;

        var t = await provider.ResolveAsync("bello", null, Ct);

        t.Should().Be(new Flit.Tramites.Domain.Integration.MandateFormatTemplate("bello", 2, "Cuerpo 2"));
    }

    [Fact]
    public async Task ConVersionFijada_DevuelveEsaVersion_AunqueHayaUnaMasReciente()
    {
        var (db, provider) = await NewAsync(current: 3);
        await using var _ = db;

        var t = await provider.ResolveAsync("bello", 2, Ct);

        t.VersionNumber.Should().Be(2);
        t.Body.Should().Be("Cuerpo 2");
    }

    [Fact]
    public async Task VersionFijadaEnCero_SignificaRedaccionDelGenerador()
    {
        var (db, provider) = await NewAsync(current: 3);
        await using var _ = db;

        var t = await provider.ResolveAsync("bello", 0, Ct);

        t.VersionNumber.Should().Be(0);
        t.Body.Should().BeNull();
    }

    [Fact]
    public async Task FormatoSinPlantillaPersonalizada_OSinFila_DevuelveVersion0()
    {
        var (db, provider) = await NewAsync();
        await using var _ = db;

        (await provider.ResolveAsync("generico", null, Ct)).Body.Should().BeNull();
        (await provider.ResolveAsync("municipio", null, Ct)).VersionNumber.Should().Be(0);
        (await provider.ResolveAsync("  BELLO ", null, Ct)).VersionNumber.Should().Be(2, "el código se normaliza");
    }

    [Fact]
    public async Task VersionFijadaInexistente_CaeALaVigenteEnVezDeRomperLaGeneracion()
    {
        var (db, provider) = await NewAsync(current: 2);
        await using var _ = db;

        var t = await provider.ResolveAsync("bello", 9, Ct);

        t.VersionNumber.Should().Be(2);
    }
}
