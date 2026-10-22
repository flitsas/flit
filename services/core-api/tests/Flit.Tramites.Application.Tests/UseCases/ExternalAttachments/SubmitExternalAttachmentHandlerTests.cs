using System.Security.Cryptography;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ExternalAttachments;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ExternalAttachments;

/// <summary>
/// HU #13263 (Feature #13261, Épica #12741) — reglas del envío de adjuntos del cliente externo, con el repositorio, el
/// almacenamiento y la matriz documental como dobles. El bloqueo de fila y el SQL reales se prueban contra Postgres en
/// <c>Flit.Integration.Tests</c>; el contrato HTTP, en <c>Flit.Admin.Tests</c>.
/// </summary>
public sealed class SubmitExternalAttachmentHandlerTests
{
    private static readonly Guid Tramite = Guid.CreateVersion7();
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly byte[] Pdf = "%PDF-1.4 comprobante sintetico"u8.ToArray();
    private static readonly byte[] OtroPdf = "%PDF-1.4 otro comprobante sintetico"u8.ToArray();

    private readonly FakeRepository _repo = new();
    private readonly FakeStorage _storage = new();
    private readonly FakeMatrix _matrix = new();

    private SubmitExternalAttachmentHandler Handler() => new(_repo, _storage, _matrix);

