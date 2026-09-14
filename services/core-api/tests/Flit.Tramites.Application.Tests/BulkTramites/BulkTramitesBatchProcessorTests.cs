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
        _processor = new BulkTramitesBatchProcessor(_repository, _gateway, TimeProvider.System, providerRetryDelay: TimeSpan.Zero);
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
    public async Task ProveedorCaidoEnLaConsultaDePersona_SeReintentaUnaVez_YSiRespondeLaFilaSeCrea()
    {
        // Kyverum daba 502 transitorios en la consulta que seguía a otra y respondía bien segundos
        // después (visto en vivo). Un reintento convierte ese falso «por retomar» en creado.
        VehiculoOk();
        CreacionOk();
        _gateway.LookupPersonAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => ((string?)null, (string?)BulkTramitesWizardGateway.ConsultaConductorFallida),
                _ => ("TITULAR DEL RUNT", (string?)null));
        _gateway.SaveActorsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ActorInput>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var batch = Batch("matricula", Row(1));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        batch.Rows.Single().Outcome.Should().Be(BulkTramitesRowOutcome.Created);
        await _gateway.Received(2).LookupPersonAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProveedorCaidoDosVecesSeguidas_NoSeInsiste_YLaFilaQuedaPorRetomar()
    {
        VehiculoOk();
        CreacionOk();
        _gateway.LookupPersonAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(((string?)null, (string?)BulkTramitesWizardGateway.ConsultaConductorFallida));

        var batch = Batch("matricula", Row(1));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        batch.Rows.Single().Outcome.Should().Be(BulkTramitesRowOutcome.CreatedPending);
        batch.Rows.Single().OutcomeReason.Should().Be("consulta_conductor_fallida:CC 10001");
        await _gateway.Received(2).LookupPersonAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProveedorCaidoEnElVehiculo_SeReintentaUnaVez_PeroNoEncontradoNoSeReintenta()
    {
        _gateway.PreviewVehicleAsync(Arg.Any<BulkTramitesRowContext>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => ((string?)null, (string?)BulkTramitesVehicleGate.ConsultaVehiculoFallida),
                _ => ("token", (string?)null),
                _ => ((string?)null, (string?)BulkTramitesVehicleGate.VehiculoNoEncontrado));
        CreacionOk();
        PersonaOk();
        _gateway.SaveActorsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ActorInput>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var batch = Batch("matricula", Row(1), Row(2));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        // Fila 1: caída → reintento → OK → creada. Fila 2: no encontrado → sin reintento.
        batch.Rows.Single(r => r.RowNumber == 1).Outcome.Should().Be(BulkTramitesRowOutcome.Created);
        batch.Rows.Single(r => r.RowNumber == 2).Outcome.Should().Be(BulkTramitesRowOutcome.NotCreated);
        await _gateway.Received(3).PreviewVehicleAsync(Arg.Any<BulkTramitesRowContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoteReclamadoEnProceso_RetomaSoloLasFilasSinResultado()
    {
        // Un lote huérfano (el proceso murió a mitad) vuelve por el worker en Processing: la fila ya
        // resuelta no se toca —no se duplica el trámite— y la pendiente se procesa.
        VehiculoOk();
        CreacionOk();
        PersonaOk();
        _gateway.SaveActorsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ActorInput>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var yaResuelta = Row(1);
        yaResuelta.Outcome = BulkTramitesRowOutcome.Created;
        yaResuelta.ProcedureInstanceId = Guid.NewGuid();
        var batch = Batch("matricula", yaResuelta, Row(2));
        batch.Status = BulkTramitesBatchStatus.Processing;

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        batch.Rows.Single(r => r.RowNumber == 2).Outcome.Should().Be(BulkTramitesRowOutcome.Created);
        batch.Status.Should().Be(BulkTramitesBatchStatus.Completed);
        await _gateway.Received(1).PreviewVehicleAsync(Arg.Any<BulkTramitesRowContext>(), Arg.Any<CancellationToken>());
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

    // ----- HU #12538: actores persona jurídica (NIT) -----

    private static BulkTramitesBatchRow FilaConEmpresa(string? representanteDocumento = null, string? email = "empresa@example.com") => new()
    {
        Id = Guid.NewGuid(),
        RowNumber = 1,
        ValuesJson = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["vin"] = "9BWZZZ377VT004259",
            ["propietario_1_tipo_documento"] = "NIT",
            ["propietario_1_numero_documento"] = "900123456",
            ["propietario_1_representante_documento"] = representanteDocumento,
            ["propietario_1_email"] = email,
            ["propietario_1_celular"] = "3000000000",
        }),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static readonly BulkTramitesLegalRepresentative RlPrimario =
        new("CC", "1020304050", "HECTOR CARDENAS", "hector@example.com", "3111111111");

    private static readonly BulkTramitesLegalRepresentative RlSecundario =
        new("CC", "52000111", "MARIA LOPEZ", "maria@example.com", "3222222222");

    private void EmpresaOk(params BulkTramitesLegalRepresentative[] representantes) => _gateway
        .LookupCompanyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), "900123456", Arg.Any<CancellationToken>())
        .Returns((new BulkTramitesCompanyLookup(
            "TRANSPORTES DEMO S.A.S., ADEMÁS PODRÁ GIRAR BAJO LA SIGLA TDEMO",
            representantes.Length == 0
                ? null
                : new BulkTramitesCompanyDirectoryEntry("contacto@demo.com", "Cra 1 # 2-3", "Medellín", "6041234567", representantes)),
            (string?)null));

    [Fact]
    public async Task NitConRepresentanteRegistrado_SeGuardaComoJuridica_ConElRlPrimarioYSinMecanismoDeFirma()
    {
        VehiculoOk();
        CreacionOk();
        EmpresaOk(RlPrimario, RlSecundario);
        IReadOnlyList<ActorInput>? guardados = null;
        _gateway.SaveActorsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Do<IReadOnlyList<ActorInput>>(a => guardados = a), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var batch = Batch("matricula", FilaConEmpresa());

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        batch.Rows.Single().Outcome.Should().Be(BulkTramitesRowOutcome.Created);
        var actor = guardados.Should().ContainSingle().Subject;
        actor.PersonType.Should().Be("juridical");
        // La razón social se recorta en la primera coma, igual que en el wizard.
        actor.NombreCompleto.Should().Be("TRANSPORTES DEMO S.A.S.");
        // Lo escrito en la fila manda; el directorio rellena lo que falta.
        actor.Email.Should().Be("empresa@example.com");
        actor.Telefono.Should().Be("3000000000");
        actor.Ciudad.Should().Be("Medellín");
        actor.Direccion.Should().Be("Cra 1 # 2-3");
        var rl = actor.RepresentanteLegal.Should().NotBeNull().And.Subject.As<ActorRepresentanteLegal>();
        rl.NumeroDocumento.Should().Be("1020304050");
        rl.NombreCompleto.Should().Be("HECTOR CARDENAS");
        rl.Email.Should().Be("hector@example.com");
        rl.Telefono.Should().Be("3111111111");
        rl.MecanismoFirma.Should().BeNull("sin elección aplica la precedencia del baúl al guardar");
        // La persona jurídica NO pasa por la consulta de conductor.
        await _gateway.DidNotReceive().LookupPersonAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NitSinRepresentanteRegistrado_ElTramiteSeCrea_YQuedaPorRetomarConElNit()
    {
        VehiculoOk();
        CreacionOk();
        EmpresaOk();

        var batch = Batch("matricula", FilaConEmpresa());

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        var fila = batch.Rows.Single();
        fila.Outcome.Should().Be(BulkTramitesRowOutcome.CreatedPending);
        fila.OutcomeReason.Should().Be("persona_juridica_sin_representante_registrado:NIT 900123456");
        fila.ProcedureInstanceId.Should().Be(InstanceId);
        await _gateway.DidNotReceive().SaveActorsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ActorInput>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NitQueRuesNoConoce_ElTramiteSeCrea_YNingunActorSeGuarda()
    {
        VehiculoOk();
        CreacionOk();
        _gateway.LookupCompanyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(((BulkTramitesCompanyLookup?)null, "empresa_no_encontrada"));

        var batch = Batch("matricula", FilaConEmpresa());

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        var fila = batch.Rows.Single();
        fila.Outcome.Should().Be(BulkTramitesRowOutcome.CreatedPending);
        fila.OutcomeReason.Should().Be("empresa_no_encontrada:NIT 900123456");
        await _gateway.DidNotReceive().SaveActorsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ActorInput>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CedulaDeRepresentanteEnLaFila_EligeEseRepresentante_AunqueNoSeaElPrimario()
    {
        VehiculoOk();
        CreacionOk();
        EmpresaOk(RlPrimario, RlSecundario);
        IReadOnlyList<ActorInput>? guardados = null;
        _gateway.SaveActorsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Do<IReadOnlyList<ActorInput>>(a => guardados = a), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        // Con puntos y cero a la izquierda a propósito: se empata por dígitos, como en el wizard.
        var batch = Batch("matricula", FilaConEmpresa(representanteDocumento: "052.000.111"));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        batch.Rows.Single().Outcome.Should().Be(BulkTramitesRowOutcome.Created);
        guardados.Should().ContainSingle().Which.RepresentanteLegal!.NombreCompleto.Should().Be("MARIA LOPEZ");
    }

    [Fact]
    public async Task CedulaDeRepresentanteQueNoEstaRegistrada_LaFilaQuedaPorRetomar()
    {
        VehiculoOk();
        CreacionOk();
        EmpresaOk(RlPrimario);

        var batch = Batch("matricula", FilaConEmpresa(representanteDocumento: "99999999"));

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        var fila = batch.Rows.Single();
        fila.Outcome.Should().Be(BulkTramitesRowOutcome.CreatedPending);
        fila.OutcomeReason.Should().Be("representante_no_registrado:NIT 900123456");
    }

    [Fact]
    public async Task RuesCaido_SeReintentaUnaVez_YSiRespondeLaFilaSeCrea()
    {
        VehiculoOk();
        CreacionOk();
        _gateway.LookupCompanyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                ((BulkTramitesCompanyLookup?)null, "consulta_empresa_fallida"),
                (new BulkTramitesCompanyLookup(
                    "TRANSPORTES DEMO S.A.S.",
                    new BulkTramitesCompanyDirectoryEntry(null, null, null, null, [RlPrimario])), (string?)null));
        _gateway.SaveActorsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ActorInput>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var batch = Batch("matricula", FilaConEmpresa());

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        batch.Rows.Single().Outcome.Should().Be(BulkTramitesRowOutcome.Created);
        await _gateway.Received(2).LookupCompanyAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RuesCaidoDosVeces_LaFilaQuedaPorRetomar_ConMotivoDistintoDeNoEncontrada()
    {
        VehiculoOk();
        CreacionOk();
        _gateway.LookupCompanyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(((BulkTramitesCompanyLookup?)null, "consulta_empresa_fallida"));

        var batch = Batch("matricula", FilaConEmpresa());

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        var fila = batch.Rows.Single();
        fila.Outcome.Should().Be(BulkTramitesRowOutcome.CreatedPending);
        fila.OutcomeReason.Should().Be("consulta_empresa_fallida:NIT 900123456");
        await _gateway.Received(2).LookupCompanyAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
