using Flit.Modules.Quipux.Application.UseCases.ConsultarEstado;
using Flit.Modules.Quipux.Domain.Codigos;
using Flit.Modules.Quipux.Domain.Configuracion;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Puertos;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.ConsultarEstado;

/// <summary>
/// HU #10871 (AC2) — un callback de rechazo de la integración Quipux (sondeo de estado) transiciona
/// el trámite a <c>subsanacion</c> (no a <c>rechazado</c>): es una observación subsanable, con el
/// motivo de la secretaría en el checklist HÍBRIDO de <c>metadata</c> (sin ítems, Quipux no entrega
/// un checklist estructurado).
/// </summary>
public sealed class ConsultarEstadoQuipuxHandlerTests
{
    private readonly IQuipuxSettingsRepository _settings = Substitute.For<IQuipuxSettingsRepository>();
    private readonly IQuipuxSubmissionRepository _submissions = Substitute.For<IQuipuxSubmissionRepository>();
    private readonly IQuipuxClient _client = Substitute.For<IQuipuxClient>();
    private readonly IProcedureInstanceRepository _instances = Substitute.For<IProcedureInstanceRepository>();
    private readonly ITramiteLifecycleService _lifecycle = Substitute.For<ITramiteLifecycleService>();
    private readonly IQuipuxAuditLog _audit = Substitute.For<IQuipuxAuditLog>();
    private readonly ConsultarEstadoQuipuxHandler _sut;

    public ConsultarEstadoQuipuxHandlerTests()
    {
        _sut = new ConsultarEstadoQuipuxHandler(
            _settings, _submissions, _client, _instances, _lifecycle, _audit,
            NullLogger<ConsultarEstadoQuipuxHandler>.Instance);
    }

    [Fact]
    public async Task Ac2_RechazoDeSecretaria_TransicionaASubsanacionConMotivoEnMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();

        var submission = new QuipuxSubmission
        {
            Id = submissionId,
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            DocumentName = "doc-1",
            DivipoCode = "0101",
            Status = QuipuxSubmissionEstado.Registrado,
        };

        var instance = new ProcedureInstance
        {
            Id = instanceId,
            TenantId = tenantId,
            Status = TramiteEstado.Entregado,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _settings.GetAsync(ct).Returns(new QuipuxSettings
        {
            Enabled = true,
            UrlLogin = "x",
            UrlRegisterDocument = "x",
            UrlValidateStatus = "x",
            Username = "x",
            Password = "x",
            ConsumerCode = "1003",
            Bucket = "b",
            AwsAccessKeyId = "k",
            AwsSecretAccessKey = "s",
            OfficerDocumentNumber = "900000000",
        });
        _submissions.GetByIdAsync(submissionId, ct).Returns(submission);
        _client.ValidateStatusAsync(Arg.Any<QuipuxValidateStatusRequest>(), ct)
            .Returns(new QuipuxValidateStatusResult(
                Codigo: 1, Descripcion: null,
                EstadoTramiteCodigo: QuipuxCodigos.TramiteRechazado,
                EstadoTramiteDescripcion: "Documentación incompleta"));
        _instances.GetByIdAsync(instanceId, tenantId, ct).Returns(instance);

        TramiteTransitionCommand? captured = null;
        _lifecycle
            .TransitionAsync(Arg.Do<TramiteTransitionCommand>(c => captured = c), ct)
            .Returns(TramiteTransitionOutcome.Ok(instance));

        var result = await _sut.HandleAsync(
            new ConsultarEstadoQuipuxCommand { SubmissionId = submissionId }, ct);

        result.Status.Should().Be(ConsultarEstadoQuipuxStatus.Rechazado);
        result.MotivoRechazo.Should().Be("Documentación incompleta");

        captured.Should().NotBeNull();
        // AC2 — destino 'subsanacion', NO 'rechazado': el trámite sigue vivo para corregirse.
        captured!.ToStatus.Should().Be(TramiteEstado.Rechazado);
        captured.Reason.Should().Be("Documentación incompleta");
        captured.Metadata.Should().NotBeNullOrWhiteSpace();
        var observation = SubsanacionObservation.FromJson(captured.Metadata);
        observation.Should().NotBeNull();
        observation!.Motivo.Should().Be("Documentación incompleta");
        observation.Items.Should().BeEmpty();

        submission.Status.Should().Be(QuipuxSubmissionEstado.Rechazado);
        submission.RejectionReason.Should().Be("Documentación incompleta");
    }

    [Fact]
    public async Task Ac2_RechazoSinDescripcion_UsaMotivoPorDefecto()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();

