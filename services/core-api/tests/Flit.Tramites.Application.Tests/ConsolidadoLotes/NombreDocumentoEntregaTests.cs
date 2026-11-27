using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #14736 (Feature #14733, Épica #14657) — el nombre de un documento generado por FLIT se calcula al
/// ENTREGAR con la placa actual; lo subido por el usuario conserva el suyo.
/// </summary>
public sealed class NombreDocumentoEntregaTests
{
    private static ProcedureInstance Instancia(string? placa)
    {
        var tenant = Guid.NewGuid();
        return new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FT1-0000102",
            Status = TramiteEstado.Entregado,
            Plate = placa,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ProcedureInstanceAttachment Adjunto(
        ProcedureInstance i, string tipo, string filename, string source = "system", string mime = "application/pdf")
    {
        var a = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            ProcedureInstanceId = i.Id,
            Tipo = tipo,
            Filename = filename,
            Mimetype = mime,
            Source = source,
            StoragePath = $"{i.Id:D}/{tipo}",
            Sha256 = "sha",
            UploadedAt = DateTimeOffset.UtcNow,
        };
        i.Attachments.Add(a);
        return a;
    }

    // ── Helper ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Generado_ConNombreViejo_ConPlacaActual_TomaElNombreNuevo()
    {
        var i = Instancia("aaa111");
        var a = Adjunto(i, "fur", "fur_antiguo.pdf");

        NombreDocumentoEntrega.ParaAdjunto(a, i).Should().Be("fur_FT1-0000102_AAA111.pdf");
    }

    [Fact]
    public void Generado_SinPlaca_UsaSinPlaca_YLaPlacaQueLlegaDespuesYaSeUsa()
    {
        var i = Instancia(null);
        var a = Adjunto(i, "consolidado", "consolidado.pdf");

        NombreDocumentoEntrega.ParaAdjunto(a, i).Should().Be("consolidado_FT1-0000102_SIN-PLACA.pdf");

        i.Plate = "BBB222";
        NombreDocumentoEntrega.ParaAdjunto(a, i).Should().Be("consolidado_FT1-0000102_BBB222.pdf");
    }

    [Theory]
    [InlineData("user")]
    [InlineData("ot")]
    [InlineData("ict")]
    [InlineData("company")]
    public void SubidoPorOtroOrigen_ConservaSuNombre(string source)
    {
        var i = Instancia("AAA111");
        var a = Adjunto(i, "cedula", "mi_cedula.pdf", source);

        NombreDocumentoEntrega.ParaAdjunto(a, i).Should().Be("mi_cedula.pdf");
    }

    [Fact]
    public void GeneradoPeroNoPdf_ConservaSuNombre()
    {
        var i = Instancia("AAA111");
        var a = Adjunto(i, "impronta", "impronta.png", mime: "image/png");

        NombreDocumentoEntrega.ParaAdjunto(a, i).Should().Be("impronta.png");
    }

    [Fact]
    public void ParaDocumento_ConDtoSinAdjuntoCargado_LoDevuelveIntacto()
    {
        var i = Instancia("AAA111");
        var dto = new ConsolidadoDocumentDto(Guid.NewGuid(), "consolidado", "x.pdf", "sha");

        NombreDocumentoEntrega.ParaDocumento(dto, i).Should().Be(dto);
    }

    // ── Perfil gestor/empresa: listado y descarga de adjuntos ───────────────────────────────────

