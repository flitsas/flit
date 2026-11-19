using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

public sealed class GetStatusHistoryHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetStatusHistoryHandler _handler;

    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    public GetStatusHistoryHandlerTests()
    {
        _handler = new GetStatusHistoryHandler(_repo);
    }

    private static ProcedureInstanceStatusHistoryEntry Entry(string to, DateTimeOffset at, string? name = null) =>
        new(Guid.NewGuid(), "borrador", to, at, name is null ? null : Guid.NewGuid(), name, null);

    [Fact]
    public async Task InstanceNotFoundOrOtherTenant_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, Arg.Any<int>(), Arg.Any<int>(), ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>, int)?)null);

        var (result, error) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task MapsItemsAndTotals()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var newer = Entry("preparado", now, "Ana Gestora");
        var older = Entry("borrador", now.AddMinutes(-5));
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[newer, older], 7));

        var (result, error) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Total.Should().Be(7);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.Items.Should().HaveCount(2);
        result.Items[0].ToStatus.Should().Be("preparado");
        result.Items[0].ChangedByName.Should().Be("Ana Gestora");
        result.Items[1].ChangedByName.Should().BeNull();
    }

    [Fact] // Migración V1→V2: el historial muestra el usuario REAL de V1, no el sistema "Migración V1".
    public async Task EventoMigracion_MuestraUsuarioRealDeV1()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        // changed_by resuelve a "Migración V1", pero el metadata conserva el actor real de V1.
        var migrado = new ProcedureInstanceStatusHistoryEntry(
            Guid.NewGuid(), "entregado", "aprobado", now, Guid.NewGuid(), "Migración V1", "Aprobado en RUNT")
        {
            Metadata = """{"origen":"migration_v1","usuario":"DIANA CHACON","usuario_rol":"g_escrituras"}""",
        };
        // Evento nativo con metadata.usuario pero SIN 'origen=migration_v1': NO se altera.
        var nativo = new ProcedureInstanceStatusHistoryEntry(
            Guid.NewGuid(), "borrador", "preparado", now.AddMinutes(-5), Guid.NewGuid(), "Ana Gestora", null)
        {
            Metadata = """{"usuario":"Sistema Interno"}""",
        };
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[migrado, nativo], 2));

        var (result, _) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        result!.Items[0].ChangedByName.Should().Be("DIANA CHACON");  // usuario real, no "Migración V1"
        result.Items[0].ChangedByUserId.Should().NotBeNull();        // se conserva la trazabilidad técnica
        result.Items[1].ChangedByName.Should().Be("Ana Gestora");    // nativo intacto
    }

    [Theory]
    [InlineData(0, 0, 0, 20)]     // defaults: page<1 → 1, pageSize<1 → 20
    [InlineData(3, 10, 20, 10)]   // skip = (page-1)*pageSize
    [InlineData(1, 500, 0, 100)]  // cap de pageSize en 100
    public async Task NormalizesPagination(int page, int pageSize, int expectedSkip, int expectedTake)
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, expectedSkip, expectedTake, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[], 0));

        var (result, error) = await _handler.HandleAsync(InstanceId, TenantId, page, pageSize, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        await _repo.Received(1).GetStatusHistoryPageAsync(InstanceId, TenantId, expectedSkip, expectedTake, ct);
    }

    // ── HU #14173 — colección aditiva de modificaciones de datos ───────────────────────────────

    private static ProcedureInstanceModificacionEntry Modificacion(string etiqueta, DateTimeOffset at, string? nombre = null) =>
        new(Guid.NewGuid(), etiqueta, "campo_test", "anterior", "nuevo", at, nombre is null ? null : Guid.NewGuid(), nombre, null);

    [Fact] // AC1: dos eventos dato_modificado → Modificaciones ordenadas del más reciente al más antiguo
    public async Task AC1_Modificaciones_DosEventos_OrdenDescendenteYCamposCorrectos()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var recent = Modificacion("Nombre propietario", now, "Ana Gestora");
        var older  = Modificacion("Placa", now.AddMinutes(-10));

        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[], 0));
        _repo.GetModificacionesPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[recent, older], 2));

        var (result, error) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Modificaciones.Should().NotBeNull().And.HaveCount(2);
        result.Modificaciones![0].Etiqueta.Should().Be("Nombre propietario");
        result.Modificaciones[0].ChangedAt.Should().Be(now);
        result.Modificaciones[0].ChangedByName.Should().Be("Ana Gestora");
        result.Modificaciones[1].Etiqueta.Should().Be("Placa");
        result.Modificaciones[1].ChangedAt.Should().Be(now.AddMinutes(-10));
        result.Modificaciones[1].ChangedByName.Should().BeNull();
        result.ModificacionesTotal.Should().Be(2);
    }

    [Fact] // AC2: instancia de otro tenant → not_found; GetModificacionesPageAsync no se llama
    public async Task AC2_InstanciaNoEncontrada_NotFoundYSinLlamarModificaciones()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>, int)?)null);

        var (result, error) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
        // No llamó al nuevo método porque la instancia no existe
        await _repo.DidNotReceive().GetModificacionesPageAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), ct);
    }

    [Fact] // Items de estados siguen igual — los dato_modificado no se mezclan en Items
    public async Task Modificaciones_NoSeIncluyen_EnItems()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var estado = Entry("preparado", now, "Ana Gestora");
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[estado], 1));
        _repo.GetModificacionesPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[], 0));

        var (result, _) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        result!.Items.Should().HaveCount(1);
        result.Items[0].ToStatus.Should().Be("preparado");
        // HU #14176: la fila de estado SÍ aparece en Modificaciones como Etiqueta="Estado"
        result.Modificaciones.Should().ContainSingle(m => m.Campo == "estado");
    }

    // ── HU #14176 — cambios de estado visibles en Modificaciones ───────────────────────────────

    [Fact] // AC1: una transición de estado aparece en Modificaciones con Etiqueta="Estado"
    public async Task HU14176_AC1_TransicionEstado_ApareceEnModificaciones_ConEtiquetaEstado()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var entry = new ProcedureInstanceStatusHistoryEntry(
            Guid.NewGuid(), "borrador", "radicado", now, Guid.NewGuid(), "Ana Gestora", null)
        {
            ChangedByCompania = "Empresa Demo",
        };
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[entry], 1));
        _repo.GetModificacionesPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[], 0));

        var (result, error) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        error.Should().BeNull();
        result!.Items.Should().HaveCount(1); // Items no cambia
        var mod = result.Modificaciones.Should().ContainSingle().Subject;
        mod.Etiqueta.Should().Be("Estado");
        mod.Campo.Should().Be("estado");
        mod.Anterior.Should().Be("borrador");
        mod.Nuevo.Should().Be("radicado");
        mod.ChangedAt.Should().Be(now);
        mod.ChangedByName.Should().Be("Ana Gestora");
        mod.ChangedByCompania.Should().Be("Empresa Demo");
    }

    [Fact] // AC1: FromStatus nulo (primera transición) → Anterior vacío, no null
    public async Task HU14176_AC1_PrimeraTransicion_AnteriorVacio()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var entry = new ProcedureInstanceStatusHistoryEntry(
            Guid.NewGuid(), null, "borrador", now, null, null, null);
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[entry], 1));
        _repo.GetModificacionesPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[], 0));

        var (result, _) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        var mod = result!.Modificaciones.Should().ContainSingle().Subject;
        mod.Campo.Should().Be("estado");
        mod.Anterior.Should().Be(string.Empty);
        mod.Nuevo.Should().Be("borrador");
    }

    [Fact] // AC2: si hay un dato_modificado con campo="estado" igual al de status history, aparece una sola vez
    public async Task HU14176_AC2_DatosModificadoEstadoDuplicado_ApareceUnaVezEnModificaciones()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var statusEntry = new ProcedureInstanceStatusHistoryEntry(
            Guid.NewGuid(), "borrador", "radicado", now, null, null, null);
        // dato_modificado con campo "estado" y mismo anterior/nuevo → debe ser descartado
        var duplicateDatoMod = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Estado", "estado", "borrador", "radicado", now.AddSeconds(-5), null, null, null);

        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[statusEntry], 1));
        _repo.GetModificacionesPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[duplicateDatoMod], 1));

        var (result, error) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        error.Should().BeNull();
        // Items sigue teniendo la fila de estado (panel de estados, sin cambiar)
        result!.Items.Should().ContainSingle();
        // Modificaciones tiene EXACTAMENTE una entrada de estado (no duplicada)
        result.Modificaciones!.Where(m => m.Campo == "estado").Should().ContainSingle();
    }

    [Fact] // AC2: dato_modificado con campo distinto de "estado" NO se descarta aunque haya status history
    public async Task HU14176_AC2_DatoModificadoCampoDiferente_NuncaDescartado()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var statusEntry = new ProcedureInstanceStatusHistoryEntry(
            Guid.NewGuid(), "borrador", "radicado", now, null, null, null);
        var placa = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Placa", "placa", "ABC123", "XYZ789", now.AddMinutes(-2), null, null, null);

        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[statusEntry], 1));
        _repo.GetModificacionesPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[placa], 1));

        var (result, _) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        result!.Modificaciones.Should().HaveCount(2);
        result.Modificaciones!.Should().Contain(m => m.Campo == "estado");
        result.Modificaciones.Should().Contain(m => m.Campo == "placa");
    }

    [Fact] // Orden descendente: estado más reciente primero que dato_modificado anterior
    public async Task HU14176_Modificaciones_OrdenadoPorChangedAtDesc()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var statusEntry = new ProcedureInstanceStatusHistoryEntry(
            Guid.NewGuid(), "borrador", "radicado", now, null, null, null);
        var olderMod = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Placa", "placa", "ABC", "XYZ", now.AddMinutes(-10), null, null, null);

        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[statusEntry], 1));
        _repo.GetModificacionesPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[olderMod], 1));

        var (result, _) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        result!.Modificaciones.Should().HaveCount(2);
        result.Modificaciones![0].Campo.Should().Be("estado");   // más reciente primero
        result.Modificaciones[1].Campo.Should().Be("placa");
    }

    // ── HU #14180 — actos ya guardados en la misma línea de Modificaciones ─────────────────────

    [Fact] // AC1: la página muestra reasignación, reenvío y revocatoria con su etiqueta y su campo
    public async Task HU14180_AC1_ActosProyectados_AparecenEnModificacionesConEtiqueta()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.Parse("2026-10-28T15:21:00+00:00");
        var reasignar = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Reasignación de gestor", "reasignacion_gestor", "", "Reasignación de gestor",
            now, Guid.NewGuid(), "Ana Gestora", "Empresa Demo");
        var reenvio = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Reenvío de validación", "reenvio_validacion", "", "Reenvío de validación",
            now.AddMinutes(-1), Guid.NewGuid(), "Ana Gestora", null);
        var solicitada = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Solicitud de revocatoria", "revocatoria", "", "El propietario desistió",
            now.AddMinutes(-2), Guid.NewGuid(), "Ana Gestora", null);
        var aprobada = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Revocatoria aprobada", "revocatoria", "", "Procede la revocatoria",
            now.AddMinutes(-3), null, null, null);
        var rechazada = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Revocatoria rechazada", "revocatoria", "", "Revocatoria rechazada",
            now.AddMinutes(-4), null, null, null);

        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[], 0));
        _repo.GetModificacionesPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[reasignar, reenvio, solicitada, aprobada, rechazada], 5));

        var (result, error) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        error.Should().BeNull();
        result!.Items.Should().BeEmpty();
        result.Modificaciones.Should().HaveCount(5);
        result.Modificaciones!.Should().Contain(m => m.Etiqueta == "Reasignación de gestor" && m.Campo == "reasignacion_gestor");
        result.Modificaciones.Should().Contain(m => m.Etiqueta == "Reenvío de validación" && m.Campo == "reenvio_validacion");
        result.Modificaciones.Should().Contain(m => m.Etiqueta == "Solicitud de revocatoria" && m.Campo == "revocatoria" && m.Nuevo == "El propietario desistió");
        result.Modificaciones.Should().Contain(m => m.Etiqueta == "Revocatoria aprobada" && m.Campo == "revocatoria");
        result.Modificaciones.Should().Contain(m => m.Etiqueta == "Revocatoria rechazada" && m.Campo == "revocatoria");
        result.Modificaciones.Should().OnlyContain(m => m.ChangedAt.Offset == TimeSpan.Zero);
    }

    [Fact] // AC1: el payload del acto ya guardado se proyecta a etiqueta, anterior vacío y frase corta
    public void HU14180_AC1_TryProject_EtiquetaAnteriorVacioYFraseSinCorreo()
    {
        var at = DateTimeOffset.Parse("2026-10-28T15:21:00+00:00");
        var id = Guid.NewGuid();

        StatusHistoryModificacionLectura.TryProject(
            "reasignar_gestor_admin", id, at, Guid.NewGuid(),
            """{"previous_assigned_to_user_id":"11111111-1111-1111-1111-111111111111","new_assigned_to_user_id":"22222222-2222-2222-2222-222222222222"}""",
            out var reasignar).Should().BeTrue();
        reasignar.Etiqueta.Should().Be("Reasignación de gestor");
        reasignar.Campo.Should().Be("reasignacion_gestor");
        reasignar.Anterior.Should().BeEmpty();
        reasignar.Nuevo.Should().Be("Reasignación de gestor");
        reasignar.ChangedAt.Should().Be(at);

        StatusHistoryModificacionLectura.TryProject(
            "reenvio_validacion_admin", Guid.NewGuid(), at, null,
            """{"party_role":"propietario","correo_anterior":"viejo@flit.io","correo_destino":"nuevo@flit.io","email_actualizado":true}""",
            out var reenvio).Should().BeTrue();
        reenvio.Etiqueta.Should().Be("Reenvío de validación");
        reenvio.Campo.Should().Be("reenvio_validacion");
        reenvio.Anterior.Should().BeEmpty();
        reenvio.Nuevo.Should().Be("Reenvío de validación");
        reenvio.Nuevo.Should().NotContain("@");
        reenvio.Anterior.Should().NotContain("@");

        StatusHistoryModificacionLectura.TryProject(
            "revocatoria_solicitada", Guid.NewGuid(), at, null,
            """{"attempt_number":1,"reason":"El propietario desistió","support_document_id":"55555555-5555-5555-5555-555555555555"}""",
            out var solicitada).Should().BeTrue();
        solicitada.Etiqueta.Should().Be("Solicitud de revocatoria");
        solicitada.Campo.Should().Be("revocatoria");
        solicitada.Anterior.Should().BeEmpty();
        solicitada.Nuevo.Should().Be("El propietario desistió");

        StatusHistoryModificacionLectura.TryProject(
            "revocatoria_aprobada", Guid.NewGuid(), at, null,
            """{"attempt_number":1,"decision_reason":"Procede la revocatoria"}""",
            out var aprobada).Should().BeTrue();
        aprobada.Etiqueta.Should().Be("Revocatoria aprobada");
        aprobada.Campo.Should().Be("revocatoria");
        aprobada.Nuevo.Should().Be("Procede la revocatoria");

        StatusHistoryModificacionLectura.TryProject(
            "revocatoria_rechazada", Guid.NewGuid(), at, null,
            """{"attempt_number":2,"decision_reason":null}""",
            out var rechazada).Should().BeTrue();
        rechazada.Etiqueta.Should().Be("Revocatoria rechazada");
        rechazada.Nuevo.Should().Be("Revocatoria rechazada");

        StatusHistoryModificacionLectura.TryProject(
            "revocatoria_solicitada", Guid.NewGuid(), at, null,
            """{"reason":"escribir a ana@flit.io"}""",
            out var conCorreo).Should().BeTrue();
        conCorreo.Nuevo.Should().Be("Solicitud de revocatoria");
        conCorreo.Nuevo.Should().NotContain("@");
    }

    [Fact] // AC1 actor: consulta_runt e identidad sin usuario; el resto no inventa nombre
    public async Task HU14180_AC1_DatoModificadoSinUsuario_ActorDeSistemaSoloEnRuntEIdentidad()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.Parse("2026-10-28T15:21:00+00:00");
        var userId = Guid.NewGuid();
        var runt = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Consulta RUNT", "consulta_runt", "", "Sin alertas", now, null, null, "NoDebeVerse");
        var identidad = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Identidad", "identidad", "", "Validada", now.AddMinutes(-1), null, null, "Tampoco");
        var placa = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Placa", "placa", "ABC", "XYZ", now.AddMinutes(-2), null, null, null);
        var runtConUsuario = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Consulta RUNT", "consulta_runt", "", "Sin alertas", now.AddMinutes(-3),
            userId, "Ana Gestora", "Empresa Demo");

        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[], 0));
        _repo.GetModificacionesPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[runt, identidad, placa, runtConUsuario], 4));

        var (result, _) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        var mods = result!.Modificaciones!;
        mods.Should().ContainSingle(m => m.Campo == "consulta_runt" && m.ChangedByUserId == null)
            .Which.Should().Match<StatusHistoryModificationDto>(m =>
                m.ChangedByName == "Sistema · RUNT" && m.ChangedByCompania == null);
        mods.Should().ContainSingle(m => m.Campo == "identidad")
            .Which.Should().Match<StatusHistoryModificationDto>(m =>
                m.ChangedByName == "Sistema · Validación de identidad" && m.ChangedByCompania == null);
        mods.Should().ContainSingle(m => m.Campo == "placa")
            .Which.Should().Match<StatusHistoryModificationDto>(m =>
                m.ChangedByName == null && m.ChangedByCompania == null);
        mods.Should().ContainSingle(m => m.Campo == "consulta_runt" && m.ChangedByUserId == userId)
            .Which.Should().Match<StatusHistoryModificationDto>(m =>
                m.ChangedByName == "Ana Gestora" && m.ChangedByCompania == "Empresa Demo");
    }

    [Fact] // AC2: el mismo id, o la misma firma, sale una vez; consultar otra vez no agrega filas
    public async Task HU14180_AC2_MismoActo_ApareceUnaVez_YConsultarOtraVezNoDuplica()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.Parse("2026-10-28T15:21:00+00:00");
        var id = Guid.NewGuid();
        var primero = new ProcedureInstanceModificacionEntry(
            id, "Reasignación de gestor", "reasignacion_gestor", "", "Reasignación de gestor",
            now, null, null, null);
        var mismoId = new ProcedureInstanceModificacionEntry(
            id, "Reasignación de gestor", "reasignacion_gestor", "", "Reasignación de gestor",
            now.AddMinutes(-1), null, null, null);
        var mismaFirma = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Reenvío de validación", "reenvio_validacion", "", "Reenvío de validación",
            now, null, null, null);
        var mismaFirmaOtra = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Reenvío de validación", "reenvio_validacion", "", "Reenvío de validación",
            now, null, null, null);
        var otraVez = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(), "Reasignación de gestor", "reasignacion_gestor", "", "Reasignación de gestor",
            now.AddHours(-3), null, null, null);

        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[], 0));
        _repo.GetModificacionesPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[primero, mismoId, mismaFirma, mismaFirmaOtra, otraVez], 5));

        var (first, _) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);
        var (second, _) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        first!.Modificaciones.Should().HaveCount(3);
        var modificaciones = first.Modificaciones!;
        modificaciones.Where(m => m.Id == id).Should().ContainSingle();
        modificaciones.Where(m => m.Campo == "reenvio_validacion").Should().ContainSingle();
        modificaciones.Where(m => m.Campo == "reasignacion_gestor").Should().HaveCount(2);
        second!.Modificaciones.Should().HaveCount(3);
    }

    [Fact] // AC2: el acto y un dato_modificado con el mismo campo y el mismo nuevo quedan en una fila
    public void HU14180_AC2_ActoYDatoModificadoMismoCampoYNuevo_QuedaUnaFila()
    {
        var at = DateTimeOffset.Parse("2026-10-28T15:21:00+00:00");
        var reasignar = Acto("reasignar_gestor_admin", "Reasignación de gestor", "reasignacion_gestor", "", "Reasignación de gestor", at);
        var reasignarDespues = Acto("reasignar_gestor_admin", "Reasignación de gestor", "reasignacion_gestor", "", "Reasignación de gestor", at.AddHours(-2));
        var espejo = Acto("dato_modificado", "Reasignación de gestor", "reasignacion_gestor", "", "Reasignación de gestor", at.AddMinutes(-30), acto: false);
        var solicitud = Acto("revocatoria_solicitada", "Solicitud de revocatoria", "revocatoria", "", "El propietario desistió", at.AddMinutes(-4));
        var espejoRevocatoria = Acto("dato_modificado", "Solicitud de revocatoria", "revocatoria", "", "El propietario desistió", at.AddMinutes(-8), acto: false);
        var placa = Acto("dato_modificado", "Placa", "placa", "ABC", "XYZ", at.AddMinutes(-9), acto: false);

        var once = StatusHistoryModificacionLectura.Dedup(
            [reasignar, reasignarDespues, espejo, solicitud, espejoRevocatoria, placa]);
        var twice = StatusHistoryModificacionLectura.Dedup(once);

        once.Should().HaveCount(4);
        once.Where(r => r.Campo == "reasignacion_gestor").Should().HaveCount(2);
        once.Where(r => r.Campo == "reasignacion_gestor").Should().OnlyContain(r => r.EsActoAdministrativo);
        once.Where(r => r.Campo == "revocatoria").Should().ContainSingle()
            .Which.EsActoAdministrativo.Should().BeTrue();
        once.Should().Contain(r => r.Campo == "placa");
        twice.Should().HaveCount(once.Count);

        var mismoId = reasignar with { };
        var repetido = reasignar with { };
        StatusHistoryModificacionLectura.Dedup([mismoId, repetido]).Should().ContainSingle();

        var otraFirma = reasignar with { Id = Guid.NewGuid() };
        StatusHistoryModificacionLectura.Dedup([reasignar, otraFirma]).Should().ContainSingle();
    }

    private static StatusHistoryModificacionLectura.Fila Acto(
        string tipo,
        string etiqueta,
        string campo,
        string anterior,
        string nuevo,
        DateTimeOffset at,
        bool acto = true) =>
        new(Guid.NewGuid(), tipo, etiqueta, campo, anterior, nuevo, at, null, acto);
}