    private static ExternalAttachmentFile Archivo(byte[]? bytes = null, string mime = "application/pdf") =>
        new("recibo.pdf", mime, bytes ?? Pdf);

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    [Fact]
    public async Task AC1_TramiteAsignadoSinAdjuntoArchivaYDevuelveElRecibo()
    {
        _repo.Target = Target("asignado");
        _matrix.Codigos = ["liquidacion_impuesto"];

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Created);
        var receipt = result.Receipt!;
        receipt.Tipo.Should().Be("liquidacion_impuesto");
        receipt.Sha256.Should().Be(Sha(Pdf));
        receipt.ReemplazoDe.Should().BeNull();
        receipt.EnMatriz.Should().BeTrue();
        receipt.PagadoMarcado.Should().BeTrue("en asignado el comprobante marca el impuesto como pagado");
        _repo.Marcas.Should().Be(1);
        _repo.Escritos.Should().ContainSingle().Which.Should().Match<NewExternalAttachment>(a =>
            a.Tipo == "liquidacion_impuesto" && a.Mimetype == "application/pdf" && a.Sha256 == Sha(Pdf) && a.StoragePath == "fm-1");
        _repo.Escritos[0].SizeBytes.Should().Be(Pdf.Length);
        receipt.AdjuntoId.Should().Be(_repo.IdsEmitidos.Single());
        _storage.Guardados.Should().ContainSingle();
        _storage.Borrados.Should().BeEmpty();
    }

    [Fact]
    public async Task HU13265_SiElMotorRechazaPorCarreraConElGestor_409AttachmentExists_YRetiraElBinarioNuevo()
    {
        _repo.Target = Target("asignado");
        _repo.FalloAlReemplazar = new AttachmentFirstWinsConflictException(new InvalidOperationException("23505"));

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Rejected);
        result.Error.Should().Be("attachment_exists");
        _repo.Marcas.Should().Be(0, "la marca de pago solo se escribe si el adjunto se archivó");
        _storage.Borrados.Should().ContainSingle().Which.Should().Be("fm-1");
    }

    [Fact]
    public async Task AC1_EnMatrizEsFalsoSiElTipoNoEstaEnLaMatrizOSoloComoGeneradoPorElSistema()
    {
        _repo.Target = Target("entregado");
        _matrix.Codigos = ["cedulas"];
        (await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken))
            .Receipt!.EnMatriz.Should().BeFalse();

        _matrix.Codigos = [];
        _matrix.GeneradosPorElSistema = ["liquidacion_impuesto"];
        _repo.Target = Target("entregado");
        (await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(OtroPdf), TestContext.Current.CancellationToken))
            .Receipt!.EnMatriz.Should().BeFalse("una casilla generada por el sistema no la completa el consumidor");
    }

    [Theory]
    [InlineData("preasignacion", false)]
    [InlineData("asignado", false)]
    [InlineData("rechazado", true)]
    public async Task AC1_EnLosEstadosEditablesElComprobanteMarcaElImpuestoComoPagado(string estado, bool subsanacion)
    {
        _repo.Target = Target(estado, subsanacion);

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Created);
        result.Receipt!.PagadoMarcado.Should().BeTrue();
        _repo.Marcas.Should().Be(1);
        _repo.Eventos.Should().Equal("reemplazar", "marcar", "confirmar");
    }

    [Fact]
    public async Task AC1_ElReemplazoTambienMarca()
    {
        var anterior = new ExternalAttachmentExisting(Guid.CreateVersion7(), "flito", Sha(Pdf), "fm-viejo");
        _repo.Target = Target("asignado") with { Vigentes = [anterior], PagadoMarcado = false };

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(OtroPdf), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Created);
        result.Receipt!.ReemplazoDe.Should().Be(anterior.Id);
        result.Receipt.PagadoMarcado.Should().BeTrue();
        _repo.Marcas.Should().Be(1);
    }

    [Fact]
    public async Task AC2_EnEntregadoSeArchivaSinMarcarYLaRespuestaDiceFalseSiNoHabiaMarcaPrevia()
    {
        _repo.Target = Target("entregado");

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Created);
        result.Receipt!.PagadoMarcado.Should().BeFalse();
        _repo.Escritos.Should().ContainSingle("el adjunto se archiva");
        _repo.Marcas.Should().Be(0);
    }

    [Fact]
    public async Task AC2_EnEntregadoConMarcaPreviaDeFlitoLaRespuestaDiceTrueSinEscribir()
    {
        _repo.Target = Target("entregado") with { PagadoMarcado = true };

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Receipt!.PagadoMarcado.Should().BeTrue("la marca ya existía y se lee, no se escribe");
        _repo.Marcas.Should().Be(0);
    }

    [Theory]
    [InlineData("preasignacion", false)]
    [InlineData("asignado", false)]
    [InlineData("rechazado", true)]
    public async Task ElIdempotente200PoneLaMarcaFaltanteSinTocarElAdjunto(string estado, bool subsanacion)
    {
        var vigente = new ExternalAttachmentExisting(Guid.CreateVersion7(), "flito", Sha(Pdf), "fm-1");
        _repo.Target = Target(estado, subsanacion) with { Vigentes = [vigente], PagadoMarcado = false };

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Unchanged);
        result.Receipt!.PagadoMarcado.Should().BeTrue();
        result.Receipt.AdjuntoId.Should().Be(vigente.Id);
        result.Receipt.ReemplazoDe.Should().BeNull();
        _repo.Marcas.Should().Be(1);
        _repo.Escritos.Should().BeEmpty("el adjunto no se toca");
        _repo.Retirados.Should().BeEmpty();
        _storage.Guardados.Should().BeEmpty();
    }

    [Fact]
    public async Task ElIdempotente200EnEntregadoNoEscribeNada()
    {
        var vigente = new ExternalAttachmentExisting(Guid.CreateVersion7(), "flito", Sha(Pdf), "fm-1");
        _repo.Target = Target("entregado") with { Vigentes = [vigente], PagadoMarcado = false };

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Unchanged);
        result.Receipt!.PagadoMarcado.Should().BeFalse();
        _repo.Marcas.Should().Be(0);
    }

    [Fact]
    public async Task ElIdempotente200NoReescribeUnaMarcaYaVigente()
    {
        var vigente = new ExternalAttachmentExisting(Guid.CreateVersion7(), "flito", Sha(Pdf), "fm-1");
        _repo.Target = Target("asignado") with { Vigentes = [vigente], PagadoMarcado = true };

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Unchanged);
        result.Receipt!.PagadoMarcado.Should().BeTrue();
        _repo.Marcas.Should().Be(0);
    }

    [Fact]
    public async Task UnRechazoNoMarcaNada()
    {
        _repo.Target = Target("asignado") with
        {
            Vigentes = [new ExternalAttachmentExisting(Guid.CreateVersion7(), null, Sha(Pdf), "fm-gestor")],
        };

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Error.Should().Be("attachment_exists");
        _repo.Marcas.Should().Be(0);
    }

    [Theory]
    [InlineData("impuesto_departamental_pagado", "flito", true)]
    [InlineData("IMPUESTO_DEPARTAMENTAL_PAGADO", "Flito", true)]
    [InlineData("impuesto_departamental_pagado", "user", false)]
    [InlineData("soat_pagado", "flito", false)]
    public void LaMarcaDeFlitoSoloProtegeElImpuestoConFuenteFlito(string clave, string fuente, bool protegida) =>
        ExternalAttachmentRules.IsProtectedFlitoMark(clave, fuente).Should().Be(protegida);

    [Theory]
    [InlineData("preasignacion", false, true)]
    [InlineData("asignado", false, true)]
    [InlineData("rechazado", true, true)]
    [InlineData("rechazado", false, false)]
    [InlineData("entregado", false, false)]
    [InlineData("borrador", false, false)]
    public void MarksPaidSoloEnLosEstadosEditables(string estado, bool subsanacion, bool marca) =>
        ExternalAttachmentRules.MarksPaid(estado, subsanacion).Should().Be(marca);

    [Theory]
    [InlineData("preasignacion", false)]
    [InlineData("asignado", false)]
    [InlineData("entregado", false)]
    [InlineData("rechazado", true)]
    public async Task AC3_LosEstadosQueAceptanElEnvioArchivan(string estado, bool subsanacion)
    {
        _repo.Target = Target(estado, subsanacion);

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Created);
    }

    [Theory]
    [InlineData("aprobado", false, true)]
    [InlineData("anulado", false, true)]
    [InlineData("revocado", false, true)]
    [InlineData("borrador", false, false)]
    [InlineData("preparado", false, false)]
    [InlineData("rechazado", false, false)]
    public async Task AC3_EstadoNoPermitido409ConEstadoYTerminalSinArchivar(string estado, bool subsanacion, bool terminal)
    {
        _repo.Target = Target(estado, subsanacion);

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Rejected);
        result.Error.Should().Be("not_allowed_in_state");
        result.Estado.Should().Be(estado);
        result.Terminal.Should().Be(terminal);
        result.TenantId.Should().Be(Tenant);
        _storage.Guardados.Should().BeEmpty("no se archiva nada");
        _repo.Escritos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("otro")]
    [InlineData("LIQUIDACION_IMPUESTO")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AC2_TipoFueraDeLaListaEsInvalidTipo(string? tipo)
    {
        _repo.Target = Target("asignado");

        var result = await Handler().HandleAsync(Tramite, tipo, Archivo(), TestContext.Current.CancellationToken);

        result.Error.Should().Be("invalid_tipo");
        _repo.Consultas.Should().Be(0, "se rechaza antes de tocar la base");
    }

    [Theory]
    [InlineData("application/msword")]
    [InlineData("image/gif")]
    [InlineData("")]
    public async Task AC2_TipoMimeFueraDeLaListaEsInvalidMime(string mime)
    {
        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(mime: mime), TestContext.Current.CancellationToken);

        result.Error.Should().Be("invalid_mime");
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("IMAGE/PNG; charset=binary")]
    public async Task AC2_LosCuatroTiposMimeDelContratoSeAceptan(string mime)
    {
        _repo.Target = Target("asignado");

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(mime: mime), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Created);
        _repo.Escritos[0].Mimetype.Should().Be(mime.Split(';')[0].ToLowerInvariant());
    }

    [Fact]
    public async Task AC2_VeinteMegasExactosSeAceptanYUnByteMasEsFileTooLarge()
    {
        _repo.Target = Target("asignado");
        var limite = new byte[20 * 1024 * 1024];
        limite[0] = 1;

        (await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(limite), TestContext.Current.CancellationToken))
            .Status.Should().Be(SubmitExternalAttachmentStatus.Created);
        (await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(new byte[(20 * 1024 * 1024) + 1]), TestContext.Current.CancellationToken))
            .Error.Should().Be("file_too_large");
    }

    [Fact]
    public async Task AC2_SinArchivoOArchivoVacioEsMissingFile()
    {
        (await Handler().HandleAsync(Tramite, "liquidacion_impuesto", null, TestContext.Current.CancellationToken))
            .Error.Should().Be("missing_file");
        (await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo([]), TestContext.Current.CancellationToken))
            .Error.Should().Be("missing_file");
    }

    [Fact]
    public async Task AC4_ElAdjuntoDelGestorGanaYSeConservaAunConElMismoSha256()
    {
        _repo.Target = Target("asignado") with
        {
            Vigentes = [new ExternalAttachmentExisting(Guid.CreateVersion7(), null, Sha(Pdf), "fm-gestor")],
        };

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Rejected);
        result.Error.Should().Be("attachment_exists");
        _storage.Guardados.Should().BeEmpty();
        _storage.Borrados.Should().BeEmpty("el archivo del gestor se conserva");
        _repo.Escritos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("kyverum")]
    [InlineData("")]
    public async Task AC4_CualquierProveedorQueNoSeaFlitoCuentaComoDelGestor(string? provider)
    {
        _repo.Target = Target("entregado") with
        {
            Vigentes = [new ExternalAttachmentExisting(Guid.CreateVersion7(), provider, Sha(OtroPdf), "fm-x")],
        };

        (await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken))
            .Error.Should().Be("attachment_exists");
    }

    [Fact]
    public async Task AC5_ConUnAdjuntoPropioYOtroArchivoReemplazaYRetiraElAnterior()
    {
        var anterior = new ExternalAttachmentExisting(Guid.CreateVersion7(), "flito", Sha(OtroPdf), "fm-anterior");
        _repo.Target = Target("asignado") with { Vigentes = [anterior] };

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Created);
        result.Receipt!.ReemplazoDe.Should().Be(anterior.Id);
        result.Receipt.AdjuntoId.Should().NotBe(anterior.Id);
        _repo.Retirados.Should().Equal(anterior.Id);
        _storage.Borrados.Should().Equal("fm-anterior");
        _repo.Confirmado.Should().BeTrue();
    }

    [Fact]
    public async Task AC5_ElMismoSha256DevuelveElMismoCuerpoSinTocarElAdjunto()
    {
        var anterior = new ExternalAttachmentExisting(Guid.CreateVersion7(), "flito", Sha(Pdf), "fm-anterior");
        _repo.Target = Target("asignado") with { Vigentes = [anterior] };

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Unchanged);
        result.Receipt.Should().Be(new ExternalAttachmentReceipt(anterior.Id, "liquidacion_impuesto", Sha(Pdf), null, false, true));
        _storage.Guardados.Should().BeEmpty("no se vuelve a subir el archivo");
        _storage.Borrados.Should().BeEmpty();
        _repo.Escritos.Should().BeEmpty();
        _repo.Retirados.Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_TramiteInexistenteOFueraDeAlcanceEsProcedureNotFound()
    {
        _repo.Target = null;

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Error.Should().Be("procedure_not_found");
        result.TenantId.Should().BeNull();
        _storage.Guardados.Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task AC6_ElAlmacenamientoCaidoEsStorageUnavailableYElClienteConservaSuAdjunto(Type falla)
    {
        var anterior = new ExternalAttachmentExisting(Guid.CreateVersion7(), "flito", Sha(OtroPdf), "fm-anterior");
        _repo.Target = Target("asignado") with { Vigentes = [anterior] };
        _storage.Falla = (Exception)Activator.CreateInstance(falla)!;

        var result = await Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        result.Error.Should().Be("storage_unavailable");
        result.TenantId.Should().Be(Tenant);
        _repo.Escritos.Should().BeEmpty();
        _repo.Retirados.Should().BeEmpty("el adjunto anterior no se retira si el nuevo no se pudo guardar");
        _storage.Borrados.Should().BeEmpty();
    }

    [Fact]
    public async Task UnErrorInesperadoDelAlmacenamientoNoSeDisfrazaDeStorageUnavailable()
    {
        _repo.Target = Target("asignado");
        _storage.Falla = new NotSupportedException("bug");

        var envio = () => Handler().HandleAsync(Tramite, "liquidacion_impuesto", Archivo(), TestContext.Current.CancellationToken);

        await envio.Should().ThrowAsync<NotSupportedException>();
    }

    [Theory]
    [InlineData("aprobado", false, true)]
    [InlineData("borrador", false, false)]
    [InlineData("rechazado", true, false)]
    public void LaReglaDeEstadoDistingueSiElTramiteVolveraAAceptar(string estado, bool subsanacion, bool terminal)
    {
        var (permitido, esTerminal) = ExternalAttachmentRules.EvaluateState(estado, subsanacion);

        esTerminal.Should().Be(terminal);
        permitido.Should().Be(estado == "rechazado");
    }

    private static ExternalAttachmentTarget Target(string estado, bool subsanacion = false) =>
        new(Tramite, Tenant, estado, subsanacion, Guid.CreateVersion7(), null, false, []);

    private sealed class FakeRepository : IExternalAttachmentRepository, IExternalAttachmentWriter
    {
        public ExternalAttachmentTarget? Target { get; set; }

        public int Consultas { get; private set; }

        public List<NewExternalAttachment> Escritos { get; } = [];

        public List<Guid> IdsEmitidos { get; } = [];

        public List<Guid> Retirados { get; } = [];

        public bool Confirmado { get; private set; }

        public Exception? FalloAlReemplazar { get; set; }

        public int Marcas { get; private set; }

        public List<string> Eventos { get; } = [];

        public async Task<T> RunLockedAsync<T>(
            Guid procedureId, string tipo,
            Func<ExternalAttachmentTarget?, IExternalAttachmentWriter, CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default)
        {
            Consultas++;
            var result = await work(Target, this, cancellationToken);
            Confirmado = true;
            Eventos.Add("confirmar");
            return result;
        }

        public Task<Guid> ReplaceAsync(NewExternalAttachment attachment, IReadOnlyCollection<Guid> retire, CancellationToken cancellationToken)
        {
            if (FalloAlReemplazar is not null)
            {
                throw FalloAlReemplazar;
            }

            Escritos.Add(attachment);
            Eventos.Add("reemplazar");
            Retirados.AddRange(retire);
            var id = Guid.CreateVersion7();
            IdsEmitidos.Add(id);
            return Task.FromResult(id);
        }

        public Task MarkTaxPaidAsync(CancellationToken cancellationToken)
        {
            Marcas++;
            Eventos.Add("marcar");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Guardados { get; } = [];

        public List<string> Borrados { get; } = [];

        public Exception? Falla { get; set; }

        public async Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            if (Falla is not null)
            {
                throw Falla;
            }

            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"fm-{Guardados.Count + 1}";
            Guardados.Add(path);
            return new StoredFile(path, Convert.ToHexStringLower(SHA256.HashData(ms.ToArray())), ms.Length);
        }

        public void Delete(string storagePath) => Borrados.Add(storagePath);

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMatrix : IResolvedChecklistMatrixProvider
    {
        public string[] Codigos { get; set; } = [];

        public string[] GeneradosPorElSistema { get; set; } = [];

        public Task<IReadOnlyList<ResolvedChecklistDoc>> GetForAsync(Guid procedureTypeId, Guid? transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResolvedChecklistDoc>>([
                .. Codigos.Select(c => new ResolvedChecklistDoc(c, c, true, 0)),
                .. GeneradosPorElSistema.Select(c => new ResolvedChecklistDoc(c, c, true, 0, EsGeneradoSistema: true)),
            ]);
    }
}
