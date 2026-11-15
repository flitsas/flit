using System.Security.Claims;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// HU #14579, BE-0 (SP-8, ADR-0088, diseno.md «Modelo de datos») — <c>diagnostico.accesos_reporte</c> (migración <c>S2_AccesosReporte</c>): una
/// fila por cada visualización o descarga del módulo de reportes, de la compañía del lector, solo inserción y sin PII. Las reglas de
/// la tabla se prueban por SQL directo; el registro, por <see cref="RegistroAccesosReporte"/> con la empresa fija. Placas ficticias.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~AccesosReporteTests
/// </code>
/// </example>
public sealed class AccesosReporteTests
{
    private const string MigracionAnterior = "20261114171738_S1_Saneamiento";

    private static readonly string[] Columnas =
    [
        "id:uuid:NO", "tenant_id:uuid:NO", "usuario_id:uuid:NO", "alcance:character varying:NO", "tipo_reporte:character varying:NO",
        "formato:character varying:NO", "desde:date:NO", "hasta:date:NO", "filtros:jsonb:NO", "empresa_consultada:uuid:YES",
        "filas:integer:NO", "empresas:integer:NO", "fecha:timestamp with time zone:NO",
    ];

    private static readonly string[] Checks =
    [
        "ck_accesos_reporte_alcance", "ck_accesos_reporte_formato", "ck_accesos_reporte_tipo", "ck_accesos_reporte_rango",
        "ck_accesos_reporte_filas", "ck_accesos_reporte_empresas", "ck_accesos_reporte_empresa_consultada",
    ];

