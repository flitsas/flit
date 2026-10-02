using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13194 (P3-26) — salida del gestor en <c>asignado</c>: con la compañía exigiendo SOAT vigente y un
/// RUNT que no lo reporta, el OCR del PDF del SOAT se registra (solo <c>soat_estado</c> y
/// <c>soat_vencimiento</c>) siempre que el trámite tenga un adjunto de SOAT no histórico. No abre huecos:
/// sin adjunto se rechaza, otros tipos y otros estados siguen en <c>not_draft</c>, y un SOAT afirmado por el
/// RUNT (vigente o vencido) no se pisa.
///
/// Uso de ejemplo:
/// <code>
/// var (result, error) = await new PersistOcrFieldsHandler(repo).HandleAsync(id, tenantId,
///     new PersistOcrFieldsRequest("soat", new Dictionary&lt;string, string?&gt; { ["estado_poliza"] = "VIGENTE" }), ct);
/// </code>
/// </summary>
public sealed class PersistOcrFieldsSoatAsignadoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly PersistOcrFieldsHandler _sut;
    private readonly Guid _id = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PersistOcrFieldsSoatAsignadoTests() => _sut = new PersistOcrFieldsHandler(_repo);

    private ProcedureInstance Instance(string status, string? adjunto = "soat", bool historico = false)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = _id,
            TenantId = _tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013194",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (adjunto is not null)
        {
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                ProcedureInstanceId = _id,
                Tipo = adjunto,
                Filename = "soat.pdf",
                Mimetype = "application/pdf",
                Source = "user",
                IsHistorico = historico,
                UploadedAt = DateTimeOffset.UtcNow,
            });
        }

        _repo.GetByIdWithDetailsAsync(_id, _tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithAttachmentsAsync(_id, _tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private static void Seed(ProcedureInstance instance, string key, string value, string source) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = key,
            ValueText = value,
            Source = source,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static ProcedureInstanceFieldValue? Campo(ProcedureInstance i, string key) =>
        i.FieldValues.FirstOrDefault(f => f.FieldKey == key);

    // Mismo payload que la suite P3-26.
    private static PersistOcrFieldsRequest OcrSoat() =>
        new("soat", new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["estado_poliza"] = "VIGENTE",
            ["fecha_vencimiento"] = "2027-01-31",
            ["numero_poliza"] = "QA-OCR-1",
            ["aseguradora"] = "ASEGURADORA QA S.A.",
        });

    /// <summary>Happy path P3-26: tras validate-runt (unknown de consulta) el OCR registra estado y vencimiento.</summary>
    [Fact]
    public async Task Asignado_conAdjunto_yRuntSinSoat_registraEstadoYVencimiento_eIgnoraLoDemas()
    {
        var instance = Instance(TramiteEstado.Asignado);
        Seed(instance, SoatGate.FieldKey, SoatGate.Unknown, "consultation");
        Seed(instance, "soat_vencimiento", "2025-01-01", "consultation");

        var (result, error) = await _sut.HandleAsync(_id, _tenantId, OcrSoat(), Ct);

        error.Should().BeNull();
        result!.Persistidos.Should().Be(2);
        result.IgnoradosFueraDeAlcance.Should().BeEquivalentTo("numero_poliza", "aseguradora");
        Campo(instance, SoatGate.FieldKey)!.ValueText.Should().Be(SoatGate.Vigente);
        Campo(instance, SoatGate.FieldKey)!.Source.Should().Be(PersistOcrFieldsHandler.OcrSource);
        Campo(instance, "soat_vencimiento")!.ValueText.Should().Be("2027-01-31");
        Campo(instance, "soat_vencimiento")!.Source.Should().Be(PersistOcrFieldsHandler.OcrSource);
        Campo(instance, "soat_poliza").Should().BeNull();
        await _repo.Received(1).SaveChangesAsync(Ct);
    }

    /// <summary>Happy path: el adjunto <c>soat_manual</c> también es soporte; sin estado previo se crean las llaves.</summary>
    [Fact]
    public async Task Asignado_conAdjuntoSoatManual_sinEstadoPrevio_creaLasLlaves()
    {
        var instance = Instance(TramiteEstado.Asignado, adjunto: "soat_manual");

        var (result, error) = await _sut.HandleAsync(_id, _tenantId, OcrSoat(), Ct);

        error.Should().BeNull();
        result!.Persistidos.Should().Be(2);
        Campo(instance, SoatGate.FieldKey)!.ValueText.Should().Be(SoatGate.Vigente);
    }

    /// <summary>SEC: sin adjunto de SOAT no hay soporte ⇒ se rechaza y no se escribe nada.</summary>
    [Fact]
    public async Task Asignado_sinAdjunto_rechazaSinEscribir()
    {
        var instance = Instance(TramiteEstado.Asignado, adjunto: null);

        var (result, error) = await _sut.HandleAsync(_id, _tenantId, OcrSoat(), Ct);

        result.Should().BeNull();
        error.Should().Be(PersistOcrFieldsHandler.SoporteSoatRequeridoError);
        instance.FieldValues.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>SEC edge: un adjunto de SOAT histórico (revocado) o de otro tipo no es soporte.</summary>
    [Theory]
    [InlineData("soat", true)]
    [InlineData("rtm", false)]
    public async Task Asignado_adjuntoNoValido_rechaza(string tipoAdjunto, bool historico)
    {
        Instance(TramiteEstado.Asignado, adjunto: tipoAdjunto, historico: historico);

        var (_, error) = await _sut.HandleAsync(_id, _tenantId, OcrSoat(), Ct);

        error.Should().Be(PersistOcrFieldsHandler.SoporteSoatRequeridoError);
    }

    /// <summary>Precedencia: un vencido o vigente AFIRMADO por el RUNT no lo pisa el PDF.</summary>
    [Theory]
    [InlineData(SoatGate.Vencido)]
    [InlineData(SoatGate.Vigente)]
    public async Task Asignado_runtAfirmoElSoat_noLoPisa(string estadoRunt)
    {
        var instance = Instance(TramiteEstado.Asignado);
        Seed(instance, SoatGate.FieldKey, estadoRunt, "consultation");

        var (result, error) = await _sut.HandleAsync(_id, _tenantId, OcrSoat(), Ct);

        error.Should().BeNull();
        result!.Persistidos.Should().Be(0);
        result.OmitidosPorPrecedencia.Should().BeEquivalentTo(SoatGate.FieldKey, "soat_vencimiento");
        Campo(instance, SoatGate.FieldKey)!.ValueText.Should().Be(estadoRunt);
        Campo(instance, "soat_vencimiento").Should().BeNull();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Contrato: en 'asignado' solo el OCR del SOAT; otros tipos siguen en not_draft.</summary>
    [Fact]
    public async Task Asignado_otroTipoDeOcr_sigueEnNotDraft()
    {
        Instance(TramiteEstado.Asignado);

        var (_, error) = await _sut.HandleAsync(_id, _tenantId,
            new PersistOcrFieldsRequest("rtm", new Dictionary<string, string?> { ["estado"] = "VIGENTE" }), Ct);

        error.Should().Be("not_draft");
    }

    /// <summary>Contrato: fuera de 'asignado' (preasignación, entregado) la excepción no aplica.</summary>
    [Theory]
    [InlineData(TramiteEstado.Preasignacion)]
    [InlineData(TramiteEstado.Entregado)]
    public async Task OtrosEstadosPostRadicacion_siguenEnNotDraft(string estado)
    {
        var instance = Instance(estado);

        var (_, error) = await _sut.HandleAsync(_id, _tenantId, OcrSoat(), Ct);

        error.Should().Be("not_draft");
        instance.FieldValues.Should().BeEmpty();
    }
}
