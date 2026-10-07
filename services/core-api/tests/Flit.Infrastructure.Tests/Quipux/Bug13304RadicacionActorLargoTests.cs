using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Quipux;
using Flit.Modules.Quipux.Application.UseCases.RegistrarDocumento;
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

namespace Flit.Infrastructure.Tests.Quipux;

/// <summary>
/// Bug #13304 — actor con nombre de 320 caracteres y teléfono de 50. Documenta qué viaja en la petición de
/// radicación ante el organismo (Quipux): el contrato de <c>registerDocument</c> NO lleva nombre ni teléfono
/// del propietario; solo su tipo (código numérico) y número de documento. Por eso el largo de esos campos no
/// afecta la radicación y nuestro código no los recorta ni los limita en este punto.
/// <para>
/// ACLARACIÓN: el comportamiento del servidor de Quipux/RUNT no se prueba offline (el usuario confirma que ya
/// funciona); aquí solo se prueba lo que arma nuestro código.
/// </para>
/// <para>Uso de ejemplo:</para>
/// <code>
/// var r = await handler.HandleAsync(new RegistrarDocumentoQuipuxCommand { SubmissionId = id }, ct);
/// // QuipuxRegisterRequest: placa, vin, codigoDivipo, consumidor, tipoTramite, tipoRequisito, documento,
/// // tipoDocumentoPropietario, nroDocumentoPropietario, tipoDocumentoFuncionario, nroDocumentoFuncionario,
/// // contenidoDocumento — sin nombre ni teléfono.
/// </code>
/// </summary>
public sealed class Bug13304RadicacionActorLargoTests
{
    private static readonly string Nombre320 = new string('N', 150) + " " + new string('A', 100) + " " + new string('R', 68);
    private static readonly string Telefono50 = new('3', 50);

    private const string ExternalRefsMatricula = """
        {"quipux": {"familia": "MATRICULA", "tipoTramite": 13, "tipoRequisito": 51, "prefijo": "MI",
          "campoPlaca": null, "campoVin": "vin", "maxLongitudEmpresa": 25}}
        """;

    private static readonly string[] CamposDelContrato =
    [
        "placa", "vin", "codigoDivipo", "consumidor", "tipoTramite", "tipoRequisito",
        "tipoDocumentoPropietario", "documento", "nroDocumentoPropietario",
        "tipoDocumentoFuncionario", "nroDocumentoFuncionario", "contenidoDocumento",
    ];

