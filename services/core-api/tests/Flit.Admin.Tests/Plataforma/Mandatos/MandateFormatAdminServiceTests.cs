using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #13171 (Feature #13118, Épica #13090) — servicio de edición de formatos sobre una base EN MEMORIA: aquí se
/// prueba la publicación de plantillas (versiones inmutables), que no se puede hacer contra la base local compartida.
/// Uso: <c>await service.UpdateAsync("bello", new(rowVersion, null, null, "Contrato {{placa}}"), userId)</c>.
/// </summary>
public sealed class MandateFormatAdminServiceTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid Author = Guid.NewGuid();

    private static async Task<(FlitDbContext Db, MandateFormatAdminService Service)> NewAsync()
    {
        var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"mandate-format-admin-{Guid.NewGuid()}").Options);
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
        return (db, new MandateFormatAdminService(new MandateFormatRepository(db)));
    }

    private static UpdateMandateFormatRequest Req(long? rv, string? name = null, string? mode = null, string? body = null) =>
        new(rv, name, mode, body);

    [Fact]
    public async Task AC3_PublicarPlantillaValida_CreaVersionNuevaYPasaAserLaVigente()
    {
        var (db, service) = await NewAsync();
        await using var _ = db;

        var first = await service.UpdateAsync("bello", Req(0, body: "Contrato sobre {{placa}} de {{mandante_nombre}}"), Author, Ct);
        var second = await service.UpdateAsync("bello", Req(first.Current!.RowVersion, body: "Texto dos {{fecha_firma}}"), Author, Ct);

        first.Status.Should().Be(MandateFormatUpdateStatus.Ok);
        first.PublishedVersion.Should().Be(1);
        second.PublishedVersion.Should().Be(2);
        var detail = (await service.GetAsync("bello", Ct))!;
        detail.Format.CurrentVersion.Should().Be(2);
        detail.CurrentBody.Should().Be("Texto dos {{fecha_firma}}");
        detail.Versions.Select(v => v.VersionNumber).Should().Equal(1, 2);
        (await service.GetVersionAsync("bello", 1, Ct))!.Value.Body.Should().Contain("{{placa}}");
    }

    [Fact]
    public async Task AC4_PlantillaConVariableDesconocida_400_ConPosicion_YNoCreaVersion()
    {
        var (db, service) = await NewAsync();
        await using var _ = db;

        var result = await service.UpdateAsync("bello", Req(0, body: "Línea\nCédula {{cedula_inventada}}"), Author, Ct);

        result.Status.Should().Be(MandateFormatUpdateStatus.BadRequest);
        result.ErrorCode.Should().Be("plantilla_variable_invalida");
        result.UnknownVariables.Should().ContainSingle().Which.Should().Be(new MandateFormatUnknownVariable("cedula_inventada", 2, 8));
        db.MandateFormatVersions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "plantilla_vacia")]
    [InlineData("Hola {{placa", "plantilla_sintaxis_invalida")]
    public async Task AC4_PlantillaVaciaOSinCerrar_SeRechaza(string body, string error)
    {
        var (db, service) = await NewAsync();
        await using var _ = db;

        var result = await service.UpdateAsync("bello", Req(0, body: body), Author, Ct);

        result.ErrorCode.Should().Be(error);
        db.MandateFormatVersions.Should().BeEmpty();
    }

    [Fact]
    public async Task Auto_NoAdmitePlantilla_PeroSiRenombrarseYCambiarDeTipo()
    {
        var (db, service) = await NewAsync();
        await using var _ = db;

        var template = await service.UpdateAsync("auto", Req(0, body: "Hola {{placa}}"), Author, Ct);
        var rename = await service.UpdateAsync("auto", Req(0, name: "Según el organismo"), Author, Ct);

        template.ErrorCode.Should().Be("formato_sin_plantilla");
        rename.Status.Should().Be(MandateFormatUpdateStatus.Ok);
        rename.Current!.Name.Should().Be("Según el organismo");
        db.MandateFormatVersions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("signer")]
    [InlineData("institutional")]
    [InlineData("open")]
    public async Task AC2_CambiarTipo_GuardaElModoCorrespondiente(string mode)
    {
        var (db, service) = await NewAsync();
        await using var _ = db;
        var start = mode == "open" ? "signer" : "open";
        var first = await service.UpdateAsync("generico", Req(0, mode: start), Author, Ct);

        var result = await service.UpdateAsync("generico", Req(first.Current!.RowVersion, mode: mode), Author, Ct);

        result.Status.Should().Be(MandateFormatUpdateStatus.Ok);
        result.Current!.AssignmentMode.Should().Be(mode);
    }

    [Fact]
    public async Task ModoInvalido_Responde400_AssignmentModeInvalido()
    {
        var (db, service) = await NewAsync();
        await using var _ = db;

        var result = await service.UpdateAsync("generico", Req(0, mode: "firma_fisica"), Author, Ct);

        result.Status.Should().Be(MandateFormatUpdateStatus.BadRequest);
        result.ErrorCode.Should().Be("assignment_mode_invalido");
    }

    [Fact]
    public async Task AC5_NombreRepetidoEnOtroFormato_Vacio_OMuyLargo_SeRechaza_YNoModificaNada()
    {
        var (db, service) = await NewAsync();
        await using var _ = db;

        (await service.UpdateAsync("generico", Req(0, name: "bello"), Author, Ct)).ErrorCode.Should().Be("nombre_repetido");
        (await service.UpdateAsync("generico", Req(0, name: " "), Author, Ct)).ErrorCode.Should().Be("nombre_vacio");
        (await service.UpdateAsync("generico", Req(0, name: new string('x', 81)), Author, Ct)).ErrorCode
            .Should().Be("nombre_demasiado_largo");
        (await service.UpdateAsync("generico", Req(0, name: new string('x', 80)), Author, Ct)).Status
            .Should().Be(MandateFormatUpdateStatus.Ok);

        (await service.ListAsync(Ct)).Single(f => f.Code == "bello").Name.Should().Be("Bello");
    }

    [Fact]
    public async Task AC5_ElMismoNombreEnElMismoFormato_NoEsRepetido()
    {
        var (db, service) = await NewAsync();
        await using var _ = db;

        var result = await service.UpdateAsync("bello", Req(0, name: "Bello"), Author, Ct);

        result.Status.Should().Be(MandateFormatUpdateStatus.Ok);
        result.Changed.Should().BeFalse();
    }

    [Fact]
    public async Task AC6_RowVersionDesactualizadoOAusente_Conflicto_YNoCambiaNada()
    {
        var (db, service) = await NewAsync();
        await using var _ = db;
        var setting = await db.MandateFormatSettings.SingleAsync(s => s.FormatCode == "bello", Ct);
        setting.RowVersion = 3;
        await db.SaveChangesAsync(Ct);

        var stale = await service.UpdateAsync("bello", Req(2, name: "Otro"), Author, Ct);
        var missing = await service.UpdateAsync("bello", Req(null, name: "Otro"), Author, Ct);

        stale.Status.Should().Be(MandateFormatUpdateStatus.Conflict);
        missing.Status.Should().Be(MandateFormatUpdateStatus.Conflict);
        stale.ErrorCode.Should().Be("row_version_conflict");
        (await service.ListAsync(Ct)).Single(f => f.Code == "bello").Name.Should().Be("Bello");
    }

    [Fact]
    public async Task AC7_CodigoFueraDelCatalogo_404_YNoCreaFila()
    {
        var (db, service) = await NewAsync();
        await using var _ = db;

        var result = await service.UpdateAsync("nuevo", Req(0, name: "Nuevo", body: "Hola"), Author, Ct);

        result.Status.Should().Be(MandateFormatUpdateStatus.NotFound);
        db.MandateFormatSettings.Count().Should().Be(5);
        (await service.GetAsync("nuevo", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Lista_ConFormatosSinFila_UsaLosValoresDeFabrica_SinRowVersion()
    {
        var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"mandate-format-empty-{Guid.NewGuid()}").Options);
        await using var _ = db;
        var service = new MandateFormatAdminService(new MandateFormatRepository(db));

        var all = await service.ListAsync(Ct);
        var edit = await service.UpdateAsync("bello", Req(0, name: "Otro"), Author, Ct);

        all.Select(f => f.Code).Should().Equal("auto", "generico", "sabaneta", "bello", "municipio");
        all.Should().OnlyContain(f => f.RowVersion == null && f.CurrentVersion == 0);
        edit.Status.Should().Be(MandateFormatUpdateStatus.NotFound, "sin fila no se crea nada");
    }

    // ── Restablecer redacción de fábrica ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Restablecer_FormatoConPlantillaEditada_VuelveAFabrica_ConservaNombreYTipo()
    {
        var (db, service) = await NewAsync();
        await using var _ = db;
        await service.UpdateAsync("bello", Req(0, name: "Bello 2026", body: "Contrato {{placa}}"), Author, Ct);

        var result = await service.ResetTemplateAsync("bello", 0, Author, Ct);

        result.Status.Should().Be(MandateFormatUpdateStatus.Ok);
        result.Changed.Should().BeTrue();
        result.Previous!.CurrentVersion.Should().Be(1);
        result.Current!.CurrentVersion.Should().Be(0);
        result.Current.Name.Should().Be("Bello 2026");
    }

    [Fact]
    public async Task Restablecer_Auto_NoTieneRedaccionPropia_400()
    {
        var (db, service) = await NewAsync();
        await using var _ = db;

        var result = await service.ResetTemplateAsync("auto", 0, Author, Ct);

        result.Status.Should().Be(MandateFormatUpdateStatus.BadRequest);
        result.ErrorCode.Should().Be("formato_sin_plantilla");
    }

    [Fact]
    public async Task Restablecer_CodigoFueraDelCatalogo_404()
    {
        var (db, service) = await NewAsync();
        await using var _ = db;

        (await service.ResetTemplateAsync("nuevo", 0, Author, Ct)).Status.Should().Be(MandateFormatUpdateStatus.NotFound);
    }
}
