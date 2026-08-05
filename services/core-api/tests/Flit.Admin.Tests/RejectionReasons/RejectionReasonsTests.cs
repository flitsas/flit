using Flit.Admin.Application.RejectionReasons;
using Flit.Admin.Domain.RejectionReasons;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.RejectionReasons;

/// <summary>
/// Catálogo global de causales de rechazo (CRUD SuperAdmin). Lo que sustituye al motivo escrito a
/// mano como dato agregable del reporte de motivos.
/// </summary>
public sealed class RejectionReasonsTests
{
    [Fact] // El código es la llave estable de los reportes: se normaliza a slug en minúsculas.
    public async Task Create_NormalizaElCodigoASlug()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateRejectionReasonHandler(new RejectionReasonRepository(ctx));

        var result = await handler.HandleAsync(
            new CreateRejectionReasonRequest("SOAT No Vigente", "SOAT no vigente", "traspaso", null),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RejectionReasonOutcome.Ok);
        result.Reason!.Code.Should().Be("soat_no_vigente");
    }

    [Fact] // Aceptar «SOAT No Vigente» y «soat_no_vigente» como códigos distintos reintroduciría
           // el problema del texto libre por la puerta de atrás.
    public async Task Create_RechazaCodigoDuplicado()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var handler = new CreateRejectionReasonHandler(new RejectionReasonRepository(ctx));
        var ct = TestContext.Current.CancellationToken;

        await handler.HandleAsync(
            new CreateRejectionReasonRequest("soat_no_vigente", "SOAT no vigente", "traspaso", null), null, ct);

        var duplicate = await handler.HandleAsync(
            new CreateRejectionReasonRequest("SOAT no vigente", "Otra cosa", "traspaso", null), null, ct);

        duplicate.Outcome.Should().Be(RejectionReasonOutcome.ValidationFailed);
        duplicate.Error.Should().Contain("soat_no_vigente");
    }

    [Fact] // Las causales no son intercambiables entre procesos.
    public async Task Create_RechazaModalidadDesconocida()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateRejectionReasonHandler(new RejectionReasonRepository(ctx));

        var result = await handler.HandleAsync(
            new CreateRejectionReasonRequest("x", "X", "cambio_de_color", null),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RejectionReasonOutcome.ValidationFailed);
    }

    [Fact] // No hay borrado: una causal retirada sigue resolviendo el nombre de los rechazos
           // históricos, pero deja de ofrecerse en el modal.
    public async Task Retirar_LaSacaDelListadoActivoPeroLaConserva()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var repo = new RejectionReasonRepository(ctx);
        var ct = TestContext.Current.CancellationToken;

        var created = await repo.CreateAsync("improntas", "Improntas", "matricula_inicial", 10, null, ct);
        await new SetRejectionReasonActiveHandler(repo).HandleAsync(created.Id, false, null, ct);

        var activas = await new ListRejectionReasonsHandler(repo)
            .HandleAsync("matricula_inicial", includeInactive: false, ct);
        var todas = await new ListRejectionReasonsHandler(repo)
            .HandleAsync("matricula_inicial", includeInactive: true, ct);

        activas.Should().BeEmpty();
        todas.Should().ContainSingle().Which.IsActive.Should().BeFalse();
    }

    [Fact] // El modal solo debe ofrecer causales de la modalidad del trámite.
    public async Task List_FiltraPorModalidad()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var repo = new RejectionReasonRepository(ctx);
        var ct = TestContext.Current.CancellationToken;

        await repo.CreateAsync("manifiesto_aduana", "Manifiesto de aduana", "matricula_inicial", 10, null, ct);
        await repo.CreateAsync("soat_no_vigente", "SOAT no vigente", "traspaso", 10, null, ct);

        var traspaso = await new ListRejectionReasonsHandler(repo).HandleAsync("traspaso", false, ct);

        traspaso.Should().ContainSingle().Which.Description.Should().Be("SOAT no vigente");
    }

    [Fact] // Guard del rechazo: no se persisten causales inactivas ni de otra modalidad.
    public async Task FilterValidIds_DescartaInactivasYDeOtraModalidad()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var repo = new RejectionReasonRepository(ctx);
        var ct = TestContext.Current.CancellationToken;

        var valida = await repo.CreateAsync("soat_no_vigente", "SOAT no vigente", "traspaso", 10, null, ct);
        var otraModalidad = await repo.CreateAsync("improntas", "Improntas", "matricula_inicial", 10, null, ct);
        var retirada = await repo.CreateAsync("impuestos", "Impuestos", "traspaso", 20, null, ct);
        await repo.SetActiveAsync(retirada.Id, false, null, ct);

        var validas = await repo.FilterValidIdsAsync(
            [valida.Id, otraModalidad.Id, retirada.Id], "traspaso", ct);

        validas.Should().BeEquivalentTo([valida.Id]);
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
