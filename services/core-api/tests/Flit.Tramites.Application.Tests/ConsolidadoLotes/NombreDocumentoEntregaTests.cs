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

    // ── Épica #14657 — escrituras: documento real, no se renombra ───────────────────────────────

    [Theory]
    [InlineData("escritura")]
    [InlineData("escritura_comprador")]
    [InlineData("ESCRITURA")]
    public void EscrituraDeSistema_ConservaSuNombre(string tipo)
    {
        var i = Instancia("AAA111");
        var a = Adjunto(i, tipo, "escritura_publica_123.pdf", source: "system");

        NombreDocumentoEntrega.EsGeneradoPorFlit(a).Should().BeFalse();
        NombreDocumentoEntrega.ParaAdjunto(a, i).Should().Be("escritura_publica_123.pdf");
    }

    // ── Épica #14657 — rues/impronta guardados vía UploadAttachmentHandler (Source=user) ────────

    [Theory]
    [InlineData("rues", "rues_FT1-0000102.pdf", "rues_FT1-0000102_AAA111.pdf")]
    [InlineData("impronta", "impronta_FT1-0000102.pdf", "impronta_FT1-0000102_AAA111.pdf")]
    [InlineData("rues", "rues_FT1-0000102_XXX999.pdf", "rues_FT1-0000102_AAA111.pdf")]
    public void RuesEImprontaYaGuardados_ConPatronDelGenerador_SeRenombran_AunqueSeanSourceUser(
        string tipo, string guardado, string esperado)
    {
        var i = Instancia("AAA111");
        var a = Adjunto(i, tipo, guardado, source: "user");

        NombreDocumentoEntrega.ParaAdjunto(a, i).Should().Be(esperado);
    }

    [Theory]
    [InlineData("rues", "certificado_de_camara.pdf")]
    [InlineData("impronta", "scan_huella.pdf")]
    [InlineData("rues", "RUES.pdf")]
    public void RuesEImpronta_SubidosPorElUsuarioConOtroNombre_NoSeRenombran(string tipo, string nombre)
    {
        var i = Instancia("AAA111");
        var a = Adjunto(i, tipo, nombre, source: "user");

        NombreDocumentoEntrega.ParaAdjunto(a, i).Should().Be(nombre);
    }

    [Fact]
    public void OtroTipoSourceUser_ConPrefijoSimilar_NoSeRenombra_NiRuesNoPdf()
    {
        var i = Instancia("AAA111");
        var otro = Adjunto(i, "cedula", "cedula_FT1-0000102.pdf", source: "user");
        var imagen = Adjunto(i, "rues", "rues_foto.png", source: "user", mime: "image/png");

        NombreDocumentoEntrega.ParaAdjunto(otro, i).Should().Be("cedula_FT1-0000102.pdf");
        NombreDocumentoEntrega.ParaAdjunto(imagen, i).Should().Be("rues_foto.png");
    }

    // ── Épica #14657 — históricos y duplicados del mismo tipo ───────────────────────────────────

    [Fact]
    public void Historico_ConservaSuNombre_YSoloElVigenteSeCalcula_SinDosNombresIguales()
    {
        var i = Instancia("AAA111");
        var historico = Adjunto(i, "fur", "fur_revocado_v1.pdf");
        historico.IsHistorico = true;
        historico.UploadedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var vigente = Adjunto(i, "fur", "fur_v2.pdf");

        NombreDocumentoEntrega.ParaAdjunto(historico, i).Should().Be("fur_revocado_v1.pdf");
        NombreDocumentoEntrega.ParaAdjunto(vigente, i).Should().Be("fur_FT1-0000102_AAA111.pdf");
    }

    [Fact]
    public void DosNoHistoricosDelMismoTipo_SoloElMasRecienteSeCalcula()
    {
        var i = Instancia("AAA111");
        var viejo = Adjunto(i, "consolidado", "consolidado_a.pdf");
        viejo.UploadedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var nuevo = Adjunto(i, "consolidado", "consolidado_b.pdf");

        NombreDocumentoEntrega.ParaAdjunto(viejo, i).Should().Be("consolidado_a.pdf");
        NombreDocumentoEntrega.ParaAdjunto(nuevo, i).Should().Be("consolidado_FT1-0000102_AAA111.pdf");
    }

    [Fact]
    public async Task Listado_ConHistoricoYVigenteDelMismoTipo_NoMuestraDosNombresIguales()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Instancia("AAA111");
        var historico = Adjunto(i, "fur", "fur_anterior.pdf");
        historico.IsHistorico = true;
        historico.UploadedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var vigente = Adjunto(i, "fur", "fur_actual.pdf");
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithAttachmentsAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>()).Returns(i);

        var (lista, _) = await new ListAttachmentsHandler(repo).HandleAsync(i.Id, i.TenantId, ct);

        lista!.Attachments.Single(d => d.Id == historico.Id).Filename.Should().Be("fur_anterior.pdf");
        lista.Attachments.Single(d => d.Id == vigente.Id).Filename.Should().Be("fur_FT1-0000102_AAA111.pdf");
        lista.Attachments.Select(d => d.Filename).Should().OnlyHaveUniqueItems();
    }

    // ── Épica #14657 — placa: una sola fuente ───────────────────────────────────────────────────

    [Fact]
    public void Placa_SinPlateConFieldValue_UsaElFieldValue_IgualQueNombreDocumentoTramite()
    {
        var i = Instancia(null);
        i.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ddd444" });
        var a = Adjunto(i, "fur", "viejo.pdf");

        NombreDocumentoEntrega.ParaAdjunto(a, i).Should().Be("fur_FT1-0000102_DDD444.pdf")
            .And.Be(NombreDocumentoTramite.Generar("fur", i));
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
