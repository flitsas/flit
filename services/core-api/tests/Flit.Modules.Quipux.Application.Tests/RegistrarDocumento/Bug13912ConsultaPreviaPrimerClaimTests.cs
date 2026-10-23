using Flit.Modules.Quipux.Application.UseCases.RegistrarDocumento;
using Flit.Modules.Quipux.Domain.Codigos;
using Flit.Modules.Quipux.Domain.Configuracion;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Puertos;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.RegistrarDocumento;

/// <summary>
/// Bug #13912 — primer claim (Attempts==1 tras el incremento del repositorio) radica sin consulta
/// previa; reintento (Attempts&gt;1) consulta y, si Quipux no trae codigo, sigue a registrar.
/// </summary>
public sealed class Bug13912ConsultaPreviaPrimerClaimTests
{
    private const string ExternalRefsTraspaso = """
        {"quipux": {"familia": "TRASPASO", "tipoTramite": 16, "tipoRequisito": 51, "prefijo": "TR",
          "campoPlaca": "placa", "campoVin": null, "maxLongitudEmpresa": 25}}
        """;

    [Fact]
    public async Task Primer_claim_Attempts_1_radica_sin_llamar_ValidateStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, client, _) = ArmarHandler(attempts: 1, ct);

        var result = await handler.HandleAsync(
            new RegistrarDocumentoQuipuxCommand { SubmissionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee") },
            ct);

        result.Status.Should().Be(RegistrarDocumentoQuipuxStatus.Registrado);
        await client.DidNotReceive()
            .ValidateStatusAsync(Arg.Any<QuipuxValidateStatusRequest>(), Arg.Any<CancellationToken>());
        await client.Received(1)
            .RegisterDocumentAsync(Arg.Any<QuipuxRegisterRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reintento_Attempts_2_con_consulta_sin_codigo_continúa_a_registrar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, client, _) = ArmarHandler(attempts: 2, ct);
        client.ValidateStatusAsync(Arg.Any<QuipuxValidateStatusRequest>(), ct)
            .Returns(new QuipuxValidateStatusResult(
                QuipuxCodigos.RegistroError, "sin_codigo", null, null));

        var result = await handler.HandleAsync(
            new RegistrarDocumentoQuipuxCommand { SubmissionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee") },
            ct);

        result.Status.Should().Be(RegistrarDocumentoQuipuxStatus.Registrado);
        await client.Received(1)
            .ValidateStatusAsync(Arg.Any<QuipuxValidateStatusRequest>(), Arg.Any<CancellationToken>());
        await client.Received(1)
            .RegisterDocumentAsync(Arg.Any<QuipuxRegisterRequest>(), Arg.Any<CancellationToken>());
    }

    private static (RegistrarDocumentoQuipuxHandler Handler, IQuipuxClient Client, Guid SubmissionId)
        ArmarHandler(int attempts, CancellationToken ct)
    {
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var submissionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var attachmentId = Guid.NewGuid();

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Traspaso,
            Id = instanceId,
            TenantId = tenantId,
            Status = TramiteEstado.Preparado,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FT2-0000190",
            Plate = "GTR553",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "placa",
            ValueText = "GTR553",
        });
        instance.Actors.Add(new ProcedureInstanceActor
        {
            ActorType = "vendedor",
            DocumentType = "C",
            DocumentNumber = "1020304050",
            FullName = "Vendedor Test",
        });
        instance.Actors.Add(new ProcedureInstanceActor
        {
            ActorType = "comprador",
            DocumentType = "C",
            DocumentNumber = "1099887766",
            FullName = "Comprador Test",
        });

        var settings = Substitute.For<IQuipuxSettingsRepository>();
        settings.GetAsync(ct).Returns(new QuipuxSettings
        {
            Enabled = true,
            UrlLogin = "https://quipux.test/login",
            UrlRegisterDocument = "https://quipux.test/register",
            UrlValidateStatus = "https://quipux.test/status",
            Username = "u",
            Password = "p",
            ConsumerCode = "1003",
            Bucket = "b",
            AwsAccessKeyId = "k",
            AwsSecretAccessKey = "s",
            OfficerDocumentType = 3,
            OfficerDocumentNumber = "901698038",
            MaxAttempts = 5,
        });

        var submissions = Substitute.For<IQuipuxSubmissionRepository>();
        submissions.GetByIdAsync(submissionId, ct).Returns(new QuipuxSubmission
        {
            Id = submissionId,
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            DocumentName = "ICT_DAG_TR_20261023_1801_GTR553",
            AttachmentId = attachmentId,
            DivipoCode = "17001",
            QuipuxProcedureType = 16,
            QuipuxRequirementType = 51,
            Status = QuipuxSubmissionEstado.Pendiente,
            Attempts = attempts,
        });

        var instances = Substitute.For<IProcedureInstanceRepository>();
        instances.GetByIdWithDetailsAsync(instanceId, tenantId, ct).Returns(instance);
        instances.GetByIdAsync(instanceId, tenantId, ct).Returns(instance);

        var procedureTypes = Substitute.For<IProcedureTypeRepository>();
        procedureTypes.GetByIdAsync(instance.ProcedureTypeId, ct)
            .Returns(new ProcedureType { Id = instance.ProcedureTypeId, ExternalRefs = ExternalRefsTraspaso });

        var uploader = Substitute.For<IQuipuxDocumentUploader>();
        uploader.UploadAsync(attachmentId, Arg.Any<string>(), ct).Returns("FLIT/doc.pdf");

        var lifecycle = Substitute.For<ITramiteLifecycleService>();
        lifecycle.TransitionAsync(Arg.Any<TramiteTransitionCommand>(), ct)
            .Returns(TramiteTransitionOutcome.Ok(instance));

        var client = Substitute.For<IQuipuxClient>();
        client.RegisterDocumentAsync(Arg.Any<QuipuxRegisterRequest>(), ct)
            .Returns(new QuipuxRegisterResult(81, "ok"));

        var handler = new RegistrarDocumentoQuipuxHandler(
            settings, submissions, client, uploader, instances, procedureTypes, lifecycle,
            Substitute.For<IQuipuxAuditLog>(),
            NullLogger<RegistrarDocumentoQuipuxHandler>.Instance);

        return (handler, client, submissionId);
    }
}