    [Fact]
    public async Task Radicacion_con_actor_de_nombre_320_y_telefono_50_no_lleva_nombre_ni_telefono()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = instanceId,
            TenantId = tenantId,
            Status = TramiteEstado.Preparado,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // Matrícula: el propietario al radicar es el comprador.
        instance.Actors.Add(new ProcedureInstanceActor
        {
            ActorType = "comprador",
            // Letra del vocabulario que mapea QuipuxTipoDocumento (C = 2).
            DocumentType = "C",
            DocumentNumber = "1020304050",
            FullName = Nombre320,
            Phone = Telefono50,
            Email = "a@b.co",
        });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "9BWZZZ377VT004251" });

        var settings = Substitute.For<IQuipuxSettingsRepository>();
        settings.GetAsync(ct).Returns(Settings("https://quipux.test/login-handler"));
        var submissions = Substitute.For<IQuipuxSubmissionRepository>();
        submissions.GetByIdAsync(submissionId, ct).Returns(new QuipuxSubmission
        {
            Id = submissionId,
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            DocumentName = "MI-DOC-1",
            DivipoCode = "05001",
            QuipuxProcedureType = 13,
            QuipuxRequirementType = 51,
            Status = QuipuxSubmissionEstado.Pendiente,
        });
        var instances = Substitute.For<IProcedureInstanceRepository>();
        instances.GetByIdWithDetailsAsync(instanceId, tenantId, ct).Returns(instance);
        instances.GetByIdAsync(instanceId, tenantId, ct).Returns(instance);
        var procedureTypes = Substitute.For<IProcedureTypeRepository>();
        procedureTypes.GetByIdAsync(instance.ProcedureTypeId, ct)
            .Returns(new ProcedureType { Id = instance.ProcedureTypeId, ExternalRefs = ExternalRefsMatricula });
        var uploader = Substitute.For<IQuipuxDocumentUploader>();
        uploader.UploadAsync(Arg.Any<Guid>(), "MI-DOC-1", ct).Returns("FLIT/MI-DOC-1.pdf");
        var lifecycle = Substitute.For<ITramiteLifecycleService>();
        lifecycle.TransitionAsync(Arg.Any<TramiteTransitionCommand>(), ct).Returns(TramiteTransitionOutcome.Ok(instance));
        var client = Substitute.For<IQuipuxClient>();
        QuipuxRegisterRequest? enviado = null;
        client.RegisterDocumentAsync(Arg.Do<QuipuxRegisterRequest>(r => enviado = r), ct)
            .Returns(new QuipuxRegisterResult(81, "ok"));

        var handler = new RegistrarDocumentoQuipuxHandler(
            settings, submissions, client, uploader, instances, procedureTypes, lifecycle,
            Substitute.For<IQuipuxAuditLog>(), NullLogger<RegistrarDocumentoQuipuxHandler>.Instance);

        var act = async () => await handler.HandleAsync(new RegistrarDocumentoQuipuxCommand { SubmissionId = submissionId }, ct);

        var result = (await act.Should().NotThrowAsync()).Subject;
        result.Status.Should().Be(RegistrarDocumentoQuipuxStatus.Registrado);
        enviado.Should().NotBeNull();
        enviado!.NroDocumentoPropietario.Should().Be("1020304050");
        enviado.TipoDocumentoPropietario.Should().Be(2);
        // Ningún campo del request lleva el nombre ni el teléfono del actor (ni completos ni recortados).
        var valores = typeof(QuipuxRegisterRequest).GetProperties()
            .Select(p => p.GetValue(enviado)?.ToString() ?? string.Empty)
            .ToList();
        valores.Should().NotContain(v => v.Contains(new string('N', 20), StringComparison.Ordinal));
        valores.Should().NotContain(v => v.Contains(new string('3', 20), StringComparison.Ordinal));
    }

    [Fact]
    public void El_contrato_de_radicacion_no_tiene_campos_de_nombre_ni_telefono()
    {
        var propiedades = typeof(QuipuxRegisterRequest).GetProperties().Select(p => p.Name).ToList();

        propiedades.Should().HaveCount(CamposDelContrato.Length);
        propiedades.Should().NotContain(p =>
            p.Contains("Nombre", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Name", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Telefono", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Phone", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Celular", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task El_body_HTTP_de_registerDocument_solo_lleva_los_12_campos_del_contrato()
    {
        var ct = TestContext.Current.CancellationToken;
        // URL de login única: la caché de token de QuipuxApiClient es estática y se indexa por ella.
        var urlLogin = $"https://quipux.test/login-{Guid.NewGuid():N}";
        var http = new QuipuxFakeHandler();
        var settings = Substitute.For<IQuipuxSettingsRepository>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns(Settings(urlLogin));
        var client = new QuipuxApiClient(new HttpClient(http), settings, NullLogger<QuipuxApiClient>.Instance);

        var act = async () => await client.RegisterDocumentAsync(new QuipuxRegisterRequest
        {
            Placa = string.Empty,
            Vin = "9BWZZZ377VT004251",
            CodigoDivipo = "05001",
            Consumidor = "1003",
            TipoTramite = 13,
            TipoRequisito = 51,
            Documento = "MI-DOC-1",
            TipoDocumentoPropietario = 2,
            NroDocumentoPropietario = "1020304050",
            TipoDocumentoFuncionario = 3,
            NroDocumentoFuncionario = "900000000",
            ContenidoDocumento = "FLIT/MI-DOC-1.pdf",
        }, ct);

        (await act.Should().NotThrowAsync()).Subject.Codigo.Should().Be(81);
        using var body = JsonDocument.Parse(http.RegisterBody!);
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(CamposDelContrato);
    }

    private static QuipuxSettings Settings(string urlLogin) => new()
    {
        Enabled = true,
        UrlLogin = urlLogin,
        UrlRegisterDocument = "https://quipux.test/register",
        UrlValidateStatus = "https://quipux.test/status",
        Username = "u",
        Password = "p",
        ConsumerCode = "1003",
        Bucket = "b",
        AwsAccessKeyId = "k",
        AwsSecretAccessKey = "s",
        OfficerDocumentNumber = "900000000",
    };

    private sealed class QuipuxFakeHandler : HttpMessageHandler
    {
        public string? RegisterBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var esRegistro = request.RequestUri!.AbsolutePath.EndsWith("/register", StringComparison.Ordinal);
            if (esRegistro)
            {
                RegisterBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            }

            var json = esRegistro ? """{"data":{"codigo":81,"descripcion":"ok"}}""" : """{"token":"tok-sin-exp"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
