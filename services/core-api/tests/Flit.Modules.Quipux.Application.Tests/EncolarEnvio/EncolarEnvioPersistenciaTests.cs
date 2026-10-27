using Flit.Modules.Quipux.Application.UseCases.EncolarEnvio;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.EncolarEnvio;

/// <summary>
/// Bug #13894 — <c>EncolarEnvioQuipuxHandler</c> llamaba <c>EnqueueIfAbsentAsync</c> (que solo
/// hace <c>db.Add</c> sin <c>SaveChanges</c>) y luego directamente al audit, que abre su propio
/// scope + DbContext. Al insertar el evento de bitácora, la FK <c>submission_id</c> apuntaba a
/// una fila inexistente → PostgreSQL 23503. El audit tragaba el error en silencio y la submission
/// se perdía al dispose del scope: <c>tramites.quipux_submissions</c> quedaba vacío eternamente.
///
/// Fix: tras <c>EnqueueIfAbsentAsync</c>, cuando la submission es nueva, llamar
/// <c>UpdateAsync(submission, null)</c> (hace <c>SaveChangesAsync</c>) ANTES del audit.
/// </summary>
public sealed class EncolarEnvioPersistenciaTests
{
    private readonly IProcedureInstanceRepository _instances = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _types = Substitute.For<IProcedureTypeRepository>();
    private readonly IQuipuxSubmissionRepository _submissions = Substitute.For<IQuipuxSubmissionRepository>();
    private readonly IQuipuxConsolidadoMaestroPort _consolidado = Substitute.For<IQuipuxConsolidadoMaestroPort>();
    private readonly IQuipuxOrganismoPort _organismos = Substitute.For<IQuipuxOrganismoPort>();
    private readonly IQuipuxTenantPort _tenants = Substitute.For<IQuipuxTenantPort>();
    private readonly IQuipuxAuditLog _audit = Substitute.For<IQuipuxAuditLog>();

    private EncolarEnvioQuipuxHandler Handler() =>
        new(_instances, _types, _submissions, _consolidado, _organismos, _tenants, _audit,
            NullLogger<EncolarEnvioQuipuxHandler>.Instance);

    // ── helpers de fixture ────────────────────────────────────────────────────

