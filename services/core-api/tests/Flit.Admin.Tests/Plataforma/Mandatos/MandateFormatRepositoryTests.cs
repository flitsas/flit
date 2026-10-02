using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #13169 (Feature #13118, Épica #13090) — persistencia de la personalización y las versiones de los formatos.
/// Uso: <c>await repo.SaveAsync("bello", new(ExpectedRowVersion: 0, Body: "Contrato {{placa}}"), userId)</c> crea la
/// versión 1 y la deja vigente. EF InMemory no corre el trigger de row_version del DDL 124: los tests que lo
/// necesitan lo emulan subiendo el RowVersion a mano, como lo haría la base real.
/// </summary>
public sealed class MandateFormatRepositoryTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid Author = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static FlitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"mandate-format-{Guid.NewGuid()}")
            .Options);

    private static async Task SeedAsync(FlitDbContext db)
    {
        // Mismos valores que el seed del DDL 124.
        (string Code, string Name, string Mode)[] seed =
        [
            ("auto", "Automática (según el organismo)", "signer"),
            ("generico", "Genérico", "signer"),
            ("sabaneta", "Sabaneta", "institutional"),
            ("bello", "Bello", "signer"),
            ("municipio", "Envigado, Funza y Medellín", "signer"),
        ];
        foreach (var (code, name, mode) in seed)
        {
            db.MandateFormatSettings.Add(new MandateFormatSettingEntity
            {
                Id = Guid.NewGuid(), FormatCode = code, DisplayName = name, AssignmentMode = mode,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(Ct);
    }

    private static async Task<(FlitDbContext Db, MandateFormatRepository Repo)> NewRepoAsync()
    {
        var db = NewDb();
        await SeedAsync(db);
        return (db, new MandateFormatRepository(db));
    }

    [Fact]
    public async Task AC1_ConfiguracionPorFormato_ListaUnaFilaPorFormatoEnElOrdenDelCatalogo_SinPlantilla()
    {
        var (db, repo) = await NewRepoAsync();
        await using var _ = db;

        var all = await repo.ListAsync(Ct);

        all.Select(s => s.Code).Should().Equal("auto", "generico", "sabaneta", "bello", "municipio");
        all.Should().OnlyContain(s => s.CurrentVersion == 0);
        all.Single(s => s.Code == "sabaneta").AssignmentMode.Should().Be("institutional");
        (await repo.GetCurrentVersionAsync("bello", Ct)).Should().BeNull("sin plantilla personalizada");
    }

    [Fact]
    public async Task AC2_VersionInmutable_GuardarPlantillaCreaVersionNumeradaConSha256AutorYFecha()
    {
        var (db, repo) = await NewRepoAsync();
        await using var _ = db;

        var result = await repo.SaveAsync("bello", new(0, Body: "Contrato sobre {{placa}}"), Author, Ct);

        result.Status.Should().Be(MandateFormatWriteStatus.Ok);
        var v = result.PublishedVersion!;
        v.VersionNumber.Should().Be(1);
        v.BodySha256.Should().Be(MandateFormatRepository.Sha256Hex("Contrato sobre {{placa}}"))
            .And.MatchRegex("^[0-9a-f]{64}$");
        v.CreatedBy.Should().Be(Author);
        v.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        result.Setting!.CurrentVersion.Should().Be(1);
    }

    [Fact]
    public async Task AC2_LasVersionesAnterioresNoSeModificanAlPublicarUnaNueva()
    {
        var (db, repo) = await NewRepoAsync();
        await using var _ = db;
        await repo.SaveAsync("bello", new(0, Body: "Texto uno"), Author, Ct);
        var before = (await repo.GetVersionAsync("bello", 1, Ct))!;
        var rv = (await repo.GetAsync("bello", Ct))!.RowVersion;

        await repo.SaveAsync("bello", new(rv, Body: "Texto dos"), Author, Ct);

        var after = (await repo.GetVersionAsync("bello", 1, Ct))!;
        after.Should().Be(before);
        db.MandateFormatVersions.Count().Should().Be(2);
    }

    [Fact]
    public async Task AC3_VersionVigente_EsLaUltimaPublicada_ConLasAnterioresLegiblesPorNumero()
    {
        var (db, repo) = await NewRepoAsync();
        await using var _ = db;
        for (var i = 1; i <= 3; i++)
        {
            var rv = (await repo.GetAsync("generico", Ct))!.RowVersion;
            (await repo.SaveAsync("generico", new(rv, Body: $"Cuerpo {i}"), Author, Ct)).Status
                .Should().Be(MandateFormatWriteStatus.Ok);
        }

        (await repo.GetCurrentVersionAsync("generico", Ct))!.Body.Should().Be("Cuerpo 3");
        (await repo.GetVersionAsync("generico", 2, Ct))!.Body.Should().Be("Cuerpo 2");
        (await repo.GetVersionAsync("generico", 1, Ct))!.Body.Should().Be("Cuerpo 1");
        (await repo.GetVersionAsync("generico", 4, Ct)).Should().BeNull();
        (await repo.ListVersionsAsync("generico", Ct)).Select(v => v.VersionNumber).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task AC4_Concurrencia_RowVersionDesactualizado_FallaConConflictoYNoCreaVersion()
    {
        var (db, repo) = await NewRepoAsync();
        await using var _ = db;
        // El primer editor guarda y la base (trigger) sube el RowVersion; el segundo todavía tiene el valor viejo.
        await repo.SaveAsync("bello", new(0, Name: "Bello renombrado"), Author, Ct);
        var setting = await db.MandateFormatSettings.SingleAsync(s => s.FormatCode == "bello", Ct);
        setting.RowVersion = 1;
        await db.SaveChangesAsync(Ct);

        var stale = await repo.SaveAsync("bello", new(0, Body: "Texto del segundo editor"), Author, Ct);

        stale.Status.Should().Be(MandateFormatWriteStatus.Conflict);
        db.MandateFormatVersions.Should().BeEmpty();
        (await repo.GetAsync("bello", Ct))!.CurrentVersion.Should().Be(0);
    }

    [Fact]
    public async Task AC4_RowVersionAusente_SobreUnFormatoExistente_EsConflicto()
    {
        var (db, repo) = await NewRepoAsync();
        await using var _ = db;

        var result = await repo.SaveAsync("bello", new(null, Name: "Otro nombre"), Author, Ct);

        result.Status.Should().Be(MandateFormatWriteStatus.Conflict);
        (await repo.GetAsync("bello", Ct))!.Name.Should().Be("Bello");
    }

    [Theory]
    [InlineData("inventado")]
    [InlineData("")]
    [InlineData("Bello 2")]
    public async Task AC5_CodigoInexistente_SeRechazaYNoSeCreaNingunaFila(string code)
    {
        var (db, repo) = await NewRepoAsync();
        await using var _ = db;

        var result = await repo.SaveAsync(code, new(0, Name: "X", Body: "Hola"), Author, Ct);

        result.Status.Should().Be(MandateFormatWriteStatus.UnknownCode);
        db.MandateFormatSettings.Count().Should().Be(5);
        db.MandateFormatVersions.Should().BeEmpty();
    }

    [Fact]
    public async Task AC5_CodigoDelCatalogoSinFila_NoSeCreaLaFila()
    {
        await using var db = NewDb();
        var repo = new MandateFormatRepository(db);

        var result = await repo.SaveAsync("bello", new(0, Name: "X"), Author, Ct);

        result.Status.Should().Be(MandateFormatWriteStatus.UnknownCode);
        db.MandateFormatSettings.Should().BeEmpty();
    }

    [Fact]
    public async Task AC7_CuerpoAcotado_MasDe100000Caracteres_SeRechazaConElLimiteDelEditorDelOrganismo()
    {
        var (db, repo) = await NewRepoAsync();
        await using var _ = db;

        var tooLong = await repo.SaveAsync("bello", new(0, Body: new string('a', 100_001)), Author, Ct);
        var exact = await repo.SaveAsync("bello", new(0, Body: new string('a', 100_000)), Author, Ct);
        var empty = await repo.SaveAsync("generico", new(0, Body: "   "), Author, Ct);

        tooLong.Status.Should().Be(MandateFormatWriteStatus.InvalidBody);
        exact.Status.Should().Be(MandateFormatWriteStatus.Ok);
        empty.Status.Should().Be(MandateFormatWriteStatus.InvalidBody);
        db.MandateFormatVersions.Count().Should().Be(1);
    }

    [Fact]
    public async Task Guardar_NombreYTipo_SinCuerpo_NoCreaVersion_YSinCambioRealNoEscribe()
    {
        var (db, repo) = await NewRepoAsync();
        await using var _ = db;

        var changed = await repo.SaveAsync("bello", new(0, Name: "Bello 2026", AssignmentMode: "open"), Author, Ct);
        var same = await repo.SaveAsync("generico", new(0, Name: "Genérico", AssignmentMode: "signer"), Author, Ct);

        changed.Changed.Should().BeTrue();
        changed.Setting!.Name.Should().Be("Bello 2026");
        changed.Setting.AssignmentMode.Should().Be("open");
        changed.PublishedVersion.Should().BeNull();
        same.Status.Should().Be(MandateFormatWriteStatus.Ok);
        same.Changed.Should().BeFalse();
        db.MandateFormatVersions.Should().BeEmpty();
    }
}
