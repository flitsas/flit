using System.Text.Json;
using Flit.Tramites.Application.BulkTramites.Processing;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.BulkTramites;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.BulkTramites;

/// <summary>
/// Reglas de creación parcial del lote (HU #12523, AC1/AC2/AC3). Lo que se prueba aquí es la
/// decisión —qué fila crea trámite y cuál no—, no la mecánica del wizard: los tres pasos llegan
/// sustituidos por <see cref="IBulkTramitesWizardGateway"/>.
/// </summary>
public sealed class BulkTramitesBatchProcessorTests
{
    private readonly IBulkTramitesBatchRepository _repository = Substitute.For<IBulkTramitesBatchRepository>();
    private readonly IBulkTramitesWizardGateway _gateway = Substitute.For<IBulkTramitesWizardGateway>();
    private readonly BulkTramitesBatchProcessor _processor;

    private static readonly Guid InstanceId = Guid.NewGuid();

    public BulkTramitesBatchProcessorTests()
    {
        _processor = new BulkTramitesBatchProcessor(_repository, _gateway, TimeProvider.System);
    }

    private BulkTramitesBatch Batch(string templateType = "matricula", params BulkTramitesBatchRow[] rows)
    {
        var batch = new BulkTramitesBatch
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            TemplateType = templateType,
            Status = BulkTramitesBatchStatus.Queued,
            SourceFilename = "lote.xlsx",
            TotalRows = rows.Length,
            CreatedAt = DateTimeOffset.UtcNow,
            Rows = [.. rows],
        };

