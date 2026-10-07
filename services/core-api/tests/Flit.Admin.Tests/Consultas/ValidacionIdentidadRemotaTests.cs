using System.Text;
using Flit.Api.Consultas;
using Flit.Consultas.Grpc.V1;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Consultas;

/// <summary>
/// HU #13351/#13348 (Epic #13316, ADR-0065 §6-7) — Trámites crea, consulta y
/// descarga sus validaciones de Kyverum a través de core-consultas (que se queda con el secreto) y aplica el aviso que
/// le llega por el bus. core-consultas se reemplaza por un CallInvoker falso.
/// </summary>
public sealed class ValidacionIdentidadRemotaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Iniciar_VaAConsultasConLaEmpresaYElIdDeLaValidacion_YNoTraeSecreto()
    {
        var invoker = new InvokerFalso(_ => new IniciarValidacionResponse
        {
            VerificationId = "kyv_9", CaptureUrl = "https://verify/s/1", ProviderStatus = "pending", RawPayloadSanitized = "{}",
        });
        var validacion = Guid.NewGuid();
        var tramite = Guid.NewGuid();

        var r = await new KyverumVerifyPorConsultas(Cliente(invoker), Db()).StartVerificationAsync(
            new KyverumVerifyStartRequest(tramite, validacion, "comprador", "Ana", "CC", "1", "a@b.co", Empresa), Ct);

        r.VerificationId.Should().Be("kyv_9");
        r.WebhookSecret.Should().BeEmpty("el secreto se queda en Consultas: Trámites no guarda ninguno");
        var pedido = invoker.Pedidos.Should().ContainSingle().Which.Should().BeOfType<IniciarValidacionRequest>().Subject;
        pedido.ValidacionId.Should().Be(validacion.ToString());
        pedido.TramiteId.Should().Be(tramite.ToString());
        pedido.Parte.Should().Be("comprador");
        invoker.Empresas.Should().ContainSingle().Which.Should().Be(Empresa.ToString());
    }

    [Theory]
    [InlineData(StatusCode.Unavailable, true)]
    [InlineData(StatusCode.DeadlineExceeded, true)]
    [InlineData(StatusCode.FailedPrecondition, false)]
    public async Task Iniciar_ConConsultasFallando_SeTraduceAlErrorDelProveedor_TransitorioOSiNo(StatusCode estado, bool transitorio)
    {
        var invoker = new InvokerFalso(_ => throw new RpcException(new Status(estado, "x")));

        var iniciar = () => new KyverumVerifyPorConsultas(Cliente(invoker), Db()).StartVerificationAsync(
            new KyverumVerifyStartRequest(null, Guid.NewGuid(), null, "Ana", "CC", "1", "a@b.co", Empresa), Ct);

        (await iniciar.Should().ThrowAsync<KyverumVerifyException>()).Which.Transient.Should().Be(transitorio);
    }

    [Fact]
    public async Task ConsultarEstado_TomaLaEmpresaDeLaValidacionGuardada()
    {
        var db = Db();
        Sembrar(db, "kyv_7");
        var invoker = new InvokerFalso(_ => new ConsultarEstadoValidacionResponse { Encontrada = true, Status = "aprobado", Score = 90, RawPayloadSanitized = "{}" });

        var estado = await new KyverumVerifyPorConsultas(Cliente(invoker), db).GetStatusAsync("kyv_7", "comprador", Ct);

        estado!.Status.Should().Be("aprobado");
        estado.Score.Should().Be(90);
        invoker.Empresas.Should().ContainSingle().Which.Should().Be(Empresa.ToString());
    }

    [Fact]
    public async Task ConsultarEstado_QueConsultasNoEncuentra_EsNull()
    {
        var db = Db();
        Sembrar(db, "kyv_8");
        var invoker = new InvokerFalso(_ => new ConsultarEstadoValidacionResponse { Encontrada = false });

        (await new KyverumVerifyPorConsultas(Cliente(invoker), db).GetStatusAsync("kyv_8", null, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Certificado_LlegaDesdeConsultas()
    {
        var db = Db();
        Sembrar(db, "kyv_c");
        var invoker = new InvokerFalso(_ => new DescargarCertificadoResponse
        {
            Encontrado = true, Contenido = ByteString.CopyFromUtf8("%PDF"), ContentType = "application/pdf", NombreArchivo = "c.pdf",
        });

        var certificado = await new KyverumCertificadoPorConsultas(Cliente(invoker), db).DownloadCertificateAsync("kyv_c", Ct);

        Encoding.UTF8.GetString(certificado!.Content).Should().Be("%PDF");
        certificado.FileName.Should().Be("c.pdf");
    }

    [Fact]
    public void SinElBusDeTramites_NoArranca()
    {
        var registrar = () => new ServiceCollection().AddConsultasRemoto(Config(new() { ["Tramites:Bus:Habilitado"] = "false" }));

        registrar.Should().Throw<InvalidOperationException>().WithMessage("*Tramites:Bus:Habilitado*");
    }

    [Fact]
    public async Task KyverumVaPorConsultas_YSeEscuchanLosAvisos()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Db());
        services.AddSingleton(Substitute.For<IKyverumVerifyClient>());
        services.AddSingleton(Substitute.For<IKyverumCertificateClient>());

        services.AddConsultasRemoto(Config(new() { ["Tramites:Bus:Habilitado"] = "true" }));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IKyverumVerifyClient>().Should().BeOfType<KyverumVerifyPorConsultas>();
        scope.ServiceProvider.GetRequiredService<IKyverumCertificateClient>().Should().BeOfType<KyverumCertificadoPorConsultas>();
        services.Should().Contain(d => d.ServiceType == typeof(IHostedService), "el consumidor de avisos corre como servicio en segundo plano");
    }

    [Fact]
    public async Task ElConsumidor_IgnoraLosAvisosDeOtroServicio_YDescartaLosDeUnaValidacionQueNoExiste()
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        var consumidor = new AvisoKyverumConsumer(Handler(repo), NullLogger<AvisoKyverumConsumer>.Instance);
        var sobre = new EventEnvelope(Guid.CreateVersion7(), AvisoKyverumConsumer.Tipo, 1, DateTimeOffset.UtcNow, Empresa, "consultas", "c",
            System.Text.Json.JsonSerializer.SerializeToElement(new { }));

        await consumidor.HandleAsync(sobre, new AvisoKyverumVerify(Guid.NewGuid(), "kyv", "comparendos", Guid.NewGuid(), "{}"), Ct);
        await repo.DidNotReceiveWithAnyArgs().GetBiometricByIdAsync(default, TestContext.Current.CancellationToken);

        var desconocida = () => consumidor.HandleAsync(sobre, new AvisoKyverumVerify(Guid.NewGuid(), "kyv", "tramites", Guid.NewGuid(), "{}"), Ct);
        await desconocida.Should().NotThrowAsync("reintentar no arregla una validación que no existe");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static KyverumWebhookHandler Handler(IProcedureInstanceRepository repo) => new(
        repo, Substitute.For<IWebhookSecretProtector>(), Substitute.For<IKyverumVerifyClient>(),
        new IdentityValidationResultApplier(Substitute.For<IIdentityValidationEventPublisher>()),
        Substitute.For<IIdentityValidationAuditLog>(), NullLogger<KyverumWebhookHandler>.Instance);

    private static IConfiguration Config(Dictionary<string, string?> extra)
    {
        var valores = new Dictionary<string, string?>
        {
            ["Consultas:Remoto:Address"] = "http://core-consultas:8084",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://gateway/connect/token",
            ["Platform:ServiceClient:ClientId"] = "svc-tramites",
            ["Platform:ServiceClient:ClientSecret"] = "secreto",
            ["Platform:Messaging:Producer"] = "tramites",
            ["Platform:Messaging:ConnectionString"] = "amqp://tramites:x@127.0.0.1:5672/flit",
        };
        foreach (var (k, v) in extra)
            valores[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    private static ValidacionIdentidadService.ValidacionIdentidadServiceClient Cliente(CallInvoker invoker) => new(invoker);

    private static FlitDbContext Db() =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase($"flit-13351-{Guid.NewGuid()}").Options);

    private static void Sembrar(FlitDbContext db, string verificationId)
    {
        db.Set<ProcedureInstanceBiometricValidation>().Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Empresa,
            PartyRole = "comprador",
            Name = "Ana",
            DocumentType = "CC",
            DocumentNumber = "1",
            Email = "a@b.co",
            Status = BiometricEstados.EnProceso,
            Provider = BiometricProviders.Kyverum,
            KyverumVerificationId = verificationId,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    /// <summary>Hace de core-consultas: guarda lo que le piden y responde (o falla) con lo que le indiquen.</summary>
    private sealed class InvokerFalso(Func<object, object> responder) : CallInvoker
    {
        public List<object> Pedidos { get; } = [];

        public List<string?> Empresas { get; } = [];

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Pedidos.Add(request);
            Empresas.Add(options.Headers?.GetValue("x-flit-tenant-id"));
            var respuesta = Task.Run(() => (TResponse)responder(request));
            return new AsyncUnaryCall<TResponse>(respuesta, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
