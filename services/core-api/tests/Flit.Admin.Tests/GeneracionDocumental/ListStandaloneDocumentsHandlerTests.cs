using Flit.Admin.Application.GeneracionDocumental.List;
using Flit.Admin.Domain.GeneracionDocumental;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12204 — historial tenant-scoped con filtros (CF-17 / CF-18 / CF-20).
///
/// Uso de ejemplo:
/// <code>
/// var page = await new ListStandaloneDocumentsHandler(repo).HandleAsync(
///     new ListStandaloneDocumentsQuery { TenantId = miTenant, DocumentType = "certificado_rues" }, ct);
/// </code>
/// </summary>
public sealed class ListStandaloneDocumentsHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtroTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Autor = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtroAutor = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly FakeStandaloneDocumentRepository _repo = new();

    private ListStandaloneDocumentsHandler Listar => new(_repo);

    // ── CF-17: metadata mínima, sin document_snapshot ───────────────────────────────

    [Fact]
    public async Task Historial_DevuelveLaMetadataMinimaDeCadaFila()
    {
        Sembrar(id: 1, tipo: StandaloneDocumentType.CertificadoRues, status: StandaloneDocumentStatus.Generated);

        var page = await Listar.HandleAsync(Query(), TestContext.Current.CancellationToken);

        page.Total.Should().Be(1);
        var fila = page.Items.Single();
        fila.DocumentType.Should().Be(StandaloneDocumentType.CertificadoRues);
        fila.Status.Should().Be(StandaloneDocumentStatus.Generated);
        fila.CompanyName.Should().NotBeNullOrWhiteSpace();
        fila.CreatedByUserName.Should().NotBeNullOrWhiteSpace();
        fila.CreatedAt.Should().NotBe(default);
    }

    /// <summary>
    /// CF-26: la fila persistida tiene <c>document_snapshot</c> (PII alta) y aun así el listado no
    /// puede filtrarlo — la proyección ni siquiera declara la propiedad. Es un test de contrato del
    /// TIPO, no de un valor: mientras la clase no tenga ese miembro, ningún handler podrá exponerlo.
    /// </summary>
    [Fact]
    public async Task Historial_NoExponeElSnapshotAunqueLaFilaLoTenga()
    {
        Sembrar(
            id: 1,
            tipo: StandaloneDocumentType.TransferenciaDominioGenerada,
            status: StandaloneDocumentStatus.Generated,
            documentSnapshot: "{\"adquirente\":{\"nombre\":\"NOMBRE COMPLETO\",\"direccion\":\"CL 1 # 2-3\"}}");

        var page = await Listar.HandleAsync(Query(), TestContext.Current.CancellationToken);

        var propiedades = typeof(StandaloneDocumentListItem).GetProperties().Select(p => p.Name).ToArray();
        propiedades.Should().NotContain("DocumentSnapshot");
        propiedades.Should().NotContain("RuesSnapshot");
        propiedades.Should().NotContain("StoragePath");
        propiedades.Should().NotContain("StorageSha256");

        page.Items.Should().ContainSingle();
    }

    // ── CF-18: filtros ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Filtro_PorTipoDeDocumento()
    {
        Sembrar(1, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated);
        Sembrar(2, StandaloneDocumentType.TransferenciaDominioGenerada, StandaloneDocumentStatus.Generated);

        var page = await Listar.HandleAsync(
            Query() with { DocumentType = StandaloneDocumentType.CertificadoRues },
            TestContext.Current.CancellationToken);

        page.Items.Should().ContainSingle()
            .Which.DocumentType.Should().Be(StandaloneDocumentType.CertificadoRues);
    }

    [Fact]
    public async Task Filtro_PorRangoDeFechasYPorUsuario_SeAplicanJuntos()
    {
        Sembrar(1, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated,
            creadoEn: Fecha(1), autor: Autor);
        Sembrar(2, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated,
            creadoEn: Fecha(5), autor: Autor);
        Sembrar(3, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated,
            creadoEn: Fecha(5), autor: OtroAutor);

        var page = await Listar.HandleAsync(
            Query() with { DateFrom = Fecha(4), DateTo = Fecha(6), CreatedByUserId = Autor },
            TestContext.Current.CancellationToken);

        page.Items.Should().ContainSingle().Which.CreatedByUserId.Should().Be(Autor);
    }

    /// <summary>
    /// CF-21 / CF-18: «En proceso» es UNA opción de usuario que cubre DOS estados internos. El
    /// cliente los expande y el handler los pasa tal cual; el filtro debe traer ambos.
    /// </summary>
    [Fact]
    public async Task Filtro_EnProceso_CubrePendingYProcessing()
    {
        Sembrar(1, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Pending);
        Sembrar(2, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Processing);
        Sembrar(3, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated);
        Sembrar(4, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Error);

        var page = await Listar.HandleAsync(
            Query() with { Statuses = [StandaloneDocumentStatus.Pending, StandaloneDocumentStatus.Processing] },
            TestContext.Current.CancellationToken);

        page.Items.Select(i => i.Status).Should().BeEquivalentTo(
            [StandaloneDocumentStatus.Pending, StandaloneDocumentStatus.Processing]);
    }

    [Fact]
    public async Task Filtro_ConEstadoDesconocido_SeDescartaYNoVaciaElHistorial()
    {
        Sembrar(1, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated);

        var page = await Listar.HandleAsync(
            Query() with { Statuses = ["archivado"] },
            TestContext.Current.CancellationToken);

        page.Items.Should().ContainSingle(
            "un estado que la columna no admite no puede convertir el historial en cero resultados");
    }

    // ── R3 / CF-20: aislamiento por el filtro de tenant ─────────────────────────────

    [Fact]
    public async Task Historial_NuncaTraeFilasDeOtroTenant()
    {
        Sembrar(1, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated);
        Sembrar(2, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated, tenant: OtroTenant);

        var page = await Listar.HandleAsync(Query(), TestContext.Current.CancellationToken);

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.CompanyName.Should().Contain(Tenant.ToString()[..8]);
    }

    [Fact]
    public async Task UsuarioNormal_QueEnviaTenantIdAjeno_SigueViendoSoloElSuyo()
    {
        Sembrar(1, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated);
        Sembrar(2, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated, tenant: OtroTenant);

        var page = await Listar.HandleAsync(
            Query() with { IsSuperAdmin = false, RequestedTenantId = OtroTenant },
            TestContext.Current.CancellationToken);

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.CompanyName.Should().Contain(Tenant.ToString()[..8]);
    }

    /// <summary>
    /// CF-20: SuperAdmin sí obtiene metadata global con el filtro explícito… y aun así solo
    /// metadata. La proyección no tiene snapshot ni URL firmada que devolver.
    /// </summary>
    [Fact]
    public async Task SuperAdmin_ConTenantIdExplicito_VeMetadataDeOtraCompaniaSinContenido()
    {
        Sembrar(2, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated,
            tenant: OtroTenant, documentSnapshot: "{\"pii\":\"alta\"}");

        var page = await Listar.HandleAsync(
            Query() with { IsSuperAdmin = true, RequestedTenantId = OtroTenant },
            TestContext.Current.CancellationToken);

        var fila = page.Items.Should().ContainSingle().Subject;
        fila.CompanyName.Should().Contain(OtroTenant.ToString()[..8]);
        fila.Should().BeOfType<StandaloneDocumentListItem>();
        typeof(StandaloneDocumentListItem).GetProperties().Select(p => p.Name)
            .Should().NotContain("DocumentSnapshot");
    }

    // ── Listado global de SuperAdmin ────────────────────────────────────────────────
    //
    // Es el UNICO camino del listado sin `WHERE tenant_id`, y en este repo ese WHERE es el
    // aislamiento real entre companias (no hay FORCE ROW LEVEL SECURITY y la app conecta como
    // owner). Por eso los casos de abajo comprueban las dos direcciones: que se abre cuando debe y
    // que no se abre en ningun otro supuesto.

    [Fact]
    public async Task SuperAdmin_QuePideTodas_VeLasDeTodasLasCompanias()
    {
        Sembrar(1, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated);
        Sembrar(2, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated, tenant: OtroTenant);

        var page = await Listar.HandleAsync(
            Query() with { IsSuperAdmin = true, AllTenants = true },
            TestContext.Current.CancellationToken);

        page.Total.Should().Be(2);
        page.Items.Select(i => i.CompanyName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task UsuarioNormal_QuePideTodas_SigueViendoSoloLaSuya()
    {
        Sembrar(1, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated);
        Sembrar(2, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated, tenant: OtroTenant);

        var page = await Listar.HandleAsync(
            Query() with { IsSuperAdmin = false, AllTenants = true },
            TestContext.Current.CancellationToken);

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.CompanyName.Should().Contain(Tenant.ToString()[..8]);
    }

    /// <summary>
    /// Sin pedirlo no se abre: un SuperAdmin que no marca «todas» sigue viendo su propia compania.
    /// Cruzar datos entre companias tiene que ser un acto deliberado, nunca el estado por defecto.
    /// </summary>
    [Fact]
    public async Task SuperAdmin_QueNoPideTodas_NoObtieneElListadoGlobal()
    {
        Sembrar(1, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated);
        Sembrar(2, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated, tenant: OtroTenant);

        var page = await Listar.HandleAsync(
            Query() with { IsSuperAdmin = true },
            TestContext.Current.CancellationToken);

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.CompanyName.Should().Contain(Tenant.ToString()[..8]);
    }

    /// <summary>
    /// Si llegan los dos, «todas» gana: quedarse con la compania concreta devolveria MENOS de lo
    /// pedido sin decirlo.
    /// </summary>
    [Fact]
    public async Task SuperAdmin_ConTodasYTenantIdALaVez_DevuelveTodas()
    {
        Sembrar(1, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated);
        Sembrar(2, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated, tenant: OtroTenant);

        var page = await Listar.HandleAsync(
            Query() with { IsSuperAdmin = true, AllTenants = true, RequestedTenantId = OtroTenant },
            TestContext.Current.CancellationToken);

        page.Total.Should().Be(2);
    }

    /// <summary>
    /// El listado global sigue siendo METADATA: la proyeccion no tiene snapshot que devolver, ni
    /// siquiera cuando la fila es de otra compania.
    /// </summary>
    [Fact]
    public async Task ListadoGlobal_NoExponeSnapshotDeNingunaCompania()
    {
        Sembrar(2, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated,
            tenant: OtroTenant, documentSnapshot: "{\"pii\":\"alta\"}");

        var page = await Listar.HandleAsync(
            Query() with { IsSuperAdmin = true, AllTenants = true },
            TestContext.Current.CancellationToken);

        page.Items.Should().NotBeEmpty();
        typeof(StandaloneDocumentListItem).GetProperties().Select(p => p.Name)
            .Should().NotContain(["DocumentSnapshot", "RuesSnapshot", "StoragePath", "StorageSha256"]);
    }

    // ── Paginación ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Paginacion_DevuelveElTotalCompletoYLaPaginaPedida()
    {
        for (var i = 1; i <= 5; i++)
        {
            Sembrar(i, StandaloneDocumentType.CertificadoRues, StandaloneDocumentStatus.Generated,
                creadoEn: Fecha(i));
        }

        var page = await Listar.HandleAsync(
            Query() with { Page = 2, PageSize = 2 },
            TestContext.Current.CancellationToken);

        page.Total.Should().Be(5);
        page.Page.Should().Be(2);
        page.Items.Should().HaveCount(2);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private static ListStandaloneDocumentsQuery Query() => new() { TenantId = Tenant };

    private static DateTimeOffset Fecha(int dia) =>
        new(2026, 9, dia, 12, 0, 0, TimeSpan.Zero);

    private void Sembrar(
        int id,
        string tipo,
        string status,
        Guid? tenant = null,
        Guid? autor = null,
        DateTimeOffset? creadoEn = null,
        string? documentSnapshot = null)
    {
        _repo.Rows.Add(new StandaloneDocument
        {
            Id = Guid.Parse($"00000000-0000-0000-0000-{id:D12}"),
            TenantId = tenant ?? Tenant,
            CreatedByUserId = autor ?? Autor,
            DocumentType = tipo,
            Scenario = tipo == StandaloneDocumentType.TransferenciaDominioGenerada ? "A" : null,
            Status = status,
            Filename = status == StandaloneDocumentStatus.Generated ? $"documento-{id}.pdf" : null,
            StoragePath = status == StandaloneDocumentStatus.Generated ? $"fm://{id}" : null,
            StorageSha256 = status == StandaloneDocumentStatus.Generated ? new string('a', 64) : null,
            DocumentSnapshot = documentSnapshot,
            CreatedAt = creadoEn ?? Fecha(1),
        });
    }
}
