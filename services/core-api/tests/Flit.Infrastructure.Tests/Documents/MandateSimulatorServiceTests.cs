using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Simulador de mandatos (HU #11706). Cubre que el escenario elegido llegue al PDF y que el envío
/// por correo adjunte ese PDF y reporte el desenlace sin filtrar datos técnicos.
/// </summary>
public sealed class MandateSimulatorServiceTests
{
    private static readonly Guid Funza = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Compania = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Mandatario = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PreviewAsync_OrganismoInexistente_NoGeneraNada()
    {
        var (service, _) = Build(conMandatario: false);

        var result = await service.PreviewAsync(
            new MandateSimulationRequest(Guid.NewGuid(), "juridica", null, null), Ct);

        result.Outcome.Should().Be(MandateSimulationOutcome.OfficeNotFound);
        result.Content.Should().BeNull();
    }

    [Theory]
    [InlineData("natural", "traspaso_standard", "pn_traspaso")]
    [InlineData("juridica", "traspaso_standard", "pj_traspaso")]
    [InlineData("natural", "matricula_inicial", "pn_matricula")]
    [InlineData("juridica", "matricula_inicial", "pj_matricula")]
    [InlineData("juridica", "TRASPASO_STANDARD", "pj_traspaso")]
    [InlineData("juridica", "MATRICULA_NUEVA", "pj_matricula")]
    public async Task PreviewAsync_GeneraPdfDelEscenarioPedido(
        string personType, string tipologia, string sufijo)
    {
        var (service, _) = Build(conMandatario: false);

        var result = await service.PreviewAsync(
            new MandateSimulationRequest(Funza, personType, null, null, tipologia), Ct);

        result.Success.Should().BeTrue();
        result.Content.Should().NotBeNullOrEmpty();
        System.Text.Encoding.ASCII.GetString(result.Content!, 0, 4).Should().Be("%PDF");
        result.FileName.Should().Be($"mandato_simulado_25286000_{sufijo}.pdf");
    }

    [Fact]
    public async Task PreviewAsync_SinTipologia_CaeATraspaso()
    {
        var (service, _) = Build(conMandatario: false);

        var result = await service.PreviewAsync(
            new MandateSimulationRequest(Funza, "juridica", null, null), Ct);

        result.FileName.Should().Be("mandato_simulado_25286000_pj_traspaso.pdf");
    }

    [Fact]
    public async Task PreviewAsync_ModoInvalido_NoSimula()
    {
        var (service, _) = Build(conMandatario: false);

        var result = await service.PreviewAsync(
            new MandateSimulationRequest(Funza, "juridica", "modo-que-no-existe", null), Ct);

        result.Outcome.Should().Be(MandateSimulationOutcome.InvalidAssignmentMode);
    }

    [Fact]
    public async Task PreviewAsync_MandatarioAjenoAlOrganismo_NoSimula()
    {
        var (service, _) = Build(conMandatario: true);

        var result = await service.PreviewAsync(
            new MandateSimulationRequest(Funza, "juridica", null, Guid.NewGuid()), Ct);

        result.Outcome.Should().Be(MandateSimulationOutcome.SignerNotFound);
    }

    [Fact]
    public async Task ListSignersAsync_DevuelveSoloLosHabilitadosEnEseOrganismo()
    {
        var (service, _) = Build(conMandatario: true);

        var items = await service.ListSignersAsync(Funza, Ct);

        items.Should().ContainSingle();
        items[0].Id.Should().Be(Mandatario);
        items[0].FullName.Should().Be("Ana Gestora");

        (await service.ListSignersAsync(Guid.NewGuid(), Ct)).Should().BeEmpty();
    }

    // El envío por correo quedó OCULTO en la interfaz (decisión de producto del 2026-08-21). Las
    // pruebas se conservan porque el servicio se conserva: si vuelve a exponerse, debe seguir
    // comportándose así y no hay que redescubrirlo.
    [Fact]
    public async Task SendAsync_SinDestinatarioValido_NiSiquieraIntentaEnviar()
    {
        var (service, sender) = Build(conMandatario: false);

        foreach (var destino in new[] { null, "", "  ", "sin-arroba", "a@b", "a@.com" })
        {
            var result = await service.SendAsync(
                new MandateSimulationSendRequest(Funza, "juridica", null, null, destino, null), Ct);

            result.Outcome.Should().Be(MandateSimulationOutcome.InvalidRecipient);
        }

        sender.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_DestinatarioValido_AdjuntaElPdfSimulado()
    {
        var (service, sender) = Build(conMandatario: true);

        var result = await service.SendAsync(
            new MandateSimulationSendRequest(
                Funza, "natural", null, Mandatario, "cliente@empresa.com", "Cliente"),
            Ct);

        result.Success.Should().BeTrue();
        var mensaje = sender.Enviados.Should().ContainSingle().Subject;
        mensaje.ToEmail.Should().Be("cliente@empresa.com");
        var adjunto = mensaje.Attachments.Should().ContainSingle().Subject;
        adjunto.ContentType.Should().Be("application/pdf");
        System.Text.Encoding.ASCII.GetString(adjunto.Content, 0, 4).Should().Be("%PDF");
        // El cuerpo debe advertir que es una simulación: el PDF va con marcadores.
        mensaje.HtmlBody.Should().Contain("simulación");
    }

    [Fact]
    public async Task SendAsync_TransporteFallido_ReportaElMotivoGenerico()
    {
        var (service, sender) = Build(conMandatario: false);
        sender.Resultado = EmailSendResult.Failed(EmailSendOutcome.RecipientRejected);

        var result = await service.SendAsync(
            new MandateSimulationSendRequest(
                Funza, "juridica", null, null, "cliente@empresa.com", null),
            Ct);

        result.Outcome.Should().Be(MandateSimulationOutcome.SendFailed);
        result.Message.Should().Be("El proveedor de correo rechazó al destinatario.");
        // Nunca host, credenciales ni el mensaje crudo del proveedor.
        result.Message.Should().NotContainAny("smtp", "Host", "password");
    }

    private static (MandateSimulatorService Service, FakeEmailSender Sender) Build(bool conMandatario)
    {
        var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandate-sim-{Guid.NewGuid()}")
            .Options);

        db.TransitOffices.Add(new TransitOffice
        {
            Id = Funza,
            Code = "25286000",
            Name = "STRIA TTOyTTE MCPAL FUNZA",
            DepartmentCode = "25",
            CityCode = "25286",
            IsActive = true,
        });
        db.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
        {
            Id = Guid.NewGuid(),
            TransitOfficeId = Funza,
            TemplateCode = MandatoTemplateResolver.Municipio,
            RequiresForNaturalPerson = true,
            MandataryFamily = MandatoFamiliaCodes.Individuo,
            AssignmentMode = MandatoAssignmentModeCodes.Signer,
            ChamberCity = "Funza",
            CustomTemplateKind = MandatoCustomTemplateKindCodes.None,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        if (conMandatario)
        {
            db.MandateSigners.Add(new MandateSigner
            {
                Id = Mandatario,
                TransitOfficeId = Funza,
                FullName = "Ana Gestora",
                DocumentType = "CC",
                DocumentNumber = "1020304050",
                IntegrityHash = "hash",
                IsActive = true,
                RegisteredAt = DateTimeOffset.UtcNow,
            });
            db.MandateSignerCompanies.Add(new MandateSignerCompany
            {
                Id = Guid.NewGuid(),
                MandateSignerId = Mandatario,
                TransitOfficeId = Funza,
                CompanyTenantId = Compania,
                IsActive = true,
            });
        }

        db.SaveChanges();

        var sender = new FakeEmailSender();
        // HU #11752 (ADR-0050) — MandateSignerDirectory ya no lee admin.admin_identity_validations:
        // resuelve identidad contra el módulo Identidad, vía el tenant del OT (ITransitOfficeOperationalStatusReader)
        // y IdentityVigenciaPorDocumentoResolver. Estas pruebas no ejercitan vigencia de identidad, así
        // que el reader devuelve "sin tenant" (LoadVigentIdentitiesAsync corta antes de consultar
        // Identidad) y el resolver recibe un repositorio nunca invocado.
        var otStatusReader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        otStatusReader
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Flit.Admin.Domain.Companies.TransitOffices.TransitOfficeOperationalStatusItem?)null);
        var identityResolver = new IdentityVigenciaPorDocumentoResolver(Substitute.For<IProcedureInstanceRepository>());
        var service = new MandateSimulatorService(
            db,
            new FakeCatalog(),
            new MandateRequirementPolicy(db),
            new MandateSignerDirectory(db, otStatusReader, identityResolver),
            new MandatoPdfGenerator(),
            new FakeConfigService(),
            NullSignatureVaultPolicy.Instance,
            new FakeStorage(),
            sender);

        return (service, sender);
    }

    private sealed class FakeCatalog : ITransitOfficeCatalog
    {
        private static readonly TransitOfficeEntry Entry =
            new(Funza, "25286000", "STRIA TTOyTTE MCPAL FUNZA", "25", "25286");

        public IReadOnlyList<TransitOfficeEntry> All => [Entry];

        public IReadOnlyList<TransitOfficeEntry> Search(string? term) => All;

        public bool Exists(Guid id) => id == Funza;

        public TransitOfficeEntry? GetById(Guid id) => id == Funza ? Entry : null;
    }

    /// <summary>Sin plantilla propia: el simulador nunca pide bytes custom en estas pruebas.</summary>
    private sealed class FakeConfigService : IMandateConfigAdminService
    {
        public Task<IReadOnlyList<MandateOtConfigView>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MandateOtConfigView>>([]);

        public Task<MandateOtConfigView?> GetAsync(Guid officeId, CancellationToken ct = default) =>
            Task.FromResult<MandateOtConfigView?>(null);

        public Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> UpsertAsync(
            Guid officeId, UpsertMandateOtConfigRequest request, Guid? userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MandateConfigWriteStatus> DeleteAsync(Guid officeId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MandateConfigExtractResult> ExtractAsync(
            ReadOnlyMemory<byte> content, string mediaType, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> UploadPdfTemplateAsync(
            Guid officeId, Stream content, string fileName, Guid? userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> SaveEditorBodyAsync(
            Guid officeId, SaveMandateEditorBodyRequest request, Guid? userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> DeleteCustomTemplateAsync(
            Guid officeId, Guid? userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<byte[]?> OpenCustomPdfAsync(Guid officeId, CancellationToken ct = default) =>
            Task.FromResult<byte[]?>(null);

        public Task<IReadOnlyList<CompanyOtMandateRuleView>> ListCompanyRulesAsync(
            Guid officeId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CompanyOtMandateRuleView>>([]);

        public Task<(MandateConfigWriteStatus Status, CompanyOtMandateRuleView? View)> UpsertCompanyRuleAsync(
            Guid officeId, Guid companyTenantId, UpsertCompanyOtMandateRuleRequest request,
            Guid? userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MandateConfigWriteStatus> DeleteCompanyRuleAsync(
            Guid officeId, Guid companyTenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename,
            CancellationToken ct = default) => throw new NotSupportedException();

        public void Delete(string storagePath) { }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string, DateTimeOffset)?>(null);
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> Enviados { get; } = [];

        public EmailSendResult Resultado { get; set; } = EmailSendResult.Sent;

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (Resultado.Success)
                Enviados.Add(message);
            return Task.FromResult(Resultado);
        }
    }
}