    /// <summary>
    /// Tipo de trámite con bloque quipux completo para traspaso bilateral.
    /// Todos los campos obligatorios (familia, tipoTramite, tipoRequisito, prefijo, campoPlaca,
    /// maxLongitudEmpresa) están presentes para pasar <c>EsUtilizable()</c>.
    /// </summary>
    private ProcedureType TipoConQuipux()
    {
        var tipo = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "TRASPASO_STANDARD",
            Name = "Traspaso",
            ExternalRefs = """{"quipux":{"familia":"TRASPASO","tipoTramite":16,"tipoRequisito":51,"prefijo":"TR","campoPlaca":"plate","campoVin":null,"maxLongitudEmpresa":35}}""",
        };
        _types.GetByIdAsync(tipo.Id, Arg.Any<CancellationToken>()).Returns(tipo);
        return tipo;
    }

    /// <summary>Instancia en estado Preparado, con TransitOfficeId y field value de placa.</summary>
    private ProcedureInstance Preparado(Guid procedureTypeId)
    {
        var tenantId = Guid.NewGuid();
        var transitOfficeId = Guid.NewGuid();

        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = procedureTypeId,
            Status = TramiteEstado.Preparado,
            TransitOfficeId = transitOfficeId,
            CreatedAt = DateTimeOffset.UtcNow,
            FieldValues = [new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123" }],
        };

        _instances
            .GetByIdWithDetailsAsync(instance.Id, tenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

        _organismos
            .ObtenerCodigoDivipoAsync(transitOfficeId, Arg.Any<CancellationToken>())
            .Returns("50001");

        _tenants
            .ObtenerRazonSocialAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns("Organismo de Transito Prueba SA");

        _consolidado
            .AsegurarAsync(instance.Id, tenantId, Arg.Any<CancellationToken>())
            .Returns(QuipuxConsolidadoMaestro.Generado(Guid.NewGuid()));

        return instance;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ruta feliz: cuando la submission es nueva, <c>UpdateAsync</c> debe haberse llamado con
    /// <c>auditEvent = null</c> ANTES de que el audit reciba su llamada.
    ///
    /// Verifica el fix principal del Bug #13894: sin esta llamada, la FK 23503 reventaba en el
    /// scope del audit (silencioso) y la submission nunca se persistía.
    /// </summary>
    [Fact]
    public async Task NuevaSubmission_UpdateAsyncSeLlamaAntesDelAudit()
    {
        var ct = TestContext.Current.CancellationToken;

        var tipo = TipoConQuipux();
        var instance = Preparado(tipo.Id);

        // EnqueueIfAbsentAsync devuelve la misma entidad que recibió → mismo Id → yaExistia = false.
        _submissions
            .EnqueueIfAbsentAsync(Arg.Any<QuipuxSubmission>(), Arg.Any<CancellationToken>())
            .Returns(info => info.Arg<QuipuxSubmission>());

        var updateLlamadasAntesDe = 0;
        var auditLlamada = false;

        // Captura el orden: registra cuántas veces se llamó UpdateAsync antes de que llegue WriteAsync.
        _submissions
            .When(r => r.UpdateAsync(Arg.Any<QuipuxSubmission>(), null, Arg.Any<CancellationToken>()))
            .Do(_ => updateLlamadasAntesDe++);

        _audit
            .When(a => a.WriteAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<object?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                // Al momento de WriteAsync, UpdateAsync ya debe haberse llamado.
                auditLlamada = true;
                updateLlamadasAntesDe.Should().Be(1,
                    "UpdateAsync debe persistir la submission ANTES de llamar al audit (Bug #13894)");
            });

        var result = await Handler().HandleAsync(
            new EncolarEnvioQuipuxCommand
            {
                ProcedureInstanceId = instance.Id,
                TenantId = instance.TenantId,
            },
            ct);

        result.Status.Should().Be(EncolarEnvioQuipuxStatus.Encolada);
        result.TieneSubmission.Should().BeTrue();

        // UpdateAsync debe haberse llamado exactamente una vez, con auditEvent = null.
        await _submissions.Received(1).UpdateAsync(
            Arg.Any<QuipuxSubmission>(), null, Arg.Any<CancellationToken>());

        // El audit también debe haberse llamado.
        auditLlamada.Should().BeTrue("el audit debe ejecutarse después de persistir la submission");
        await _audit.Received(1).WriteAsync(
            instance.TenantId,
            Arg.Any<Guid>(),
            QuipuxStage.ConsolidadoGenerado,
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Idempotencia intacta: cuando el trámite ya tiene submission activa (<c>YaEncolada</c>),
    /// <c>UpdateAsync</c> NO debe llamarse (no hay nada que persistir) y el audit tampoco.
    /// </summary>
    [Fact]
    public async Task YaEncolada_UpdateAsyncNoSeLlama_YAuditTampoco()
    {
        var ct = TestContext.Current.CancellationToken;

        var tipo = TipoConQuipux();
        var instance = Preparado(tipo.Id);

        // Devuelve una submission con Id DISTINTO → yaExistia = true → retorno temprano.
        var existente = new QuipuxSubmission
        {
            Id = Guid.NewGuid(), // distinto del Id que va a construir el handler
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            DocumentName = "OrganismoTransitoPrueba_TR_20261023_1200_ABC123",
            Status = QuipuxSubmissionEstado.Pendiente,
        };

        _submissions
            .EnqueueIfAbsentAsync(Arg.Any<QuipuxSubmission>(), Arg.Any<CancellationToken>())
            .Returns(existente);

        var result = await Handler().HandleAsync(
            new EncolarEnvioQuipuxCommand
            {
                ProcedureInstanceId = instance.Id,
                TenantId = instance.TenantId,
            },
            ct);

        result.Status.Should().Be(EncolarEnvioQuipuxStatus.YaEncolada);
        result.SubmissionId.Should().Be(existente.Id);

        // UpdateAsync y audit no deben tocarse si ya existía una submission activa.
        await _submissions.DidNotReceive().UpdateAsync(
            Arg.Any<QuipuxSubmission>(), Arg.Any<QuipuxSubmissionEvent?>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().WriteAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<object?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// La submission que llega a <c>UpdateAsync</c> debe tener el mismo <c>ProcedureInstanceId</c>
    /// y <c>TenantId</c> del trámite, y DocumentName no vacío.
    /// </summary>
    [Fact]
    public async Task NuevaSubmission_UpdateAsyncRecibeLosAtributosClave()
    {
        var ct = TestContext.Current.CancellationToken;

        var tipo = TipoConQuipux();
        var instance = Preparado(tipo.Id);

        QuipuxSubmission? submissionCapturada = null;

        _submissions
            .EnqueueIfAbsentAsync(Arg.Any<QuipuxSubmission>(), Arg.Any<CancellationToken>())
            .Returns(info => info.Arg<QuipuxSubmission>());

        _submissions
            .When(r => r.UpdateAsync(Arg.Any<QuipuxSubmission>(), null, Arg.Any<CancellationToken>()))
            .Do(info => submissionCapturada = info.Arg<QuipuxSubmission>());

        await Handler().HandleAsync(
            new EncolarEnvioQuipuxCommand
            {
                ProcedureInstanceId = instance.Id,
                TenantId = instance.TenantId,
            },
            ct);

        submissionCapturada.Should().NotBeNull();
        submissionCapturada!.ProcedureInstanceId.Should().Be(instance.Id);
        submissionCapturada.TenantId.Should().Be(instance.TenantId);
        submissionCapturada.DocumentName.Should().NotBeNullOrWhiteSpace(
            "el document_name debe calcularse antes de persistir");
        submissionCapturada.Status.Should().Be(QuipuxSubmissionEstado.Pendiente,
            "una submission recién encolada siempre empieza en pendiente");
        submissionCapturada.DivipoCode.Should().Be("50001");
    }
}
