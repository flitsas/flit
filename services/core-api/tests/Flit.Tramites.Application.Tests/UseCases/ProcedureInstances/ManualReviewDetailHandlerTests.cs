using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13297 (Feature #13282 C2, Épica #13202) — <see cref="GetManualDetailHandler"/> y <see cref="GetManualImageHandler"/>:
/// proyección del contrato <c>ManualDetail</c>, entrega de imágenes con su Content-Type real, 404 por kind inválido o imagen
/// ausente y auditoría <c>manual_imagenes_consultadas</c> (usuario y recurso; sin PII ni rutas de storage).
/// <para>Uso: <c>await new GetManualImageHandler(repo, storage, audit).HandleAsync(new GetManualImageQuery(id, "rostro", user), ct)</c>.</para>
/// </summary>
public sealed class ManualReviewDetailHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Id = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Tenant = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private const string RutaOpaca = "fm-ruta-opaca-secreta";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IManualIdentityReviewReadRepository _repo = Substitute.For<IManualIdentityReviewReadRepository>();
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static ManualIdentityReviewDetailRow Fila(
        string status = BiometricEstados.PendienteRevisionManual, bool imagenes = true) => new(
        Id, Tenant, null, null, "Ana Gómez", "1001", "Compañía A", "prevalidacion", status, Now.AddMinutes(-90),
        Now.AddMinutes(-80), "v1", imagenes, imagenes, imagenes, imagenes, null, null, null, null, Now.AddMinutes(-90));

    [Fact]
    public async Task Detalle_mapea_el_contrato_y_calcula_la_espera()
    {
        _repo.GetDetailAsync(Id, Arg.Any<CancellationToken>()).Returns(Fila());
        var handler = new GetManualDetailHandler(_repo, _audit, new FixedTime(Now));

        var (result, error) = await handler.HandleAsync(new GetManualDetailQuery(Id, User), Ct);

        error.Should().BeNull();
        result!.Status.Should().Be("pendiente_revision_manual");
        result.WaitingMinutes.Should().Be(90);
        result.ConsentTextVersion.Should().Be("v1");
        result.Images.Select(i => i.Kind).Should().Equal("rostro", "anverso", "reverso", "firma");
        result.Images.Should().OnlyContain(i => i.Available);
    }

    [Fact]
    public async Task Detalle_inexistente_responde_not_found_y_no_audita()
    {
        _repo.GetDetailAsync(Id, Arg.Any<CancellationToken>()).Returns((ManualIdentityReviewDetailRow?)null);

        var (result, error) = await new GetManualDetailHandler(_repo, _audit).HandleAsync(new GetManualDetailQuery(Id, User), Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
        await _audit.DidNotReceiveWithAnyArgs().LogAsync(default!, Ct);
    }

    [Fact]
    public async Task Detalle_audita_con_usuario_y_sin_pii()
    {
        _repo.GetDetailAsync(Id, Arg.Any<CancellationToken>()).Returns(Fila());
        IdentityValidationAuditEntry? entrada = null;
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(e => entrada = e), Arg.Any<CancellationToken>());

        await new GetManualDetailHandler(_repo, _audit).HandleAsync(new GetManualDetailQuery(Id, User), Ct);

        entrada.Should().NotBeNull();
        entrada!.Stage.Should().Be("manual_imagenes_consultadas");
        entrada.TenantId.Should().Be(Tenant);
        entrada.ValidationId.Should().Be(Id);
        entrada.Detail.Should().Be($"usuario={User}; recurso=detalle");
        (entrada.Message + entrada.Detail).Should().NotContain("Ana").And.NotContain("1001");
    }

    [Theory]
    [InlineData("selfie")]
    [InlineData("")]
    [InlineData("../rostro")]
    public async Task Imagen_con_kind_invalido_responde_not_found_sin_consultar_el_repositorio(string kind)
    {
        var (result, error) = await new GetManualImageHandler(_repo, _storage, _audit)
            .HandleAsync(new GetManualImageQuery(Id, kind, User), Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Imagen_sin_ruta_en_el_ciclo_actual_responde_not_found_y_no_audita()
    {
        _repo.GetImageRefAsync(Id, "rostro", Arg.Any<CancellationToken>())
            .Returns(new ManualIdentityImageRef(Id, Tenant, null, null, null));

        var (_, error) = await new GetManualImageHandler(_repo, _storage, _audit).HandleAsync(new GetManualImageQuery(Id, "rostro", User), Ct);

        error.Should().Be("not_found");
        await _audit.DidNotReceiveWithAnyArgs().LogAsync(default!, Ct);
    }

    [Fact]
    public async Task Imagen_entrega_los_bytes_con_su_content_type_y_audita_sin_la_ruta()
    {
        _repo.GetImageRefAsync(Id, "firma", Arg.Any<CancellationToken>())
            .Returns(new ManualIdentityImageRef(Id, Tenant, null, "comprador", RutaOpaca));
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
        _storage.OpenReadAsync(RutaOpaca, Arg.Any<CancellationToken>()).Returns(new MemoryStream(png));
        IdentityValidationAuditEntry? entrada = null;
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(e => entrada = e), Arg.Any<CancellationToken>());

        var (result, error) = await new GetManualImageHandler(_repo, _storage, _audit)
            .HandleAsync(new GetManualImageQuery(Id, "FIRMA", User), Ct);

        error.Should().BeNull();
        result!.ContentType.Should().Be("image/png");
        result.Content.Should().Equal(png);
        entrada!.Stage.Should().Be("manual_imagenes_consultadas");
        entrada.PartyRole.Should().Be("comprador");
        entrada.Detail.Should().Be($"usuario={User}; recurso=imagen:firma");
        (entrada.Message + entrada.Detail).Should().NotContain(RutaOpaca);
    }

    [Fact]
    public async Task Imagen_cuyo_binario_no_existe_responde_not_found_y_no_audita()
    {
        _repo.GetImageRefAsync(Id, "rostro", Arg.Any<CancellationToken>())
            .Returns(new ManualIdentityImageRef(Id, Tenant, null, null, RutaOpaca));
        _storage.OpenReadAsync(RutaOpaca, Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var (_, error) = await new GetManualImageHandler(_repo, _storage, _audit).HandleAsync(new GetManualImageQuery(Id, "rostro", User), Ct);

        error.Should().Be("not_found");
        await _audit.DidNotReceiveWithAnyArgs().LogAsync(default!, Ct);
    }
}
