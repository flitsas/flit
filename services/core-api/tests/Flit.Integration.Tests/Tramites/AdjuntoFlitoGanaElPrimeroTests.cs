using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ExternalAttachments;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using Npgsql;
using NSubstitute;
using System.Text;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13265 (Feature #13261, Épica #12741) — «gana quien carga primero» defendido por el MOTOR (DDL 131, trigger
/// <c>tr_attachments_flito_gana_primero</c>) contra PostgreSQL real, sin sleeps: el orden de las dos cargas se fuerza
/// con la transacción abierta de una y se espera (<c>pg_stat_activity</c>) a que la otra esté bloqueada en el lock del trámite.
/// <para>Uso de ejemplo: con FLITO dentro de su transacción (lock del trámite), el INSERT del gestor desde otra conexión queda
/// esperando; al confirmar FLITO falla con 23505 y <c>ck_attachments_flito_gana_primero</c>.</para>
/// </summary>
public sealed class AdjuntoFlitoGanaElPrimeroTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Tipo = "liquidacion_impuesto";
    private const string UniqueViolation = "23505";
    private static readonly Guid Compania = new("5a5a5a5a-0001-4000-8000-000000013265");
    private static readonly Guid Tramite = new("5a5a5a5a-0002-4000-8000-000000013265");
    private static readonly Guid Gestor = new("5a5a5a5a-0003-4000-8000-000000013265");
    private static readonly byte[] Pdf = "%PDF-1.4 comprobante sintetico 13265"u8.ToArray();

    // ── Carrera 1: FLITO primero (con el lock), el gestor llega segundo ───────────────────────────

    [PostgresFact]
    public async Task AC1_Carrera_FlitoTieneElLock_ElInsertDelGestorEsperaYAlConfirmarFlitoFalla()
    {
        await SembrarAsync("asignado");
        await using var gestorConn = await Fixture.OpenConnectionAsync();
        var gestorPid = await PidAsync(gestorConn);
        var flitoTieneElLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gestorEstaEsperando = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var flitoTask = new ExternalAttachmentRepository(NewContext()).RunLockedAsync(Tramite, Tipo, async (target, writer, ct) =>
        {
            target.Should().NotBeNull();
            flitoTieneElLock.SetResult();
            await gestorEstaEsperando.Task;
            return await writer.ReplaceAsync(new NewExternalAttachment(Tipo, "flito.pdf", "application/pdf", 10, "aa", "fm-flito"), [], ct);
        });
        await flitoTieneElLock.Task;

        var gestorTask = InsertarAsync(gestorConn, provider: null, source: "user");
        await EsperarBloqueoAsync(gestorPid);
        gestorTask.IsCompleted.Should().BeFalse("el gestor espera el lock del trámite que tiene FLITO");
        gestorEstaEsperando.SetResult();

        await flitoTask;
        var rechazo = await Assert.ThrowsAsync<PostgresException>(() => gestorTask);
        rechazo.SqlState.Should().Be(UniqueViolation);
        rechazo.ConstraintName.Should().Be(ExternalAttachmentRules.FirstWinsConstraint);
        (await VigentesAsync()).Should().Equal("flito");
    }

    // ── Carrera 2: el gestor primero (insert sin confirmar), FLITO espera y termina en 409 ─────────

    [PostgresFact]
    public async Task AC1_Carrera_GestorInsertaSinConfirmar_FlitoEsperaYTerminaEnAttachmentExists()
    {
        await SembrarAsync("asignado");
        await using var gestorConn = await Fixture.OpenConnectionAsync();
        await using var gestorTx = await gestorConn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await InsertarAsync(gestorConn, provider: null, source: "user", tx: gestorTx);

        var storage = Substitute.For<IAttachmentStorage>();
        var handler = new SubmitExternalAttachmentHandler(new ExternalAttachmentRepository(NewContext()), storage, Matriz());
        var flitoTask = handler.HandleAsync(Tramite, Tipo, Archivo(), TestContext.Current.CancellationToken);

        await EsperarBloqueoAsync(pid: null, consultaLike: "%FOR NO KEY UPDATE OF pi%");
        flitoTask.IsCompleted.Should().BeFalse("FLITO espera el lock que tiene el insert sin confirmar del gestor");
        await gestorTx.CommitAsync(TestContext.Current.CancellationToken);

        var result = await flitoTask;
        result.Status.Should().Be(SubmitExternalAttachmentStatus.Rejected);
        result.Error.Should().Be("attachment_exists");
        await storage.DidNotReceive().SaveAsync(default, default!, default!, default!, default);
        (await VigentesAsync()).Should().Equal((string?)null);
    }

    // ── El motor como defensa cuando la lectura de FLITO no vio al otro bando ─────────────────────

    [PostgresFact]
    public async Task AC1_FlitoConLecturaObsoleta_ElMotorRechaza_409AttachmentExists_SinMarcaNiBinarioNuevo()
    {
        await SembrarAsync("asignado");
        await using (var conn = await Fixture.OpenConnectionAsync())
        {
            await InsertarAsync(conn, provider: null, source: "user");
        }

        var storage = Substitute.For<IAttachmentStorage>();
        storage.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("fm-nuevo", "bb", Pdf.Length));
        var handler = new SubmitExternalAttachmentHandler(new VistaObsoleta(new ExternalAttachmentRepository(NewContext())), storage, Matriz());

        var result = await handler.HandleAsync(Tramite, Tipo, Archivo(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(SubmitExternalAttachmentStatus.Rejected);
        result.Error.Should().Be("attachment_exists");
        storage.Received(1).Delete("fm-nuevo");
        (await VigentesAsync()).Should().Equal((string?)null);
        (await ContarMarcasAsync()).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC1_Carrera_GestorConLecturaObsoleta_ElMotorRechaza_409AdjuntoBloqueadoFlito_YRetiraElBinarioNuevo()
    {
        await SembrarAsync("borrador");
        await using var flitoConn = await Fixture.OpenConnectionAsync();
        var storage = Substitute.For<IAttachmentStorage>();
        storage.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                // Entre la lectura del gestor (sin adjunto de FLITO) y su guardado, FLITO confirma el suyo.
                await InsertarAsync(flitoConn, provider: "flito", source: "user");
                return new StoredFile("fm-gestor", "cc", Pdf.Length);
            });
        var handler = new UploadAttachmentHandler(new ProcedureInstanceRepository(NewContext()), storage, ValidadorLiquidacion());

        var (result, error) = await handler.HandleAsync(Tramite, Compania, Subida(), Gestor, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be("adjunto_bloqueado_flito");
        storage.Received(1).Delete("fm-gestor");
        (await VigentesAsync()).Should().Equal("flito");
    }

    // ── Reemplazos legítimos de cada lado ────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC4_FlitoReemplazaSuPropioAdjunto_DeleteMasInsert_NoDisparaElMotor()
    {
        await SembrarAsync("asignado");
        await using var conn = await Fixture.OpenConnectionAsync();
        var anterior = await InsertarAsync(conn, provider: "flito", source: "user");

        var nuevo = await new ExternalAttachmentRepository(NewContext()).RunLockedAsync(Tramite, Tipo, (_, writer, ct) =>
            writer.ReplaceAsync(new NewExternalAttachment(Tipo, "v2.pdf", "application/pdf", 10, "dd", "fm-v2"), [anterior], ct));

        (await VigentesAsync()).Should().Equal("flito");
        (await IdsVigentesAsync()).Should().Equal(nuevo);
    }

    [PostgresFact]
    public async Task AC4_GestorReemplazaSuPropioAdjunto_RetiraEInserta_NoDisparaElMotor()
    {
        await SembrarAsync("borrador");
        await using (var conn = await Fixture.OpenConnectionAsync())
        {
            await InsertarAsync(conn, provider: null, source: "user");
        }

        var storage = Substitute.For<IAttachmentStorage>();
        storage.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("fm-gestor-2", "ee", Pdf.Length));
        var handler = new UploadAttachmentHandler(new ProcedureInstanceRepository(NewContext()), storage, ValidadorLiquidacion());

        var (result, error) = await handler.HandleAsync(Tramite, Compania, Subida(), Gestor, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        (await IdsVigentesAsync()).Should().Equal(result!.Id);
        storage.Received(1).Delete("path-previo");
    }

    // ── Alcance exacto del motor ─────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC4_Motor_SoloActuaEnElTipoDelContratoYSoloConVigentes()
    {
        await SembrarAsync("asignado");
        await using var conn = await Fixture.OpenConnectionAsync();

        // Otro tipo: conviven FLITO y el gestor.
        await InsertarAsync(conn, provider: "flito", source: "user", tipo: "factura");
        await InsertarAsync(conn, provider: null, source: "user", tipo: "factura");

        // Histórico de FLITO: no bloquea al gestor; y el gestor histórico no bloquea a FLITO.
        await InsertarAsync(conn, provider: "flito", source: "user", historico: true);
        var gestor = await InsertarAsync(conn, provider: null, source: "user");
        await InsertarAsync(conn, provider: null, source: "user", historico: true);
        (await VigentesAsync()).Should().Equal((string?)null);

        // Un UPDATE que vuelve vigente un adjunto del otro bando también lo rechaza.
        await EjecutarAsync(conn, "UPDATE tramites.procedure_instance_attachments SET is_historico = true WHERE id = @id", gestor);
        await InsertarAsync(conn, provider: "flito", source: "user");
        var historico = await UnoAsync(conn, "SELECT id FROM tramites.procedure_instance_attachments WHERE provider IS NULL AND tipo = 'liquidacion_impuesto' LIMIT 1");
        var rechazo = async () => await EjecutarAsync(conn, "UPDATE tramites.procedure_instance_attachments SET is_historico = false WHERE id = @id", historico);
        (await rechazo.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be(ExternalAttachmentRules.FirstWinsConstraint);
    }

    [PostgresFact]
    public async Task AC1_Motor_ElSentidoInversoYElPortalTambienSeRechazan()
    {
        await SembrarAsync("asignado");
        await using var conn = await Fixture.OpenConnectionAsync();

        // Otro proveedor / portal vigente -> FLITO rechazado por el motor.
        await InsertarAsync(conn, provider: null, source: "portal");
        var flito = async () => await InsertarAsync(conn, provider: "flito", source: "user");
        (await flito.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(UniqueViolation);
    }

    [PostgresFact]
    public async Task AC1_Motor_ElPortalNoPuedeCargarSiFlitoYaCargo_YSiElPortalCargoPrimeroFlitoRecibeAttachmentExists()
    {
        await SembrarAsync("asignado");
        await using var conn = await Fixture.OpenConnectionAsync();
        await InsertarAsync(conn, provider: "flito", source: "user");
        var portal = async () => await InsertarAsync(conn, provider: null, source: "portal");
        (await portal.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be(ExternalAttachmentRules.FirstWinsConstraint);

        // Y al revés, con el recibo del portal primero (sin adjunto de FLITO): FLITO recibe attachment_exists.
        await Fixture.ResetAsync();
        await SembrarAsync("asignado");
        await using var conn2 = await Fixture.OpenConnectionAsync();
        await InsertarAsync(conn2, provider: null, source: "portal");
        var handler = new SubmitExternalAttachmentHandler(
            new ExternalAttachmentRepository(NewContext()), Substitute.For<IAttachmentStorage>(), Matriz());
        var result = await handler.HandleAsync(Tramite, Tipo, Archivo(), TestContext.Current.CancellationToken);
        result.Error.Should().Be("attachment_exists");
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────

    private sealed class VistaObsoleta(IExternalAttachmentRepository inner) : IExternalAttachmentRepository
    {
        public Task<T> RunLockedAsync<T>(
            Guid procedureId, string tipo,
            Func<ExternalAttachmentTarget?, IExternalAttachmentWriter, CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default) =>
            inner.RunLockedAsync(procedureId, tipo, (t, w, ct) => work(t is null ? null : t with { Vigentes = [] }, w, ct), cancellationToken);
    }

    private static IResolvedChecklistMatrixProvider Matriz()
    {
        var matriz = Substitute.For<IResolvedChecklistMatrixProvider>();
        matriz.GetForAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ResolvedChecklistDoc>>([]));
        return matriz;
    }

    private static AttachmentValidator ValidadorLiquidacion()
    {
        var catalog = Substitute.For<IDocumentTypeCatalog>();
        catalog.GetRuleAsync(Tipo, Arg.Any<CancellationToken>())
            .Returns(new DocumentTypeRule(Tipo, ["application/pdf"], 20L * 1024 * 1024));
        return new AttachmentValidator(catalog);
    }

    private static ExternalAttachmentFile Archivo() => new("flito.pdf", "application/pdf", Pdf);

    private static UploadAttachmentInput Subida() =>
        new(Tipo, "gestor.pdf", "application/pdf", Pdf.Length, new MemoryStream(Pdf));

    private static async Task<int> PidAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_backend_pid()", conn);
        return (int)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Espera (sondeo, sin sleeps fijos) a que un backend esté bloqueado en un lock de fila/transacción.</summary>
    private async Task EsperarBloqueoAsync(int? pid, string? consultaLike = null)
    {
        await using var monitor = await Fixture.OpenConnectionAsync();
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < limite)
        {
            await using var cmd = new NpgsqlCommand(
                """
                SELECT count(*) FROM pg_stat_activity
                 WHERE datname = current_database() AND wait_event_type = 'Lock'
                   AND (@pid::int IS NULL OR pid = @pid::int)
                   AND (@like::text IS NULL OR query LIKE @like::text)
                """, monitor);
            cmd.Parameters.AddWithValue("pid", (object?)pid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("like", (object?)consultaLike ?? DBNull.Value);
            if ((long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))! > 0)
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("Ningún backend llegó a esperar el lock del trámite.");
    }

    private static async Task<Guid> InsertarAsync(
        NpgsqlConnection conn, string? provider, string source, string tipo = Tipo, bool historico = false, NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO tramites.procedure_instance_attachments
                (tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path, source, provider, is_historico, uploaded_at)
            VALUES (@tenant, @id, @tipo, 'x.pdf', 'application/pdf', 10, 'aa', 'path-previo', @source, @provider, @historico, now())
            RETURNING id
            """, conn, tx);
        cmd.Parameters.AddWithValue("tenant", Compania);
        cmd.Parameters.AddWithValue("id", Tramite);
        cmd.Parameters.AddWithValue("tipo", tipo);
        cmd.Parameters.AddWithValue("source", source);
        cmd.Parameters.AddWithValue("provider", (object?)provider ?? DBNull.Value);
        cmd.Parameters.AddWithValue("historico", historico);
        return (Guid)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task EjecutarAsync(NpgsqlConnection conn, string sql, Guid id)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> UnoAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (Guid)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Proveedores de los adjuntos vigentes del tipo (null = el gestor/portal), vistos desde otra conexión.</summary>
    private async Task<List<string?>> VigentesAsync()
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT provider FROM tramites.procedure_instance_attachments WHERE procedure_instance_id = @id AND tipo = @tipo AND is_historico = false ORDER BY uploaded_at, id", conn);
        cmd.Parameters.AddWithValue("id", Tramite);
        cmd.Parameters.AddWithValue("tipo", Tipo);
        var lista = new List<string?>();
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            lista.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }

        return lista;
    }

    private async Task<List<Guid>> IdsVigentesAsync()
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM tramites.procedure_instance_attachments WHERE procedure_instance_id = @id AND tipo = @tipo AND is_historico = false", conn);
        cmd.Parameters.AddWithValue("id", Tramite);
        cmd.Parameters.AddWithValue("tipo", Tipo);
        var lista = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            lista.Add(reader.GetGuid(0));
        }

        return lista;
    }

    private async Task<long> ContarMarcasAsync()
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM tramites.procedure_instance_field_values WHERE procedure_instance_id = @id AND field_key = 'impuesto_departamental_pagado'", conn);
        cmd.Parameters.AddWithValue("id", Tramite);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task SembrarAsync(string estado)
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Compania, "IT-13265", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13265@flit.test",
                DisplayName = "Gestor sintético 13265",
                Status = "active",
                HomeTenantId = Compania,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var conn = await Fixture.OpenConnectionAsync();
        foreach (var sql in new[]
                 {
                     """
                     INSERT INTO tramites.procedure_instances
                         (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
                     VALUES (@id, @tenant, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1),
                             'IT-13265', 'borrador', 'SINTVIN13265', @gestor, now())
                     """,
                     "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, 'preasignacion')",
                     "UPDATE tramites.procedure_instances SET status = @estado WHERE id = @id",
                 })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", Tramite);
            cmd.Parameters.AddWithValue("tenant", Compania);
            cmd.Parameters.AddWithValue("estado", estado);
            cmd.Parameters.AddWithValue("gestor", Gestor);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
