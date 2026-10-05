using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Admin.Domain.Integrations;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.MarcaBlanca;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13263 (Feature #13261, Épica #12741) de punta a punta: host HTTP real (<see cref="MarcaBlancaApiFactory"/>) contra
/// Postgres con todas las migraciones, pase externo real y el file-manager como doble en memoria. Comprueba lo que los
/// dobles no pueden: el alcance real del trámite, el estado de la fila (<c>source</c>, <c>provider</c>, retiro del anterior),
/// el bloqueo de fila bajo concurrencia, la matriz documental real y la bitácora real. Usa el PDF sintético de ~3 MB de
/// <c>Tramites/Fixtures</c> (sin datos reales).
/// </summary>
public sealed class ExternalAttachmentEndToEndTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly string[] Textos = ["uno", "uno", "uno", "dos", "tres", "cuatro"];
    private const string Tipo = "liquidacion_impuesto";
    private static readonly Guid Compania = new("5a5a5a5a-0001-4000-8000-000000013263");
    private static readonly Guid Gestor = new("5a5a5a5a-0003-4000-8000-000000013263");
    private static readonly string FixtureSha = "5d4bee9ef5c46aa93ca3e8547f734c9ba0fc4369dbc15d9da09782d734306098";

    [PostgresFact]
    public async Task AC1_ElComprobanteQuedaComoCargaDeUsuarioConProveedorFlitoSinTocarNadaMas()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync("asignado");
        var pdf = await LeerFixtureAsync();
        pdf.Length.Should().BeGreaterThan(2_000_000, "el PDF sintético pesa ~3 MB");
        Convert.ToHexStringLower(SHA256.HashData(pdf)).Should().Be(FixtureSha);

        await using var almacen = new AlmacenFactory(Fixture);
        var response = await Post(almacen, tramite, pdf);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var body = await Json(response);
        body.GetProperty("tipo").GetString().Should().Be(Tipo);
        body.GetProperty("sha256").GetString().Should().Be(FixtureSha);
        body.GetProperty("reemplazoDe").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("pagadoMarcado").GetBoolean().Should().BeFalse();

        var filas = await FilasAsync(tramite);
        var fila = filas.Should().ContainSingle("no se genera nada más (sin OCR ni consolidado)").Subject;
        fila.Id.Should().Be(body.GetProperty("adjuntoId").GetGuid());
        fila.Tipo.Should().Be(Tipo);
        fila.Source.Should().Be("user");
        fila.Provider.Should().Be("flito");
        fila.Sha256.Should().Be(FixtureSha);
        fila.Mimetype.Should().Be("application/pdf");
        fila.SizeBytes.Should().Be(pdf.Length);
        fila.StoragePath.Should().Be("fm-1");
        fila.UploadedBy.Should().BeNull("lo envía un cliente de integración, no un usuario");
        almacen.Almacen.Guardados.Should().HaveCount(1);
    }

    [PostgresFact]
    public async Task AC5_ElMismoArchivoEsIdempotenteYOtroArchivoReemplazaRetirandoElAnterior()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync("asignado");
        var pdf = await LeerFixtureAsync();
        await using var almacen = new AlmacenFactory(Fixture);

        var primero = await Json(await Post(almacen, tramite, pdf));
        var repetido = await Post(almacen, tramite, pdf);

        repetido.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpoRepetido = await Json(repetido);
        cuerpoRepetido.GetProperty("adjuntoId").GetGuid().Should().Be(primero.GetProperty("adjuntoId").GetGuid());
        cuerpoRepetido.GetProperty("sha256").GetString().Should().Be(FixtureSha);
        cuerpoRepetido.GetProperty("reemplazoDe").ValueKind.Should().Be(JsonValueKind.Null);
        (await FilasAsync(tramite)).Should().HaveCount(1);
        almacen.Almacen.Guardados.Should().HaveCount(1, "el 200 idempotente no vuelve a subir ni a escribir nada");

        byte[] otro = [.. pdf.Take(1000), .. "corregido"u8.ToArray()];
        var reemplazo = await Post(almacen, tramite, otro);

        reemplazo.StatusCode.Should().Be(HttpStatusCode.Created);
        var cuerpo = await Json(reemplazo);
        cuerpo.GetProperty("reemplazoDe").GetGuid().Should().Be(primero.GetProperty("adjuntoId").GetGuid());
        var filas = await FilasAsync(tramite);
        filas.Should().ContainSingle().Which.Id.Should().Be(cuerpo.GetProperty("adjuntoId").GetGuid(), "el anterior se retiró");
        almacen.Almacen.Borrados.Should().ContainSingle("el binario anterior se retira tras confirmar").Which.Should().Be("fm-1");
    }

    [PostgresFact]
    public async Task AC4_ElAdjuntoDelGestorGanaAunConElMismoSha256YSeConserva()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync("entregado");
        var pdf = await LeerFixtureAsync();
        var delGestor = await InsertarAdjuntoAsync(tramite, provider: null, sha: FixtureSha, path: "fm-gestor");
        await using var almacen = new AlmacenFactory(Fixture);

        var response = await Post(almacen, tramite, pdf);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response)).GetProperty("code").GetString().Should().Be("attachment_exists");
        (await FilasAsync(tramite)).Should().ContainSingle().Which.Id.Should().Be(delGestor);
        almacen.Almacen.Guardados.Should().BeEmpty();
        almacen.Almacen.Borrados.Should().BeEmpty();
    }

    [PostgresTheory]
    [InlineData("preasignacion", false, 201, null)]
    [InlineData("asignado", false, 201, null)]
    [InlineData("entregado", false, 201, null)]
    [InlineData("rechazado", true, 201, null)]
    [InlineData("rechazado", false, 409, false)]
    [InlineData("borrador", false, 409, false)]
    [InlineData("preparado", false, 409, false)]
    [InlineData("aprobado", false, 409, true)]
    [InlineData("anulado", false, 409, true)]
    [InlineData("revocado", false, 409, true)]
    public async Task AC3_SoloLosEstadosPermitidosArchivanYElRestoResponde409ConTerminal(
        string estado, bool subsanacion, int esperado, bool? terminal)
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync(estado, subsanacion);
        await using var almacen = new AlmacenFactory(Fixture);

        var response = await Post(almacen, tramite, "%PDF-1.4 estado"u8.ToArray());

        ((int)response.StatusCode).Should().Be(esperado);
        if (terminal is { } t)
        {
            var body = await Json(response);
            body.GetProperty("code").GetString().Should().Be("not_allowed_in_state");
            body.GetProperty("estado").GetString().Should().Be(estado);
            body.GetProperty("terminal").GetBoolean().Should().Be(t);
            (await FilasAsync(tramite)).Should().BeEmpty("no se archiva");
            almacen.Almacen.Guardados.Should().BeEmpty();
        }
        else
        {
            (await FilasAsync(tramite)).Should().ContainSingle();
        }
    }

    [PostgresFact]
    public async Task AC6_ElAlcanceEsElDelFeedNuncaRadicadoMigradoOEliminadoSon404()
    {
        await SembrarAsync();
        var nuncaRadicado = await RadicadoAsync("asignado", radicado: false);
        var migrado = await RadicadoAsync("asignado", migrado: true);
        var eliminado = await RadicadoAsync("asignado", eliminado: true);
        await using var almacen = new AlmacenFactory(Fixture);

        foreach (var id in new[] { nuncaRadicado, migrado, eliminado, Guid.CreateVersion7() })
        {
            var response = await Post(almacen, id, "%PDF-1.4 alcance"u8.ToArray());

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Json(response)).GetProperty("code").GetString().Should().Be("procedure_not_found");
        }

        (await FilasAsync(nuncaRadicado)).Should().BeEmpty();
        (await FilasAsync(migrado)).Should().BeEmpty();
        (await FilasAsync(eliminado)).Should().BeEmpty();
        almacen.Almacen.Guardados.Should().BeEmpty();
    }

    [PostgresFact]
    public async Task AC6_SinElPermisoDeEscrituraEs403YNoEscribeNada()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync("asignado");
        await using var almacen = new AlmacenFactory(Fixture);

        var response = await Post(almacen, tramite, "%PDF-1.4 permiso"u8.ToArray(), [ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead]);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response)).GetProperty("code").GetString().Should().Be("insufficient_scope");
        (await FilasAsync(tramite)).Should().BeEmpty();
    }

    [PostgresFact]
    public async Task AC6_ElAlmacenamientoCaidoEs503YElClienteConservaSuAdjunto()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync("asignado");
        await using var almacen = new AlmacenFactory(Fixture);
        var anterior = await Json(await Post(almacen, tramite, "%PDF-1.4 uno"u8.ToArray()));
        almacen.Almacen.Falla = new HttpRequestException("file-manager caído");

        var response = await Post(almacen, tramite, "%PDF-1.4 dos"u8.ToArray());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response)).GetProperty("code").GetString().Should().Be("storage_unavailable");
        (await FilasAsync(tramite)).Should().ContainSingle().Which.Id.Should().Be(anterior.GetProperty("adjuntoId").GetGuid());
        almacen.Almacen.Borrados.Should().BeEmpty();
    }

    [PostgresFact]
    public async Task AC6_CadaLlamadaQuedaEnLaBitacoraComoEscrituraConDatosPersonalesSinElArchivo()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync("asignado");
        await using var almacen = new AlmacenFactory(Fixture);

        var cliente = Cliente(almacen, [ExternalScopes.AttachmentsWrite]);
        cliente.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.263");
        using var content = Multipart(Tipo, "%PDF-1.4 bitacora"u8.ToArray());
        (await cliente.PostAsync($"/api/v1/external/tramites/{tramite}/adjuntos", content, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT client_id, endpoint, items_count, tenant_ids, pii_unmasked, http_status, sync_version_from "
            + "FROM integrations.external_access_log WHERE endpoint = 'tramites.adjunto-envio'",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue("la solicitud quedó registrada");
        reader.GetString(0).Should().Be("flito-it");
        reader.GetInt32(2).Should().Be(1);
        reader.GetFieldValue<Guid[]>(3).Should().Equal(Compania);
        reader.GetBoolean(4).Should().BeTrue("es una escritura con datos personales");
        reader.GetInt32(5).Should().Be(201);
        reader.IsDBNull(6).Should().BeTrue();
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeFalse("una fila por solicitud");
    }

    [PostgresFact]
    public async Task EnMatriz_SeLeeDeLaMatrizDocumentalRealDelTipoDeTramite()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync("asignado");
        await EjecutarAsync(
            "DELETE FROM tramites.procedure_document_requirements WHERE procedure_type_id = (SELECT procedure_type_id FROM tramites.procedure_instances WHERE id = @id)",
            ("id", tramite));
        await using var almacen = new AlmacenFactory(Fixture);

        var sinMatriz = await Json(await Post(almacen, tramite, "%PDF-1.4 sin matriz"u8.ToArray()));
        await EjecutarAsync(
            "INSERT INTO tramites.procedure_document_requirements (procedure_type_id, document_type_id, is_mandatory, default_sort_order) "
            + "SELECT pi.procedure_type_id, d.id, false, 1 FROM tramites.procedure_instances pi, tramites.document_types d "
            + "WHERE pi.id = @id AND d.code = 'liquidacion_impuesto'",
            ("id", tramite));
        var conMatriz = await Json(await Post(almacen, tramite, "%PDF-1.4 con matriz"u8.ToArray()));

        sinMatriz.GetProperty("enMatriz").GetBoolean().Should().BeFalse();
        conMatriz.GetProperty("enMatriz").GetBoolean().Should().BeTrue();
    }

    [PostgresTheory]
    [InlineData("asignado")]
    [InlineData("entregado")]
    public async Task PagadoMarcado_ReflejaLaMarcaVigenteDelConsumidorYNoLaEscribe(string estado)
    {
        await SembrarAsync();
        var conMarca = await RadicadoAsync("borrador");
        var sinMarca = await RadicadoAsync(estado);
        var marcaDelGestor = await RadicadoAsync("borrador");
        await SembrarMarcaAsync(conMarca, source: "flito");
        await SembrarMarcaAsync(marcaDelGestor, source: "user");
        foreach (var id in (Guid[])[conMarca, marcaDelGestor])
        {
            await EjecutarAsync("UPDATE tramites.procedure_instances SET status = @estado WHERE id = @id", ("id", id), ("estado", estado));
        }

        await using var almacen = new AlmacenFactory(Fixture);

        (await Json(await Post(almacen, conMarca, "%PDF-1.4 a"u8.ToArray()))).GetProperty("pagadoMarcado").GetBoolean()
            .Should().BeTrue("la marca vigente del consumidor se devuelve, también en entregado");
        (await Json(await Post(almacen, sinMarca, "%PDF-1.4 b"u8.ToArray()))).GetProperty("pagadoMarcado").GetBoolean()
            .Should().BeFalse("esta HU no escribe la marca");
        (await Json(await Post(almacen, marcaDelGestor, "%PDF-1.4 c"u8.ToArray()))).GetProperty("pagadoMarcado").GetBoolean()
            .Should().BeFalse("la marca puesta por el gestor no es la del consumidor");
        (await EscalarAsync(
            "SELECT count(*) FROM tramites.procedure_instance_field_values WHERE procedure_instance_id = @id AND field_key = 'impuesto_departamental_pagado'",
            ("id", sinMarca))).Should().Be(0);
    }

    [PostgresFact]
    public async Task EnviosConcurrentesNuncaDejanDosAdjuntosVigentesDeFlito()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync("asignado");
        await using var almacen = new AlmacenFactory(Fixture);

        // Seis envíos al mismo tiempo: tres con el mismo contenido y tres distintos.
        var contenidos = Textos.Select(t => System.Text.Encoding.UTF8.GetBytes($"%PDF-1.4 {t}")).ToArray();
        var clientes = contenidos.Select(_ => Cliente(almacen, [ExternalScopes.AttachmentsWrite])).ToArray();
        var respuestas = await Task.WhenAll(contenidos.Select((c, i) => Task.Run(async () =>
        {
            using var content = Multipart(Tipo, c);
            return await clientes[i].PostAsync($"/api/v1/external/tramites/{tramite}/adjuntos", content, TestContext.Current.CancellationToken);
        })));

        respuestas.Select(r => (int)r.StatusCode).Should().OnlyContain(s => s == 201 || s == 200, "todas se serializan sin error");
        var filas = await FilasAsync(tramite);
        filas.Should().ContainSingle("nunca quedan dos adjuntos vigentes de Flito").Which.Provider.Should().Be("flito");
        almacen.Almacen.Borrados.Should().HaveCount(almacen.Almacen.Guardados.Count - 1, "cada reemplazo retiró el binario anterior");
    }

    // ── Host, pase y siembra ────────────────────────────────────────────────

    private static async Task<HttpResponseMessage> Post(AlmacenFactory host, Guid tramite, byte[] archivo, IReadOnlyList<string>? scopes = null)
    {
        var cliente = Cliente(host, scopes ?? [ExternalScopes.AttachmentsWrite]);
        using var content = Multipart(Tipo, archivo);
        return await cliente.PostAsync($"/api/v1/external/tramites/{tramite}/adjuntos", content, TestContext.Current.CancellationToken);
    }

    private static MultipartFormDataContent Multipart(string tipo, byte[] archivo)
    {
        var parte = new ByteArrayContent(archivo);
        parte.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent { { new StringContent(tipo), "tipo" }, { parte, "file", "comprobante.pdf" } };
    }

    private static HttpClient Cliente(AlmacenFactory host, IReadOnlyList<string> scopes)
    {
        using var scope = host.Factory.Services.CreateScope();
        var pase = scope.ServiceProvider.GetRequiredService<IExternalClientTokenIssuer>().Issue("flito-it", scopes).Token;
        var cliente = host.Factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        return cliente;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

    private static async Task<byte[]> LeerFixtureAsync() =>
        await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Tramites", "Fixtures", "comprobante-impuesto-sintetico.pdf"),
            TestContext.Current.CancellationToken);

    /// <summary>Host real con el file-manager reemplazado por un almacén en memoria.</summary>
    private sealed class AlmacenFactory : IAsyncDisposable
    {
        public AlmacenFactory(PostgresDatabaseFixture fixture)
        {
            Factory = new MarcaBlancaApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IAttachmentStorage>();
                s.AddSingleton<IAttachmentStorage>(Almacen);
            }));
        }

        public WebApplicationFactory<Program> Factory { get; }

        public AlmacenEnMemoria Almacen { get; } = new();

        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    private sealed class AlmacenEnMemoria : IAttachmentStorage
    {
        private int _n;

        public List<string> Guardados { get; } = [];

        public List<string> Borrados { get; } = [];

        public Exception? Falla { get; set; }

        public async Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            if (Falla is not null)
            {
                throw Falla;
            }

            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            string path;
            lock (Guardados)
            {
                path = $"fm-{++_n}";
                Guardados.Add(path);
            }

            return new StoredFile(path, Convert.ToHexStringLower(SHA256.HashData(ms.ToArray())), ms.Length);
        }

        public void Delete(string storagePath)
        {
            lock (Guardados)
            {
                Borrados.Add(storagePath);
            }
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private async Task SembrarAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(Compania, "IT-13263", isGroupParent: false, parentId: null));
        await ctx.SaveChangesAsync();
        ctx.Users.Add(new User
        {
            Id = Gestor,
            Email = "it-13263@flit.test",
            DisplayName = "Gestor 13263",
            Status = "active",
            HomeTenantId = Compania,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> RadicadoAsync(
        string estado, bool subsanacion = false, bool radicado = true, bool migrado = false, bool eliminado = false)
    {
        var id = Guid.CreateVersion7();
        await using (var ctx = NewContext())
        {
            var tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();
            ctx.ProcedureInstances.Add(new ProcedureInstance
            {
                Id = id,
                TenantId = Compania,
                ProcedureTypeId = tipo,
                ReferenceNumber = $"IT13263-{id.ToString("N")[^8..]}",
                Status = TramiteEstado.Borrador,
                Vin = $"VIN{id.ToString("N")[^14..]}",
                CreatedByUserId = Gestor,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        if (radicado)
        {
            await EjecutarAsync(
                "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) "
                + "SELECT tenant_id, id, 'entregado' FROM tramites.procedure_instances WHERE id = @id",
                ("id", id));
        }

        await EjecutarAsync(
            "UPDATE tramites.procedure_instances SET status = @estado, subsanacion_activa = @sub, is_migrated = @migrado, "
            + "deleted_at = CASE WHEN @eliminado THEN now() ELSE NULL END WHERE id = @id",
            ("id", id), ("estado", estado), ("sub", subsanacion), ("migrado", migrado), ("eliminado", eliminado));
        return id;
    }

    private async Task SembrarMarcaAsync(Guid tramite, string source) =>
        await EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, form_field_id, field_key, value_text, source) "
            + "SELECT pi.tenant_id, pi.id, (SELECT id FROM tramites.form_fields ORDER BY id LIMIT 1), 'impuesto_departamental_pagado', 'true', @source "
            + "FROM tramites.procedure_instances pi WHERE pi.id = @id",
            ("id", tramite), ("source", source));

    private async Task<Guid> InsertarAdjuntoAsync(Guid tramite, string? provider, string sha, string path)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tramites.procedure_instance_attachments (tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path, source, provider, uploaded_at) "
            + "SELECT tenant_id, id, 'liquidacion_impuesto', 'gestor.pdf', 'application/pdf', 10, @sha, @path, 'user', @provider, now() "
            + "FROM tramites.procedure_instances WHERE id = @id RETURNING id",
            conn);
        cmd.Parameters.AddWithValue("id", tramite);
        cmd.Parameters.AddWithValue("sha", sha);
        cmd.Parameters.AddWithValue("path", path);
        cmd.Parameters.AddWithValue("provider", (object?)provider ?? DBNull.Value);
        return (Guid)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private sealed record Fila(Guid Id, string Tipo, string Source, string? Provider, string Sha256, string Mimetype, long SizeBytes, string StoragePath, Guid? UploadedBy);

    private async Task<List<Fila>> FilasAsync(Guid tramite)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT id, tipo, source, provider, sha256, mimetype, size_bytes, storage_path, uploaded_by "
            + "FROM tramites.procedure_instance_attachments WHERE procedure_instance_id = @id ORDER BY uploaded_at, id",
            conn);
        cmd.Parameters.AddWithValue("id", tramite);
        var filas = new List<Fila>();
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            filas.Add(new Fila(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetGuid(8)));
        }

        return filas;
    }

    private async Task EjecutarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<long> EscalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }
}
