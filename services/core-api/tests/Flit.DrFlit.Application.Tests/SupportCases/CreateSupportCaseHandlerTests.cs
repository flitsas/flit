using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.SupportCases;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.DrFlit.Application.Tests.SupportCases;

/// <summary>
/// HU #12925 — radicación end-to-end del caso: pending → adjuntos → Bug → created | failed.
/// Uso de ejemplo:
/// <code>
/// var r = await handler.HandleAsync(new CreateSupportCaseCommand(tenant, user, isSuperAdmin, "Nombre", …, attachmentIds), ct);
/// // r.Outcome == Created ⇒ r.CaseId es el número del work item
/// </code>
/// </summary>
public sealed class CreateSupportCaseHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("0199a000-0000-7000-8000-0000000000aa");
    private static readonly Guid User = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    private static readonly Guid CaseRow = Guid.Parse("0199a000-0000-7000-8000-00000000c001");
    private static readonly Guid A1 = Guid.Parse("0199a000-0000-7000-8000-00000000a001");
    private static readonly Guid A2 = Guid.Parse("0199a000-0000-7000-8000-00000000a002");
    private static readonly Guid A3 = Guid.Parse("0199a000-0000-7000-8000-00000000a003");
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 15, 0, 0, TimeSpan.Zero);

    private readonly IDrFlitSupportCaseGateway _gateway = Substitute.For<IDrFlitSupportCaseGateway>();
    private readonly IDrFlitSupportCaseRepository _repository = Substitute.For<IDrFlitSupportCaseRepository>();
    private readonly IDrFlitSupportAttachmentStore _attachments = Substitute.For<IDrFlitSupportAttachmentStore>();
    private readonly IDrFlitSupportCaseSettings _settings = Substitute.For<IDrFlitSupportCaseSettings>();

    private DrFlitSupportTicket? _sentTicket;
    private IReadOnlyList<DrFlitTicketAttachment>? _sentFiles;

    public CreateSupportCaseHandlerTests()
    {
        _settings.MaxAttachments.Returns(5);
        _settings.DeployEnvironment.Returns(DrFlitDeployEnvironment.QA);
        _gateway.DestinationName.Returns("FLIT - SOPORTE");
        _gateway.ResolveAffectedModule(Arg.Any<string?>()).Returns("Matricula");
        _repository.InsertPendingAsync(default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(CaseRow);
        _attachments.GetPendingAsync(default, default, default!, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(ci => (ci.ArgAt<IReadOnlyCollection<Guid>?>(2) ?? [])
                .Select(id => new DrFlitPendingAttachment(id, $"{id:N}.png", "image/png", $"fm-{id:N}"))
                .ToList());
        GatewayReturns(new DrFlitBugCreationResult(true, 13001, "https://dev.azure.com/FlitDevOps/FLIT%20-%20SOPORTE/_workitems/edit/13001", 0, null));
    }

    private void GatewayReturns(DrFlitBugCreationResult result) =>
        _gateway.CreateBugAsync(default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(ci =>
            {
                _sentTicket = ci.ArgAt<DrFlitSupportTicket>(0);
                _sentFiles = ci.ArgAt<IReadOnlyList<DrFlitTicketAttachment>>(1);
                return result;
            });

    private CreateSupportCaseHandler Handler() =>
        new(_gateway, _repository, _attachments, _settings, new FixedClock(Now), NullLogger<CreateSupportCaseHandler>.Instance);

    private static CreateSupportCaseCommand Command(
        bool superAdmin = false,
        IReadOnlyList<Guid>? attachments = null,
        string? email = "usuario.prueba@example.test",
        string? prioridad = "Alta",
        string? frecuencia = "siempre",
        string? detalle = "Al subir la factura sale error",
        string? titulo = "No puedo subir la factura",
        string? telefono = "3000000000") =>
        new(Tenant, User, superAdmin, "Usuario Prueba", email, telefono, "Empresa Demo S.A.S", detalle,
            "Que la factura quede cargada", frecuencia, titulo, prioridad, "/tramites/matricula", attachments ?? []);

    private Task<CreateSupportCaseResult> Handle(CreateSupportCaseCommand command) =>
        Handler().HandleAsync(command, TestContext.Current.CancellationToken);

    // ── AC1 — camino feliz ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_PersistePendingCreaElBugMarcaCreatedYVinculaAdjuntos()
    {
        var result = await Handle(Command(attachments: [A1, A2]));

        result.Should().Be(new CreateSupportCaseResult(CreateSupportCaseOutcome.Created, 13001, null, 0));
        Received.InOrder(() =>
        {
            _repository.InsertPendingAsync(Arg.Any<DrFlitSupportCaseRecord>(), TestContext.Current.CancellationToken);
            _gateway.CreateBugAsync(Arg.Any<DrFlitSupportTicket>(), Arg.Any<IReadOnlyList<DrFlitTicketAttachment>>(), TestContext.Current.CancellationToken);
            _repository.MarkCreatedAsync(CaseRow, 13001, 2, 0, TestContext.Current.CancellationToken);
            _attachments.LinkToCaseAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { A1, A2 })), CaseRow, TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task AC1_ElRegistroYElBugLlevanLoConfirmadoMasAmbienteYModuloResueltos()
    {
        DrFlitSupportCaseRecord? record = null;
        _repository.InsertPendingAsync(Arg.Do<DrFlitSupportCaseRecord>(r => record = r), TestContext.Current.CancellationToken).Returns(CaseRow);

        await Handle(Command());

        record!.TenantId.Should().Be(Tenant);
        record.UserId.Should().Be(User);
        record.AdoProject.Should().Be("FLIT - SOPORTE");
        record.AffectedModule.Should().Be("Matricula");
        record.Ticket.Should().BeSameAs(_sentTicket);
        _sentTicket!.Should().BeEquivalentTo(new DrFlitSupportTicket(
            "No puedo subir la factura", "Usuario Prueba", "usuario.prueba@example.test", "3000000000", "Empresa Demo S.A.S",
            "Al subir la factura sale error", "Que la factura quede cargada", DrFlitCaseFrequency.Siempre, DrFlitCasePriority.Alta,
            DrFlitDeployEnvironment.QA, "/tramites/matricula", Now));
    }

    [Fact]
    public async Task AC1_LosAdjuntosSeAbrenDesdeElAlmacenamientoAlSubirse()
    {
        await Handle(Command(attachments: [A1]));

        _sentFiles.Should().ContainSingle().Which.FileName.Should().Be($"{A1:N}.png");
        await _sentFiles![0].OpenAsync(TestContext.Current.CancellationToken);
        await _attachments.Received(1).OpenAsync($"fm-{A1:N}", TestContext.Current.CancellationToken);
    }

    // ── AC2 — Azure DevOps caído ────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_ProveedorCaido_MarcaFailedYDevuelveProviderUnavailableSinVincular()
    {
        GatewayReturns(DrFlitBugCreationResult.Failed("http_503", attachmentsFailed: 1));

        var result = await Handle(Command(attachments: [A1]));

        result.Outcome.Should().Be(CreateSupportCaseOutcome.ProviderUnavailable);
        result.Error.Should().Be(CreateSupportCaseHandler.ProviderUnavailableMessage);
        result.CaseId.Should().BeNull();
        await _repository.Received(1).MarkFailedAsync(CaseRow, "http_503", 1, 1, TestContext.Current.CancellationToken);
        await _repository.DidNotReceiveWithAnyArgs().MarkCreatedAsync(default, default, default, default, TestContext.Current.CancellationToken);
        await _attachments.DidNotReceiveWithAnyArgs().LinkToCaseAsync(default!, default, TestContext.Current.CancellationToken);
    }

    // ── AC3 — adjuntos parcialmente fallidos ───────────────────────────────────────────

    [Fact]
    public async Task AC3_UnoDeTresAdjuntosFalla_ElCasoSeCreaConAttachmentsFailed1()
    {
        GatewayReturns(new DrFlitBugCreationResult(true, 13002, "u", 1, null));

        var result = await Handle(Command(attachments: [A1, A2, A3]));

        result.Outcome.Should().Be(CreateSupportCaseOutcome.Created);
        result.AttachmentsFailed.Should().Be(1);
        _sentFiles.Should().HaveCount(3);
        await _repository.Received(1).MarkCreatedAsync(CaseRow, 13002, 3, 1, TestContext.Current.CancellationToken);
    }

    // ── AC4 — caseUrl solo para SuperAdmin ──────────────────────────────────────────────

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "https://dev.azure.com/FlitDevOps/FLIT%20-%20SOPORTE/_workitems/edit/13001")]
    public async Task AC4_CaseUrlSoloParaSuperAdmin(bool superAdmin, string? expected)
    {
        (await Handle(Command(superAdmin: superAdmin))).CaseUrl.Should().Be(expected);
    }

    // ── AC5 — ambiente con default fail-safe ────────────────────────────────────────────

    [Theory]
    [InlineData(null, DrFlitDeployEnvironment.DEV)]
    [InlineData("", DrFlitDeployEnvironment.DEV)]
    [InlineData("Production", DrFlitDeployEnvironment.DEV)]
    [InlineData("Development", DrFlitDeployEnvironment.DEV)]
    [InlineData("PDN", DrFlitDeployEnvironment.PDN)]
    [InlineData(" qa ", DrFlitDeployEnvironment.QA)]
    [InlineData("3", DrFlitDeployEnvironment.DEV)]
    public void AC5_AmbienteSinConfigurarODesconocido_EsDevNuncaPdn(string? configured, DrFlitDeployEnvironment expected)
    {
        DrFlitSupportCaseWire.ParseDeployEnvironment(configured).Should().Be(expected);
    }

    [Fact]
    public async Task AC5_ElCasoUsaElAmbienteDeLaConfiguracion()
    {
        _settings.DeployEnvironment.Returns(DrFlitDeployEnvironment.PDN);

        await Handle(Command());

        _sentTicket!.Environment.Should().Be(DrFlitDeployEnvironment.PDN);
    }

    // ── Validación: 400 sin tocar BD ni proveedor ──────────────────────────────────────

    public static TheoryData<CreateSupportCaseCommand> InvalidCommands() => new()
    {
        Command(email: null),
        Command(email: "no-es-correo"),
        Command(email: "a@b"),
        Command(prioridad: "Urgente"),
        Command(prioridad: null),
        Command(frecuencia: "nunca"),
        Command(detalle: "   "),
        Command(detalle: new string('x', CreateSupportCaseHandler.MaxDetalle + 1)),
        Command(titulo: new string('x', CreateSupportCaseHandler.MaxTitulo + 1)),
        Command(telefono: new string('1', CreateSupportCaseHandler.MaxTelefono + 1)),
        Command(attachments: [.. Enumerable.Range(0, 6).Select(_ => Guid.NewGuid())]),
    };

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public async Task FormularioInvalido_NoPersisteNiLlamaAlProveedor(CreateSupportCaseCommand command)
    {
        var result = await Handle(command);

        result.Outcome.Should().Be(CreateSupportCaseOutcome.Invalid);
        result.Error.Should().NotBeNullOrWhiteSpace();
        await _repository.DidNotReceiveWithAnyArgs().InsertPendingAsync(default!, TestContext.Current.CancellationToken);
        await _gateway.DidNotReceiveWithAnyArgs().CreateBugAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AdjuntoAjenoOVencido_Invalid()
    {
        _attachments.GetPendingAsync(default, default, default!, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs([new DrFlitPendingAttachment(A1, "a.png", "image/png", "fm-a")]);

        var result = await Handle(Command(attachments: [A1, A2]));

        result.Outcome.Should().Be(CreateSupportCaseOutcome.Invalid);
        result.Error.Should().Contain("ya no están disponibles");
        await _repository.DidNotReceiveWithAnyArgs().InsertPendingAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LosAdjuntosSeBuscanPorTenantUsuarioYFechaActual()
    {
        await Handle(Command(attachments: [A1, A1, A2]));

        await _attachments.Received(1).GetPendingAsync(
            Tenant, User, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { A1, A2 })), Now, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TelefonoVacio_SeEnviaComoNull()
    {
        await Handle(Command(telefono: "  "));

        _sentTicket!.RequesterPhone.Should().BeNull();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
