using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #14559 (ADR-0085) — el veredicto OCR que el wizard calcula en el cliente se declara en
/// <c>POST .../attachments/register</c>, se guarda normalizado en la fila del adjunto y vuelve en
/// <see cref="AttachmentDto"/>. Es informativo: un valor desconocido se descarta (NULL), nunca da error.
/// Uso de ejemplo:
/// <code>
/// var (dto, error) = await new RegisterAttachmentHandler(repo).HandleAsync(
///     id, tenant, new RegisterAttachmentInput("factura", "f.pdf", "application/pdf", 10, "abc", "p",
///         OcrStatus: "rejected", OcrReason: "vin_mismatch"), null, ct);
/// // dto.OcrStatus == "rejected", dto.OcrReason == "vin_mismatch"
/// </code>
/// </summary>
public sealed class RegisterAttachmentOcrVerdictTests
{
    private static readonly System.Text.Json.JsonSerializerOptions WebJson =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly RegisterAttachmentHandler _register;
    private readonly ListAttachmentsHandler _list;
    private readonly Guid _id = Guid.NewGuid();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly ProcedureInstance _instance;

    public RegisterAttachmentOcrVerdictTests()
    {
        _register = new RegisterAttachmentHandler(_repo);
        _list = new ListAttachmentsHandler(_repo);
        _instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = _id,
            TenantId = _tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ChecklistEstado = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithAttachmentsAsync(_id, _tenant, Arg.Any<CancellationToken>()).Returns(_instance);
    }

    private static RegisterAttachmentInput Input(
        string? ocrStatus = null, string? ocrReason = null, string tipo = "factura", string storagePath = "file_xyz") =>
        new(tipo, "doc.pdf", "application/pdf", 1024, "abc123", storagePath, ocrStatus, ocrReason);

    private async Task<AttachmentDto> RegisterOk(RegisterAttachmentInput input)
    {
        var (result, error) = await _register.HandleAsync(
            _id, _tenant, input, null, TestContext.Current.CancellationToken);
        error.Should().BeNull();
        return result!;
    }

    [Fact]
    public async Task Register_ConOcrVerified_PersisteEnLaFilaYDevuelveEnDto()
    {
        var dto = await RegisterOk(Input("verified"));

        var fila = _instance.Attachments.Should().ContainSingle().Subject;
        fila.OcrStatus.Should().Be("verified");
        fila.OcrReason.Should().BeNull();
        dto.OcrStatus.Should().Be("verified");
        dto.OcrReason.Should().BeNull();
    }

    [Fact]
    public async Task Register_ConOcrRejectedVinMismatch_PersisteEnLaFilaYDevuelveEnDto()
    {
        var dto = await RegisterOk(Input("rejected", "vin_mismatch"));

        var fila = _instance.Attachments.Should().ContainSingle().Subject;
        fila.OcrStatus.Should().Be("rejected");
        fila.OcrReason.Should().Be("vin_mismatch");
        dto.OcrStatus.Should().Be("rejected");
        dto.OcrReason.Should().Be("vin_mismatch");
    }

    [Fact]
    public async Task Register_OcrConMayusculasYEspacios_SeNormalizaAntesDeAsignar()
    {
        // El CHECK de BD exige minúsculas: un 'VERIFIED' sin normalizar daría 23514.
        await RegisterOk(Input("  SKIPPED ", " Excede_Tamano  "));

        var fila = _instance.Attachments.Should().ContainSingle().Subject;
        fila.OcrStatus.Should().Be("skipped");
        fila.OcrReason.Should().Be("excede_tamano");
    }

    [Fact]
    public async Task Register_OcrStatusDesconocido_SeGuardaNuloYNoFalla()
    {
        // HU #11996: el OCR nunca impide cargar. Status inválido descarta también el motivo.
        var dto = await RegisterOk(Input("aprobado", "vin_mismatch"));

        var fila = _instance.Attachments.Should().ContainSingle().Subject;
        fila.OcrStatus.Should().BeNull();
        fila.OcrReason.Should().BeNull();
        dto.OcrStatus.Should().BeNull();
    }

    [Fact]
    public async Task Register_ReasonConVerified_SeDescarta()
    {
        await RegisterOk(Input("verified", "vin_mismatch"));

        var fila = _instance.Attachments.Should().ContainSingle().Subject;
        fila.OcrStatus.Should().Be("verified");
        fila.OcrReason.Should().BeNull();
    }

    [Fact]
    public async Task Register_ReasonDesconocido_SeGuardaNuloConservandoElStatus()
    {
        await RegisterOk(Input("rejected", "el VIN 9BW... no cruza"));

        var fila = _instance.Attachments.Should().ContainSingle().Subject;
        fila.OcrStatus.Should().Be("rejected");
        fila.OcrReason.Should().BeNull();
    }

    [Fact]
    public async Task Register_ReemplazoDeTipo_FilaNuevaConVeredictoNuevoSinHeredar()
    {
        await RegisterOk(Input("rejected", "vin_mismatch", storagePath: "p1"));
        var primera = _instance.Attachments.Single();

        await RegisterOk(Input(storagePath: "p2"));

        var fila = _instance.Attachments.Should().ContainSingle().Subject;
        fila.Id.Should().NotBe(primera.Id);
        fila.StoragePath.Should().Be("p2");
        fila.OcrStatus.Should().BeNull();
        fila.OcrReason.Should().BeNull();
    }

    [Fact]
    public async Task Register_SinCamposOcr_DejaNulo_Retrocompatible()
    {
        var input = new RegisterAttachmentInput("factura", "doc.pdf", "application/pdf", 1024, "abc123", "file_xyz");

        var dto = await RegisterOk(input);

        var fila = _instance.Attachments.Should().ContainSingle().Subject;
        fila.OcrStatus.Should().BeNull();
        fila.OcrReason.Should().BeNull();
        dto.OcrStatus.Should().BeNull();
        dto.OcrReason.Should().BeNull();
    }

    [Fact]
    public async Task ListAttachments_ProyectaOcrStatusYReasonDeLaFila()
    {
        _instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant,
            ProcedureInstanceId = _id,
            Tipo = "factura",
            Filename = "f.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = "abc",
            StoragePath = "p",
            Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
            OcrStatus = "rejected",
            OcrReason = "vin_mismatch",
        });

        var (result, error) = await _list.HandleAsync(_id, _tenant, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        var dto = result!.Attachments.Should().ContainSingle().Subject;
        dto.OcrStatus.Should().Be("rejected");
        dto.OcrReason.Should().Be("vin_mismatch");
    }

    [Fact]
    public void AttachmentDto_SerializaOcrEnCamelCase()
    {
        var dto = new AttachmentDto(
            Guid.NewGuid(), "factura", "f.pdf", "application/pdf", 10, "abc", "user", DateTimeOffset.UtcNow,
            OcrStatus: "skipped", OcrReason: "analisis_fallido");

        var json = System.Text.Json.JsonSerializer.Serialize(dto, WebJson);

        json.Should().Contain("\"ocrStatus\":\"skipped\"").And.Contain("\"ocrReason\":\"analisis_fallido\"");
    }
}