        var submission = new QuipuxSubmission
        {
            Id = submissionId,
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            DocumentName = "doc-2",
            DivipoCode = "0101",
            Status = QuipuxSubmissionEstado.Registrado,
        };

        var instance = new ProcedureInstance
        {
            Id = instanceId,
            TenantId = tenantId,
            Status = TramiteEstado.Entregado,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000002",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _settings.GetAsync(ct).Returns(new QuipuxSettings
        {
            Enabled = true,
            UrlLogin = "x",
            UrlRegisterDocument = "x",
            UrlValidateStatus = "x",
            Username = "x",
            Password = "x",
            ConsumerCode = "1003",
            Bucket = "b",
            AwsAccessKeyId = "k",
            AwsSecretAccessKey = "s",
            OfficerDocumentNumber = "900000000",
        });
        _submissions.GetByIdAsync(submissionId, ct).Returns(submission);
        _client.ValidateStatusAsync(Arg.Any<QuipuxValidateStatusRequest>(), ct)
            .Returns(new QuipuxValidateStatusResult(
                Codigo: 1, Descripcion: null,
                EstadoTramiteCodigo: QuipuxCodigos.TramiteRechazado,
                EstadoTramiteDescripcion: null));
        _instances.GetByIdAsync(instanceId, tenantId, ct).Returns(instance);

        TramiteTransitionCommand? captured = null;
        _lifecycle
            .TransitionAsync(Arg.Do<TramiteTransitionCommand>(c => captured = c), ct)
            .Returns(TramiteTransitionOutcome.Ok(instance));

        await _sut.HandleAsync(new ConsultarEstadoQuipuxCommand { SubmissionId = submissionId }, ct);

        captured.Should().NotBeNull();
        captured!.ToStatus.Should().Be(TramiteEstado.Rechazado);
        captured.Reason.Should().Be("Rechazado por el organismo de tránsito vía Quipux (sin descripción).");
        var observation = SubsanacionObservation.FromJson(captured.Metadata);
        observation.Should().NotBeNull();
        observation!.Motivo.Should().Be("Rechazado por el organismo de tránsito vía Quipux (sin descripción).");
    }

    [Fact] // HU #10872 (AC1) — el rechazo de Quipux también captura el snapshot de field_values.
    public async Task Ac1_RechazoDeSecretaria_CapturaSnapshotDeFieldValuesEnMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();

        var submission = new QuipuxSubmission
        {
            Id = submissionId,
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            DocumentName = "doc-3",
            DivipoCode = "0101",
            Status = QuipuxSubmissionEstado.Registrado,
        };

        var instance = new ProcedureInstance
        {
            Id = instanceId,
            TenantId = tenantId,
            Status = TramiteEstado.Entregado,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000003",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _settings.GetAsync(ct).Returns(new QuipuxSettings
        {
            Enabled = true,
            UrlLogin = "x",
            UrlRegisterDocument = "x",
            UrlValidateStatus = "x",
            Username = "x",
            Password = "x",
            ConsumerCode = "1003",
            Bucket = "b",
            AwsAccessKeyId = "k",
            AwsSecretAccessKey = "s",
            OfficerDocumentNumber = "900000000",
        });
        _submissions.GetByIdAsync(submissionId, ct).Returns(submission);
        _client.ValidateStatusAsync(Arg.Any<QuipuxValidateStatusRequest>(), ct)
            .Returns(new QuipuxValidateStatusResult(
                Codigo: 1, Descripcion: null,
                EstadoTramiteCodigo: QuipuxCodigos.TramiteRechazado,
                EstadoTramiteDescripcion: "Falta corregir el VIN"));
        _instances.GetByIdAsync(instanceId, tenantId, ct).Returns(instance);
        _instances.GetFieldValuesAsync(instanceId, tenantId, ct).Returns(
        [
            new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = instanceId,
                FieldKey = "vin",
                ValueText = "1HGCM82633A004352",
            },
        ]);

        TramiteTransitionCommand? captured = null;
        _lifecycle
            .TransitionAsync(Arg.Do<TramiteTransitionCommand>(c => captured = c), ct)
            .Returns(TramiteTransitionOutcome.Ok(instance));

        await _sut.HandleAsync(new ConsultarEstadoQuipuxCommand { SubmissionId = submissionId }, ct);

        var observation = SubsanacionObservation.FromJson(captured!.Metadata);
        observation.Should().NotBeNull();
        observation!.FieldSnapshot.Should().NotBeNull();
        observation.FieldSnapshot!["vin"].Should().Be("1HGCM82633A004352");
    }
}
