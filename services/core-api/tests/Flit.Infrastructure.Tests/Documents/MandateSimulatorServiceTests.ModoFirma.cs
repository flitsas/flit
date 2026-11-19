using System.Text;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #14358 AC8 — el simulador y el trámite dan el mismo modo de firma para la misma configuración. El mismo
/// escenario recorre los dos caminos reales de generación y se comparan los <see cref="MandatoData"/> resultantes.
/// </summary>
public sealed partial class MandateSimulatorServiceTests
{
    [Theory]
    [InlineData(MandatoAssignmentModeCodes.Signer, true, MandatarioFirmaModo.Estampada)]
    [InlineData(MandatoAssignmentModeCodes.Signer, false, MandatarioFirmaModo.Manual)]
    [InlineData(MandatoAssignmentModeCodes.Open, true, MandatarioFirmaModo.Manual)]
    [InlineData(MandatoAssignmentModeCodes.Institutional, true, MandatarioFirmaModo.SinBloque)]
    [InlineData(null, true, MandatarioFirmaModo.Estampada)]
    public async Task AC8_ElSimuladorYElTramite_DanElMismoModoDeFirma(
        string? tipo, bool conEstampa, MandatarioFirmaModo esperado)
    {
        var candidato = new MandateSignerCandidate(
            Mandatario, "Ana Gestora", "1020304050", null, IdentityVigente: conEstampa, TipoDocumento: "CC");
        var directorio = new DirectorioFijo(candidato);

        var capturing = new CapturingGenerator();
        var (service, _) = Build(conMandatario: true, generator: capturing, directory: directorio);
        var simulado = await service.PreviewAsync(
            new MandateSimulationRequest(Funza, "natural", tipo, Mandatario, "traspaso_standard"), Ct);

        simulado.Success.Should().BeTrue();
        capturing.Captured!.ModoFirmaMandatario.Should().Be(esperado);
        (await ModoEnElTramiteAsync(tipo, directorio)).Should().Be(capturing.Captured.ModoFirmaMandatario);
    }

    private static async Task<MandatarioFirmaModo> ModoEnElTramiteAsync(string? tipo, IMandateSignerDirectory directorio) =>
        (await MandatoEnElTramiteAsync(tipo, directorio)).ModoFirmaMandatario;

    private static async Task<MandatoData> MandatoEnElTramiteAsync(string? tipo, IMandateSignerDirectory directorio)
    {
        var instanceId = Guid.NewGuid();
        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveByOfficeIdAsync(Funza, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(
                Funza,
                MandatoTemplateResolver.Municipio,
                RequiresForNaturalPerson: true,
                InstitutionalMandataryName: null,
                InstitutionalMandataryNit: null,
                AssignmentMode: tipo,
                OtDefaultMandateSignerId: Mandatario));

        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithFurGraphAsync(instanceId, Compania, Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstance
            {
                ProcedureType = ProcedureTypeFixture.Traspaso,
                Id = instanceId,
                TenantId = Compania,
                ProcedureTypeId = ProcedureTypeFixture.Traspaso.Id,
                ReferenceNumber = "TRM-2026-000778",
                Status = TramiteEstado.Borrador,
                TransitOfficeId = Funza,
                CreatedAt = DateTimeOffset.UtcNow,
                FieldValues =
                {
                    new ProcedureInstanceFieldValue
                    {
                        Id = Guid.NewGuid(),
                        TenantId = Compania,
                        ProcedureInstanceId = instanceId,
                        FieldKey = "transit_office_code",
                        ValueText = "25286000",
                        Source = "user",
                    },
                },
            });

        var generador = new SoloCaptura();
        var handler = new GenerarFurHandler(
            repo,
            new MockFurDocumentGenerator(),
            Substitute.For<IKyverumCertificateClient>(),
            Substitute.For<IRuesCertificateGenerator>(),
            Substitute.For<IRnmcCertificateGenerator>(),
            Substitute.For<IProcedureInstancePrendaRepository>(),
            new StorageDelTramite(),
            NullLogger<GenerarFurHandler>.Instance,
            mandatoGenerator: generador,
            mandatePolicy: policy,
            mandateDirectory: directorio);

        var (_, error) = await handler.HandleAsync(instanceId, Compania, Ct);

        error.Should().BeNull();
        return generador.Captured!;
    }

    private sealed class DirectorioFijo(MandateSignerCandidate candidato) : IMandateSignerDirectory
    {
        public Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
            Guid transitOfficeId, Guid companyTenantId, string? nitMandante = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MandateSignerCandidate>>([candidato]);

        public Task<MandateSignerCandidate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(id == candidato.Id ? candidato : null);
    }

    private sealed class SoloCaptura : IMandatoGenerator
    {
        public MandatoData? Captured { get; private set; }

        public GeneratedDocument GenerateMandato(MandatoData data)
        {
            Captured = data;
            return new GeneratedDocument(
                "mandato", "mandato.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF MANDATO"));
        }
    }

    private sealed class StorageDelTramite : IAttachmentStorage
    {
        public Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content,
            CancellationToken ct = default) =>
            Task.FromResult(new StoredFile($"{procedureInstanceId:D}/{tipo}", $"sha-{tipo}", 10));

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath)
        {
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }
}
