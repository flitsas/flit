using System.Text;
using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.SupportCases;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.DrFlit.Application.Tests.SupportCases;

/// <summary>
/// HU #12924 — subida previa de adjuntos de un caso de soporte.
/// Uso de ejemplo:
/// <code>
/// var r = await handler.HandleAsync(new UploadSupportAttachmentCommand(tenant, user, "captura.png", "image/png", len, stream), ct);
/// // r.Outcome == Uploaded ⇒ r.Attachment.Id se envía luego en attachmentIds
/// </code>
/// </summary>
public sealed class UploadSupportAttachmentHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("0199a000-0000-7000-8000-0000000000aa");
    private static readonly Guid User = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 15, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2];
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n...");
    private static readonly byte[] Webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, 1];
    private static readonly byte[] Text = Encoding.UTF8.GetBytes("pasos para reproducir");
    private static readonly byte[] Exe = [0x4D, 0x5A, 0x90, 0x00];

    private readonly IDrFlitSupportAttachmentStore _store = Substitute.For<IDrFlitSupportAttachmentStore>();
    private readonly IDrFlitSupportCaseSettings _settings = Substitute.For<IDrFlitSupportCaseSettings>();
    private readonly FixedClock _clock = new(Now);

    public UploadSupportAttachmentHandlerTests()
    {
        _settings.MaxAttachments.Returns(5);
        _settings.MaxFileSizeBytes.Returns(1024);
        _settings.AllowedMimeTypes.Returns(["image/png", "image/jpeg", "image/webp", "application/pdf", "text/plain"]);
        _settings.AttachmentTtl.Returns(TimeSpan.FromHours(24));
        _store.SaveAsync(default, default, default!, default!, default!, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(ci => new DrFlitStoredAttachment(Guid.Parse("0199a000-0000-7000-8000-00000000f001"), ci.ArgAt<string>(2), ci.ArgAt<Stream>(4).Length));
    }

    private UploadSupportAttachmentHandler Handler() => new(_store, _settings, _clock);

    private Task<UploadSupportAttachmentResult> Upload(string? name, byte[]? bytes, string? declared = null, long? length = null) =>
        Handler().HandleAsync(
            new UploadSupportAttachmentCommand(Tenant, User, name, declared, length ?? bytes?.Length ?? 0, bytes is null ? null : new MemoryStream(bytes)),
            TestContext.Current.CancellationToken);

    // ── AC1 — tipo permitido dentro del tamaño ──────────────────────────────────────────

    [Theory]
    [InlineData("captura.png", "image/png")]
    [InlineData("foto.JPG", "image/jpeg")]
    [InlineData("foto.jpeg", "image/jpeg")]
    [InlineData("pantalla.webp", "image/webp")]
    [InlineData("soporte.pdf", "application/pdf")]
    [InlineData("pasos.txt", "text/plain")]
    public async Task AC1_TipoPermitido_SeGuardaConVencimientoDe24h(string name, string mime)
    {
        var bytes = mime switch
        {
            "image/png" => Png,
            "image/jpeg" => Jpeg,
            "image/webp" => Webp,
            "application/pdf" => Pdf,
            _ => Text,
        };

        var result = await Upload(name, bytes, mime);

        result.Outcome.Should().Be(UploadSupportAttachmentOutcome.Uploaded);
        result.Attachment!.FileName.Should().Be(name);
        result.Attachment.SizeBytes.Should().Be(bytes.Length);
        await _store.Received(1).SaveAsync(
            Tenant, User, name, mime, Arg.Any<Stream>(), Now.AddHours(24), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC1_PurgaLosVencidosDelUsuarioAntesDeGuardar()
    {
        await Upload("captura.png", Png, "image/png");

        await _store.Received(1).PurgeExpiredAsync(Tenant, User, Now, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC1_NombreConRuta_SeGuardaSoloElNombre()
    {
        var result = await Upload(@"..\..\C:\temp\captura.png", Png, "image/png");

        result.Attachment!.FileName.Should().Be("captura.png");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/octet-stream")]
    [InlineData("image/png; charset=binary")]
    public async Task AC1_TipoDeclaradoAusenteOGenerico_SeAceptaPorLaExtension(string? declared)
    {
        (await Upload("captura.png", Png, declared)).Outcome.Should().Be(UploadSupportAttachmentOutcome.Uploaded);
    }

    // ── AC2 — tipo no permitido, tamaño excedido o sin archivo: 400 sin persistir ───────

    public static TheoryData<string?, byte[]?, string?, long?, UploadSupportAttachmentOutcome> Invalid() => new()
    {
        { null, Png, "image/png", null, UploadSupportAttachmentOutcome.MissingFile },
        { "captura.png", null, "image/png", 0, UploadSupportAttachmentOutcome.MissingFile },
        { "vacio.png", [], "image/png", 1, UploadSupportAttachmentOutcome.MissingFile },
        { "programa.exe", Exe, "application/octet-stream", null, UploadSupportAttachmentOutcome.InvalidType },
        { "sin-extension", Png, "image/png", null, UploadSupportAttachmentOutcome.InvalidType },
        { "doc.docx", Pdf, null, null, UploadSupportAttachmentOutcome.InvalidType },
        { "captura.png", Png, "application/pdf", null, UploadSupportAttachmentOutcome.InvalidType },
        { "disfrazado.pdf", Exe, "application/pdf", null, UploadSupportAttachmentOutcome.InvalidType },
        { "texto.txt", [0x61, 0x00, 0x62], "text/plain", null, UploadSupportAttachmentOutcome.InvalidType },
        { "grande.png", Png, "image/png", 2048, UploadSupportAttachmentOutcome.TooLarge },
        { "miente.png", [.. Png, .. new byte[2000]], "image/png", 10, UploadSupportAttachmentOutcome.TooLarge },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task AC2_ArchivoInvalido_NoSeGuarda(string? name, byte[]? bytes, string? declared, long? length, UploadSupportAttachmentOutcome expected)
    {
        var result = await Upload(name, bytes, declared, length);

        result.Outcome.Should().Be(expected);
        result.Error.Should().NotBeNullOrWhiteSpace();
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default!, default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC2_LosTiposPermitidosSalenDeLaConfiguracion()
    {
        _settings.AllowedMimeTypes.Returns(["image/png"]);

        (await Upload("soporte.pdf", Pdf, "application/pdf")).Outcome.Should().Be(UploadSupportAttachmentOutcome.InvalidType);
        (await Upload("captura.png", Png, "image/png")).Outcome.Should().Be(UploadSupportAttachmentOutcome.Uploaded);
    }

    [Fact]
    public async Task AC2_ElTamanoMaximoSaleDeLaConfiguracion()
    {
        _settings.MaxFileSizeBytes.Returns(5);

        (await Upload("captura.png", Png, "image/png")).Outcome.Should().Be(UploadSupportAttachmentOutcome.TooLarge);
    }

    // ── AC3 — la cantidad no se limita en la subida individual ─────────────────────────

    [Fact]
    public async Task AC3_LaSubidaIndividualNoConsultaCuantosAdjuntosLleva()
    {
        _settings.MaxAttachments.Returns(1);

        for (var i = 0; i < 3; i++)
            (await Upload($"c{i}.png", Png, "image/png")).Outcome.Should().Be(UploadSupportAttachmentOutcome.Uploaded);

        await _store.DidNotReceiveWithAnyArgs().GetPendingAsync(default, default, default!, default, TestContext.Current.CancellationToken);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