    [Fact]
    public async Task Empresa_Listado_YDescarga_UsanElNombreCalculado_PeroLoSubidoNo()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Instancia("AAA111");
        var generado = Adjunto(i, "fur", "viejo.pdf");
        var subido = Adjunto(i, "factura", "factura_cliente.pdf", "user");
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithAttachmentsAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>()).Returns(i);
        var storage = Substitute.For<IAttachmentStorage>();
        storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream([1])));

        var (lista, _) = await new ListAttachmentsHandler(repo).HandleAsync(i.Id, i.TenantId, ct);
        lista!.Attachments.Single(d => d.Id == generado.Id).Filename.Should().Be("fur_FT1-0000102_AAA111.pdf");
        lista.Attachments.Single(d => d.Id == subido.Id).Filename.Should().Be("factura_cliente.pdf");

        var (descarga, _) = await new DownloadAttachmentHandler(repo, storage).HandleAsync(i.Id, i.TenantId, generado.Id, ct);
        descarga!.Filename.Should().Be("fur_FT1-0000102_AAA111.pdf");
        var (descargaSubido, _) = await new DownloadAttachmentHandler(repo, storage).HandleAsync(i.Id, i.TenantId, subido.Id, ct);
        descargaSubido!.Filename.Should().Be("factura_cliente.pdf");
        generado.Filename.Should().Be("viejo.pdf", "no se reescribe la base");
    }

    // ── Perfil OT: consolidado de «Ver consolidado» y entrega ───────────────────────────────────

    [Fact]
    public async Task OT_VerConsolidado_Descarga_UsaElNombreCalculado()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Instancia("AAA111");
        Adjunto(i, "consolidado_maestro", "maestro_viejo.pdf");
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithAttachmentsAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>()).Returns(i);
        var storage = Substitute.For<IAttachmentStorage>();
        storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream([1])));

        var (descarga, error) = await new DescargarConsolidadoHandler(repo, storage).HandleAsync(i.Id, i.TenantId, ct);

        error.Should().BeNull();
        descarga!.Filename.Should().Be("consolidado_maestro_FT1-0000102_AAA111.pdf");
    }

    [Theory]
    [InlineData(ConsolidadoEntregaTipo.Wizard, "consolidado")]
    [InlineData(ConsolidadoEntregaTipo.Maestro, "consolidado_maestro")]
    public async Task EntregaConsolidado_SoloLectura_TraeElNombreCalculado_AunqueSeGuardoConElViejo(
        ConsolidadoEntregaTipo tipo, string tipoAdjunto)
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Instancia("AAA111");
        Adjunto(i, tipoAdjunto, "nombre_viejo.pdf");
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithAttachmentsAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>()).Returns(i);
        var handler = new EntregarConsolidadoHandler(
            repo,
            new GenerarConsolidadoHandler(repo, Substitute.For<IExpedienteConsolidadoMerger>(), Substitute.For<IAttachmentStorage>()),
            new GenerarConsolidadoMaestroHandler(repo, Substitute.For<IExpedienteConsolidadoMerger>(), Substitute.For<IAttachmentStorage>()));

        var (result, error) = await handler.HandleAsync(
            new EntregarConsolidadoRequest(i.Id, i.TenantId, tipo, SoloLectura: true), ct);

        error.Should().BeNull();
        result!.Document.Filename.Should().Be($"{tipoAdjunto}_FT1-0000102_AAA111.pdf");
    }

    [Fact]
    public async Task EntregaConsolidado_CargadoPorUsuario_ConservaSuNombre()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Instancia("AAA111");
        Adjunto(i, "consolidado", "mio_del_superadmin.pdf", source: "user");
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithAttachmentsAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>()).Returns(i);
        var handler = new EntregarConsolidadoHandler(
            repo,
            new GenerarConsolidadoHandler(repo, Substitute.For<IExpedienteConsolidadoMerger>(), Substitute.For<IAttachmentStorage>()),
            new GenerarConsolidadoMaestroHandler(repo, Substitute.For<IExpedienteConsolidadoMerger>(), Substitute.For<IAttachmentStorage>()));

        var (result, _) = await handler.HandleAsync(
            new EntregarConsolidadoRequest(i.Id, i.TenantId, ConsolidadoEntregaTipo.Wizard, SoloLectura: true), ct);

        result!.Document.Filename.Should().Be("mio_del_superadmin.pdf");
    }
}