        _repository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        return batch;
    }

    private static BulkTramitesBatchRow Row(int numero, string? structuralError = null) => new()
    {
        Id = Guid.NewGuid(),
        RowNumber = numero,
        ValuesJson = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["vin"] = "9BWZZZ377VT00425" + numero.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["propietario_1_tipo_documento"] = "CC",
            ["propietario_1_numero_documento"] = "1000" + numero.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["propietario_1_email"] = $"t{numero.ToString(System.Globalization.CultureInfo.InvariantCulture)}@example.com",
            ["propietario_1_celular"] = "3000000000",
            ["propietario_1_ciudad"] = "Bogotá",
            ["propietario_1_direccion"] = "Calle 1 # 2-3",
        }),
        StructuralErrorCode = structuralError,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private void VehiculoOk() => _gateway
        .PreviewVehicleAsync(Arg.Any<BulkTramitesRowContext>(), Arg.Any<CancellationToken>())
        .Returns(("token", (string?)null));

    private void CreacionOk() => _gateway
        .CreateTramiteAsync(Arg.Any<BulkTramitesRowContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
        .Returns(((Guid?)InstanceId, (string?)null));

    private void PersonaOk(string nombre = "TITULAR DEL RUNT") => _gateway
        .LookupPersonAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(((string?)nombre, (string?)null));

    [Fact]
    public async Task TodoBien_LaFilaQuedaCreada_YElLoteCompletado()
    {
        VehiculoOk();
        CreacionOk();
        PersonaOk();
        _gateway.SaveActorsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ActorInput>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var batch = Batch("matricula", Row(1));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        var fila = batch.Rows.Single();
        fila.Outcome.Should().Be(BulkTramitesRowOutcome.Created);
        fila.ProcedureInstanceId.Should().Be(InstanceId);
        fila.OutcomeReason.Should().BeNull();
        batch.Status.Should().Be(BulkTramitesBatchStatus.Completed);
        batch.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task VehiculoQueFalla_NoCreaTramite_YRegistraElMotivo()
    {
        _gateway.PreviewVehicleAsync(Arg.Any<BulkTramitesRowContext>(), Arg.Any<CancellationToken>())
            .Returns(((string?)null, "vehiculo_no_encontrado"));

        var batch = Batch("matricula", Row(1));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        var fila = batch.Rows.Single();
        fila.Outcome.Should().Be(BulkTramitesRowOutcome.NotCreated);
        fila.OutcomeReason.Should().Be("vehiculo_no_encontrado");
        fila.ProcedureInstanceId.Should().BeNull();

        // Sin vehículo no se intenta crear nada.
        await _gateway.DidNotReceive().CreateTramiteAsync(
            Arg.Any<BulkTramitesRowContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActorQueFalla_ElTramiteSeCreaIgual_MarcadoParaRetomar()
    {
        VehiculoOk();
        CreacionOk();
        PersonaOk();
        _gateway.SaveActorsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ActorInput>>(), Arg.Any<CancellationToken>())
            .Returns("conductor_no_encontrado");

        var batch = Batch("matricula", Row(1));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        var fila = batch.Rows.Single();
        fila.Outcome.Should().Be(BulkTramitesRowOutcome.CreatedPending);
        fila.OutcomeReason.Should().Be("conductor_no_encontrado");
        fila.ProcedureInstanceId.Should().Be(InstanceId);
    }

    [Fact]
    public async Task PersonaQueElRuntNoEncuentra_ElTramiteSeCrea_PeroLosActoresQuedanParaElWizard()
    {
        // El nombre del actor NO viene en el Excel: sale de la consulta al RUNT. Sin ella no hay
        // actor que guardar, y el motivo tiene que decir QUÉ documento falló, porque una fila de
        // traspaso puede traer hasta 8 personas.
        VehiculoOk();
        CreacionOk();
        _gateway.LookupPersonAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), "CC", "10001", Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)BulkTramitesWizardGateway.ConductorNoEncontrado));

        var batch = Batch("matricula", Row(1));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        var fila = batch.Rows.Single();
        fila.Outcome.Should().Be(BulkTramitesRowOutcome.CreatedPending);
        fila.OutcomeReason.Should().Be("conductor_no_encontrado:CC 10001");
        fila.ProcedureInstanceId.Should().Be(InstanceId);

        // Todas o ninguna: el guardado del wizard es un upsert del conjunto completo.
        await _gateway.DidNotReceive().SaveActorsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ActorInput>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ElNombreQueSeGuarda_EsElQueReportaElRunt_ConLosDatosDeContactoDelExcel()
    {
        VehiculoOk();
        CreacionOk();
        PersonaOk("ANA MARIA GOMEZ");
        IReadOnlyList<ActorInput>? guardados = null;
        _gateway.SaveActorsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Do<IReadOnlyList<ActorInput>>(a => guardados = a), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var batch = Batch("matricula", Row(1));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        var actor = guardados.Should().ContainSingle().Subject;
        actor.NombreCompleto.Should().Be("ANA MARIA GOMEZ");
        actor.Telefono.Should().Be("3000000000");
        actor.Ciudad.Should().Be("Bogotá");
        actor.Direccion.Should().Be("Calle 1 # 2-3");
        batch.Rows.Single().Outcome.Should().Be(BulkTramitesRowOutcome.Created);
    }

    [Fact]
    public async Task UnaFilaQueFalla_NoDetieneLasDemas()
    {
        // La fila 1 se cae en la consulta del vehículo; la 2 sigue su curso.
        _gateway.PreviewVehicleAsync(Arg.Any<BulkTramitesRowContext>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => ((string?)null, "placa_invalida"),
                _ => ("token", (string?)null));
        CreacionOk();
        PersonaOk();
        _gateway.SaveActorsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ActorInput>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var batch = Batch("matricula", Row(1), Row(2));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        batch.Rows.Single(r => r.RowNumber == 1).Outcome.Should().Be(BulkTramitesRowOutcome.NotCreated);
        batch.Rows.Single(r => r.RowNumber == 2).Outcome.Should().Be(BulkTramitesRowOutcome.Created);
        batch.Status.Should().Be(BulkTramitesBatchStatus.Completed);
    }

    [Fact]
    public async Task FilaConErrorEstructural_NiSeConsulta_PorqueYaVenIaRechazadaDeHU12522()
    {
        VehiculoOk();
        CreacionOk();
        PersonaOk();

        var batch = Batch("traspaso", Row(1, structuralError: "porcentajes_no_suman_100"));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        batch.Rows.Single().Outcome.Should().BeNull();
        await _gateway.DidNotReceive().PreviewVehicleAsync(
            Arg.Any<BulkTramitesRowContext>(), Arg.Any<CancellationToken>());
        batch.Status.Should().Be(BulkTramitesBatchStatus.Completed);
    }

    [Fact]
    public async Task LoteQueNoEstaEnCola_NoSeReprocesa()
    {
        var batch = Batch("matricula", Row(1));
        batch.Status = BulkTramitesBatchStatus.Completed;

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        await _gateway.DidNotReceive().PreviewVehicleAsync(
            Arg.Any<BulkTramitesRowContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ErrorInesperadoEnUnaFila_LaMarcaYSigue_SinTumbarElLote()
    {
        _gateway.PreviewVehicleAsync(Arg.Any<BulkTramitesRowContext>(), Arg.Any<CancellationToken>())
            .Returns<(string?, string?)>(_ => throw new InvalidOperationException("proveedor caído"));

        var batch = Batch("matricula", Row(1));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        var fila = batch.Rows.Single();
        fila.Outcome.Should().Be(BulkTramitesRowOutcome.NotCreated);
        fila.OutcomeReason.Should().Be("error_inesperado");
        batch.Status.Should().Be(BulkTramitesBatchStatus.Completed);
    }
}
