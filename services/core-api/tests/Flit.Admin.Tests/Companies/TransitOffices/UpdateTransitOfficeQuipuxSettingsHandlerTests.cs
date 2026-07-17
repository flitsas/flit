using Flit.Admin.Application.Companies.TransitOffices.UpdateTransitOfficeQuipuxSettings;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitOffices;

/// <summary>
/// Tests de la parametrización Quipux de la secretaría DESTINO (HU #10710): carga manual del
/// <c>divipo_code</c> + las tres banderas por familia de trámite. Proveedor InMemory.
///
/// Ojo con el homónimo: esto NO es
/// <c>admin.transit_office_profiles.operation_mode = 'quipux'</c> (el OT-CLIENTE en solo
/// lectura) — es el catálogo al que FLIT radica.
/// </summary>
public sealed class UpdateTransitOfficeQuipuxSettingsHandlerTests
{
    [Fact]
    public async Task Update_PersistsDivipoCodeAndFlags_AndReportsElegible()
    {
        var db = NewDbName();
        var officeId = await SeedOfficeAsync(db);

        await using var ctx = NewContext(db);
        var result = await NewHandler(ctx).HandleAsync(
            NewCommand(officeId, "05001", matricula: true, traspaso: false, otros: false),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(UpdateTransitOfficeQuipuxSettingsStatus.Success);
        result.Settings!.DivipoCode.Should().Be("05001");
        result.Settings.QuipuxRegistration.Should().BeTrue();
        result.Settings.QuipuxTransfer.Should().BeFalse();
        result.Settings.Elegible.Should().BeTrue();

        await using var verify = NewContext(db);
        var saved = await verify.TransitOffices.SingleAsync(
            o => o.Id == officeId, TestContext.Current.CancellationToken);
        saved.DivipoCode.Should().Be("05001");
        saved.QuipuxRegistration.Should().BeTrue();
        saved.QuipuxTransfer.Should().BeFalse();
        saved.QuipuxOther.Should().BeFalse();
    }

    /// <summary>
    /// El DIVIPO conserva los ceros a la izquierda: es texto, no un entero. Medellín es
    /// <c>05001</c> y perderlo lo mandaría a la secretaría equivocada.
    /// </summary>
    [Fact]
    public async Task Update_PreservesLeadingZeros()
    {
        var db = NewDbName();
        var officeId = await SeedOfficeAsync(db);

        await using var ctx = NewContext(db);
        var result = await NewHandler(ctx).HandleAsync(
            NewCommand(officeId, "05001", true, true, true),
            TestContext.Current.CancellationToken);

        result.Settings!.DivipoCode.Should().Be("05001").And.NotBe("5001");
    }

    /// <summary>
    /// Un DIVIPO vacío NO es un error: es el estado normal de las 311 secretarías aún no
    /// integradas. Se normaliza a null (desconocido), nunca a cadena vacía.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_BlankDivipoCode_IsNotAnError_AndNormalizesToNull(string? divipoCode)
    {
        var db = NewDbName();
        var officeId = await SeedOfficeAsync(db);

        await using var ctx = NewContext(db);
        var result = await NewHandler(ctx).HandleAsync(
            NewCommand(officeId, divipoCode, false, false, false),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(UpdateTransitOfficeQuipuxSettingsStatus.Success);
        result.Settings!.DivipoCode.Should().BeNull();
        result.Settings.Elegible.Should().BeFalse();

        await using var verify = NewContext(db);
        var saved = await verify.TransitOffices.SingleAsync(
            o => o.Id == officeId, TestContext.Current.CancellationToken);
        saved.DivipoCode.Should().BeNull();
    }

    /// <summary>
    /// Se permite declarar banderas antes de conocer el DIVIPO (el alta es gradual), pero la
    /// secretaría NO queda elegible: sin DIVIPO no se radica. Es el fallo seguro.
    /// </summary>
    [Fact]
    public async Task Update_FlagsWithoutDivipoCode_IsAllowed_ButNotElegible()
    {
        var db = NewDbName();
        var officeId = await SeedOfficeAsync(db);

        await using var ctx = NewContext(db);
        var result = await NewHandler(ctx).HandleAsync(
            NewCommand(officeId, null, matricula: true, traspaso: true, otros: true),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(UpdateTransitOfficeQuipuxSettingsStatus.Success);
        result.Settings!.QuipuxRegistration.Should().BeTrue();
        result.Settings.DivipoCode.Should().BeNull();
        result.Settings.Elegible.Should().BeFalse();
    }

    /// <summary>Con DIVIPO pero sin ninguna bandera tampoco hay nada que radicar.</summary>
    [Fact]
    public async Task Update_DivipoCodeWithoutFlags_IsNotElegible()
    {
        var db = NewDbName();
        var officeId = await SeedOfficeAsync(db);

        await using var ctx = NewContext(db);
        var result = await NewHandler(ctx).HandleAsync(
            NewCommand(officeId, "11001", false, false, false),
            TestContext.Current.CancellationToken);

        result.Settings!.Elegible.Should().BeFalse();
    }

    [Fact]
    public async Task Update_TrimsDivipoCode()
    {
        var db = NewDbName();
        var officeId = await SeedOfficeAsync(db);

        await using var ctx = NewContext(db);
        var result = await NewHandler(ctx).HandleAsync(
            NewCommand(officeId, "  05001  ", true, false, false),
            TestContext.Current.CancellationToken);

        result.Settings!.DivipoCode.Should().Be("05001");
    }

    [Theory]
    [InlineData("05-001")]
    [InlineData("ABC")]
    [InlineData("05001x")]
    [InlineData("123456789012345678901")] // 21 dígitos: excede varchar(20).
    public async Task Update_InvalidDivipoCode_Returns422_AndDoesNotPersist(string divipoCode)
    {
        var db = NewDbName();
        var officeId = await SeedOfficeAsync(db);

        await using var ctx = NewContext(db);
        var result = await NewHandler(ctx).HandleAsync(
            NewCommand(officeId, divipoCode, true, false, false),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(UpdateTransitOfficeQuipuxSettingsStatus.InvalidDivipoCode);
        result.Settings.Should().BeNull();

        await using var verify = NewContext(db);
        var saved = await verify.TransitOffices.SingleAsync(
            o => o.Id == officeId, TestContext.Current.CancellationToken);
        saved.DivipoCode.Should().BeNull();
        saved.QuipuxRegistration.Should().BeFalse();
    }

    /// <summary>Un PUT sin banderas no debe apagarlas por omisión: 422.</summary>
    [Fact]
    public async Task Update_MissingFlags_Returns422()
    {
        var db = NewDbName();
        var officeId = await SeedOfficeAsync(db);

        await using var ctx = NewContext(db);
        var result = await NewHandler(ctx).HandleAsync(
            new UpdateTransitOfficeQuipuxSettingsCommand
            {
                TransitOfficeId = officeId,
                Request = new UpdateTransitOfficeQuipuxSettingsRequest("05001", null, null, null),
            },
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(UpdateTransitOfficeQuipuxSettingsStatus.MissingFlags);
    }

    [Fact]
    public async Task Update_UnknownOffice_ReturnsNotFound()
    {
        var db = NewDbName();
        await SeedOfficeAsync(db);

        await using var ctx = NewContext(db);
        var result = await NewHandler(ctx).HandleAsync(
            NewCommand(Guid.NewGuid(), "05001", true, false, false),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(UpdateTransitOfficeQuipuxSettingsStatus.NotFound);
    }

    /// <summary>Una oficina descatalogada (is_active = false) no se parametriza.</summary>
    [Fact]
    public async Task Update_InactiveCatalogOffice_ReturnsNotFound()
    {
        var db = NewDbName();
        var officeId = await SeedOfficeAsync(db, isActive: false);

        await using var ctx = NewContext(db);
        var result = await NewHandler(ctx).HandleAsync(
            NewCommand(officeId, "05001", true, false, false),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(UpdateTransitOfficeQuipuxSettingsStatus.NotFound);
    }

    /// <summary>Reparametrizar reemplaza el estado completo (PUT), incluido volver a "desconocido".</summary>
    [Fact]
    public async Task Update_IsAFullReplace_AndCanClearDivipoCode()
    {
        var db = NewDbName();
        var officeId = await SeedOfficeAsync(db);
        var ct = TestContext.Current.CancellationToken;

        await using (var first = NewContext(db))
        {
            await NewHandler(first).HandleAsync(NewCommand(officeId, "05001", true, true, true), ct);
        }

        await using (var second = NewContext(db))
        {
            var result = await NewHandler(second).HandleAsync(
                NewCommand(officeId, null, false, true, false), ct);
            result.Settings!.DivipoCode.Should().BeNull();
            result.Settings.QuipuxRegistration.Should().BeFalse();
            result.Settings.QuipuxTransfer.Should().BeTrue();
            result.Settings.QuipuxOther.Should().BeFalse();
        }

        await using var verify = NewContext(db);
        var saved = await verify.TransitOffices.SingleAsync(o => o.Id == officeId, ct);
        saved.DivipoCode.Should().BeNull();
        saved.QuipuxTransfer.Should().BeTrue();
        saved.QuipuxRegistration.Should().BeFalse();
    }

    // ---------- Helpers ----------

    private static UpdateTransitOfficeQuipuxSettingsHandler NewHandler(FlitDbContext ctx) =>
        new(new DbTransitOfficeQuipuxSettingsWriter(ctx));

    private static UpdateTransitOfficeQuipuxSettingsCommand NewCommand(
        Guid officeId, string? divipoCode, bool matricula, bool traspaso, bool otros) =>
        new()
        {
            TransitOfficeId = officeId,
            Request = new UpdateTransitOfficeQuipuxSettingsRequest(divipoCode, matricula, traspaso, otros),
        };

    private static async Task<Guid> SeedOfficeAsync(string db, bool isActive = true)
    {
        var officeId = Guid.NewGuid();
        await using var seed = NewContext(db);
        seed.TransitOffices.Add(new TransitOffice
        {
            Id = officeId,
            Code = "5001000",
            Name = "Medellín — Secretaría de Movilidad",
            DepartmentCode = "05",
            CityCode = "05001",
            IsActive = isActive,
        });
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        return officeId;
    }

    private static string NewDbName() => $"flit-ot-quipux-settings-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