    // SP-8 — la migración crea la tabla con sus columnas, CHECK, índice y comentarios @pii, y el Down la retira.
    [Fact]
    public async Task BE0_S2_crea_accesos_reporte_con_columnas_checks_comentarios_e_indice_y_es_reversible()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);

        (await ListaAsync(conexion, "SELECT column_name || ':' || data_type || ':' || is_nullable FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = 'accesos_reporte'", ct))
            .Should().BeEquivalentTo(Columnas, "las columnas del borrador de diseno.md, sin placa, documento, NIT ni nombre");
        (await ListaAsync(conexion, "SELECT conname FROM pg_constraint WHERE contype = 'c' AND conrelid = 'diagnostico.accesos_reporte'::regclass", ct))
            .Should().BeEquivalentTo(Checks);
        (await DefinicionAsync(conexion, "ck_accesos_reporte_tipo", ct)).Should().ContainAll("vehiculos", "consultas", "saneamientos", "derivaciones", "consumo");
        (await DefinicionAsync(conexion, "ck_accesos_reporte_alcance", ct)).Should().ContainAll("COMPANIA", "BACKOFFICE_EMPRESA", "BACKOFFICE_GLOBAL");
        (await DefinicionAsync(conexion, "ck_accesos_reporte_formato", ct)).Should().ContainAll("PANTALLA", "XLSX", "PDF");
        (await ListaAsync(conexion, "SELECT indexdef FROM pg_indexes WHERE schemaname = 'diagnostico' AND tablename = 'accesos_reporte' AND indexname = 'ix_accesos_reporte_tenant_fecha'", ct))
            .Should().ContainSingle().Which.Should().Contain("(tenant_id, fecha DESC)");
        (await ListaAsync(conexion, "SELECT obj_description('diagnostico.accesos_reporte'::regclass, 'pg_class')", ct))
            .Should().ContainSingle().Which.Should().Contain("ADR-0088").And.Contain("Solo inserción.");
        (await ComentarioAsync(conexion, "usuario_id", ct)).Should().StartWith("@pii:low");
        (await ComentarioAsync(conexion, "filtros", ct)).Should().Contain("placa_filtrada");

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior, ct);
            (await ListaAsync(conexion, "SELECT tablename FROM pg_tables WHERE schemaname = 'diagnostico' AND tablename = 'accesos_reporte'", ct))
                .Should().BeEmpty("el Down retira la tabla");
            await db.Database.MigrateAsync(ct);
            (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
        }
    }

    // SP-8 (b) — la fila es de la compañía del lector, con el usuario del token; el filtro de placa se guarda como bandera, nunca su valor.
    [Fact]
    public async Task BE0_registro_guarda_fila_del_lector_con_filtros_de_lista_blanca_y_sin_la_placa()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        Guid id;
        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa))
        {
            id = await Registro(db, empresa, usuario).RegistrarAsync(
                Solicitud(filtros: new Dictionary<string, IReadOnlyList<string>> { ["estado"] = ["ERROR", "NA"], ["placa"] = ["QWE12"] }, filas: 25), ct);
        }

        await using var lectura = DosEmpresas.Contexto(api.BaseDeDatos, empresa);
        var fila = await lectura.Set<AccesoReporte>().AsNoTracking().SingleAsync(ct);
        fila.Id.Should().Be(id);
        fila.TenantId.Should().Be(empresa);
        fila.UsuarioId.Should().Be(usuario);
        fila.Alcance.Should().Be(AlcanceReporte.Compania);
        fila.TipoReporte.Should().Be(TipoReporte.Consultas);
        fila.Formato.Should().Be(FormatoReporte.Pantalla);
        fila.Filas.Should().Be(25);
        fila.EmpresaConsultada.Should().BeNull();
        fila.Fecha.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        fila.Filtros.Should().NotContain("QWE12", "SP-8: nunca el valor de la placa");
        var filtros = JsonElement.Parse(fila.Filtros);
        filtros.GetProperty("placa_filtrada").GetBoolean().Should().BeTrue();
        filtros.GetProperty("estado").EnumerateArray().Select(e => e.GetString()).Should().Equal("ERROR", "NA");
        filtros.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("estado", "placa_filtrada");

        await using var conexion = await AbrirAsync(api, ct);
        (await ListaAsync(conexion, $"SELECT tipo_reporte || '|' || alcance || '|' || formato FROM diagnostico.accesos_reporte WHERE id = '{id}'", ct))
            .Should().Equal("consultas|COMPANIA|PANTALLA");
    }

    // SP-8 — solo inserción: la aplicación no puede modificar ni borrar una fila; el modelo no lleva token de concurrencia.
    [Fact]
    public async Task BE0_UPDATE_o_DELETE_de_la_aplicacion_falla()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa))
            await Registro(db, empresa, Guid.NewGuid()).RegistrarAsync(Solicitud(), ct);

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa))
        {
            var fila = await db.Set<AccesoReporte>().SingleAsync(ct);
            db.Entry(fila).Property(f => f.Filas).CurrentValue = 999;
            await FluentActions.Awaiting(() => db.SaveChangesAsync(ct)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*solo inserción*");
        }

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa))
        {
            db.Remove(await db.Set<AccesoReporte>().SingleAsync(ct));
            await FluentActions.Awaiting(() => db.SaveChangesAsync(ct)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*solo inserción*");
        }

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa))
        {
            (await db.Set<AccesoReporte>().AsNoTracking().SingleAsync(ct)).Filas.Should().Be(10, "nada cambió en la base");
            db.Model.FindEntityType(typeof(AccesoReporte))!.GetProperties().Should().NotContain(p => p.IsConcurrencyToken);
        }

        typeof(AccesoReporte).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true } && p.Name != nameof(AccesoReporte.TenantId)
                        && !p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)))
            .Select(p => p.Name).Should().BeEmpty("solo TenantId tiene setter público");
    }

    // SP-8 (d) — un usuario de la compañía A no lee las filas de B (filtro global por tenant).
    [Fact]
    public async Task BE0_usuario_de_A_no_lee_filas_de_B()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        await using (var db = empresas.A.Db())
            await Registro(db, empresas.A.Id, Guid.NewGuid()).RegistrarAsync(Solicitud(), ct);
        await using (var db = empresas.B.Db())
        {
            await Registro(db, empresas.B.Id, Guid.NewGuid()).RegistrarAsync(Solicitud(), ct);
            await Registro(db, empresas.B.Id, Guid.NewGuid()).RegistrarAsync(Solicitud(formato: FormatoReporte.Xlsx), ct);
        }

        await using var deA = empresas.A.Db();
        (await deA.Set<AccesoReporte>().AsNoTracking().ToListAsync(ct)).Should().ContainSingle().Which.TenantId.Should().Be(empresas.A.Id);
        await using var deB = empresas.B.Db();
        (await deB.Set<AccesoReporte>().AsNoTracking().CountAsync(ct)).Should().Be(2);
    }

    // SP-8/SP-9 — fail-closed: un filtro fuera de la lista blanca (documento, nombre o un valor libre) no se registra y la petición
    // no sigue; la empresa consultada va solo en BACKOFFICE_EMPRESA; un rango invertido lo rechaza el CHECK y el error se propaga.
    [Fact]
    public async Task BE0_registro_rechaza_filtros_fuera_de_lista_blanca_y_propaga_el_fallo_de_la_base()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa);
        var registro = Registro(db, empresa, Guid.NewGuid());

        foreach (var filtros in new Dictionary<string, IReadOnlyList<string>>[]
                 {
                     new() { ["documento"] = ["1020304050"] },
                     new() { ["nombre"] = ["PERSONA FICTICIA"] },
                     new() { ["estado"] = ["texto libre"] },
                     new() { ["fuente"] = ["RUES"] },
                 })
        {
            FluentActions.Invoking(() => registro.Agregar(Solicitud(filtros: filtros))).Should().Throw<ArgumentException>()
                .Which.Message.Should().NotContain("1020304050").And.NotContain("PERSONA FICTICIA");
        }

        FluentActions.Invoking(() => registro.Agregar(Solicitud(alcance: AlcanceReporte.BackofficeEmpresa))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => registro.Agregar(Solicitud(alcance: AlcanceReporte.Compania, empresaConsultada: Guid.NewGuid()))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => registro.Agregar(Solicitud(alcance: AlcanceReporte.Compania, tipo: TipoReporte.Consumo))).Should().Throw<ArgumentException>();
        db.ChangeTracker.Entries<AccesoReporte>().Should().BeEmpty("lo rechazado no queda pendiente de guardar");

        await FluentActions.Awaiting(() => registro.RegistrarAsync(Solicitud(desde: new DateOnly(2026, 11, 10), hasta: new DateOnly(2026, 11, 1)), ct))
            .Should().ThrowAsync<DbUpdateException>("ck_accesos_reporte_rango: sin registro no hay datos");
    }

    private static RegistroAccesosReporte Registro(DiagnosticoDb db, Guid empresa, Guid usuario)
    {
        var claims = new List<Claim> { new("sub", usuario.ToString()), new("permissions", Permisos.ReportesVer) };
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba")) } };
        return new RegistroAccesosReporte(db, new UsuarioActual(http, new EmpresaFija(empresa)), NullLogger<RegistroAccesosReporte>.Instance);
    }

    private static SolicitudAccesoReporte Solicitud(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? filtros = null,
        int filas = 10,
        FormatoReporte formato = FormatoReporte.Pantalla,
        AlcanceReporte alcance = AlcanceReporte.Compania,
        TipoReporte tipo = TipoReporte.Consultas,
        Guid? empresaConsultada = null,
        DateOnly? desde = null,
        DateOnly? hasta = null) =>
        new(alcance, tipo, formato, desde ?? new DateOnly(2026, 10, 17), hasta ?? new DateOnly(2026, 11, 15),
            filtros ?? new Dictionary<string, IReadOnlyList<string>>(), filas, Empresas: 1, empresaConsultada);

    private static async Task<List<string>> ListaAsync(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(sql, conexion);
        var lista = new List<string>();
        await using var fila = await comando.ExecuteReaderAsync(ct);
        while (await fila.ReadAsync(ct))
            lista.Add(fila.IsDBNull(0) ? string.Empty : fila.GetString(0));
        return lista;
    }

    private static async Task<string> DefinicionAsync(NpgsqlConnection conexion, string nombre, CancellationToken ct) =>
        string.Join(" ", await ListaAsync(conexion, $"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = '{nombre}'", ct));

    private static async Task<string> ComentarioAsync(NpgsqlConnection conexion, string columna, CancellationToken ct) =>
        string.Join(" ", await ListaAsync(conexion,
            $"SELECT col_description('diagnostico.accesos_reporte'::regclass, ordinal_position::int) FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = 'accesos_reporte' AND column_name = '{columna}'", ct));

    private static async Task<NpgsqlConnection> AbrirAsync(ApiDePrueba api, CancellationToken ct)
    {
        var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        return conexion;
    }
}
