using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Configuracion;

/// <summary>
/// HU #13736 (F00.2-H1) — <c>diagnostico.configuracion_empresa</c> y <c>diagnostico.cambios_contrato</c> sobre el schema
/// migrado (base efímera de <see cref="ApiDePrueba"/>). Las reglas las garantiza la base: se prueban por SQL directo.
/// </summary>
public sealed class ConfiguracionEmpresaSchemaTests
{
    // Escenario 1 — valores por defecto en la base (F00.2-C2).
    [Fact]
    public async Task Valores_por_defecto_al_insertar_solo_tenant()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        await using var conexion = await AbrirAsync(api, ct);
        await InsertarSoloTenantAsync(conexion, empresas.A.Id, ct);

        await using (var leer = new NpgsqlCommand(
                         "SELECT fuentes_habilitadas::text, limite_vehiculos_lote, visibilidad_usuario, fuentes_por_defecto::text, " +
                         "estimado_consultas, redaccion_ia_suspendida FROM diagnostico.configuracion_empresa WHERE tenant_id = @t", conexion))
        {
            leer.Parameters.AddWithValue("t", empresas.A.Id);
            await using var fila = await leer.ExecuteReaderAsync(ct);
            (await fila.ReadAsync(ct)).Should().BeTrue();
            fila.GetString(0).Should().Be("[\"RUNT\"]");
            fila.GetInt32(1).Should().Be(500);
            fila.GetString(2).Should().Be("EMPRESA");
            fila.GetString(3).Should().Be("[\"RUNT\"]");
            fila.GetString(4).Should().Be("NO_MOSTRAR");
            fila.GetBoolean(5).Should().BeFalse();
        }

        // La misma fila leída por el contexto de datos de la empresa: los códigos se traducen al dominio.
        await using var db = empresas.A.Db();
        var configuracion = await db.Set<ConfiguracionEmpresa>().AsNoTracking().SingleAsync(ct);
        configuracion.FuentesHabilitadas.Should().Equal(Fuente.Runt);
        configuracion.FuentesPorDefecto.Should().Equal(Fuente.Runt);
        configuracion.LimiteVehiculosLote.Should().Be(500);
        configuracion.VisibilidadUsuario.Should().Be(VisibilidadUsuario.Empresa);
        configuracion.EstimadoConsultas.Should().Be(EstimadoConsultas.NoMostrar);
        configuracion.RedaccionIaSuspendida.Should().BeFalse();
        await using var deB = empresas.B.Db();
        (await deB.Set<ConfiguracionEmpresa>().CountAsync(ct)).Should().Be(0, "B no ve la configuración de A");
    }

    // Escenario 2 — subconjunto garantizado por la base (F00.2-C5).
    [Fact]
    public async Task Check_rechaza_por_defecto_fuera_de_habilitadas()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        await using var conexion = await AbrirAsync(api, ct);
        await InsertarSoloTenantAsync(conexion, tenant, ct);

        var fuera = () => ActualizarAsync(conexion, tenant, "fuentes_por_defecto = '[\"RUNT\", \"AVALUO\"]'::jsonb", ct);

        var error = (await fuera.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        error.ConstraintName.Should().Be("ck_por_defecto_subconjunto");

        // Con AVALUO contratado, el mismo valor sí cabe.
        await ActualizarAsync(conexion, tenant, "fuentes_habilitadas = '[\"RUNT\", \"AVALUO\"]'::jsonb, fuentes_por_defecto = '[\"RUNT\", \"AVALUO\"]'::jsonb", ct);
    }

    // Escenario 3 — límite positivo (F00.2-C6).
    [Fact]
    public async Task Check_rechaza_limite_cero_o_negativo()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        await using var conexion = await AbrirAsync(api, ct);
        await InsertarSoloTenantAsync(conexion, tenant, ct);

        foreach (var limite in new[] { "0", "-1" })
        {
            var guardar = () => ActualizarAsync(conexion, tenant, "limite_vehiculos_lote = " + limite, ct);
            var error = (await guardar.Should().ThrowAsync<PostgresException>()).Which;
            error.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            error.ConstraintName.Should().Be("ck_configuracion_empresa_limite_positivo");
        }

        await ActualizarAsync(conexion, tenant, "limite_vehiculos_lote = 1", ct);
    }

    // Escenario 4 — sin retención ni purga (F00.2-C11, R-18).
    [Fact]
    public async Task No_existen_columnas_de_retencion_ni_purga()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);

        var columnas = new List<string>();
        await using (var listar = new NpgsqlCommand(
                         "SELECT column_name FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = 'configuracion_empresa'", conexion))
        await using (var fila = await listar.ExecuteReaderAsync(ct))
        {
            while (await fila.ReadAsync(ct))
                columnas.Add(fila.GetString(0));
        }

        columnas.Should().BeEquivalentTo(
            ["tenant_id", "fuentes_habilitadas", "limite_vehiculos_lote", "contrato_actualizado_por", "contrato_actualizado_en",
             "redaccion_ia_suspendida", "visibilidad_usuario", "fuentes_por_defecto", "estimado_consultas",
             "operativa_actualizada_por", "operativa_actualizada_en"]);
        columnas.Should().NotContain(c => c.Contains("retencion", StringComparison.Ordinal) || c.Contains("purga", StringComparison.Ordinal));
    }

    // Auditoría del cambio contractual (criterio 6 del Feature): la tabla existe, va por empresa y guarda jsonb.
    [Fact]
    public async Task Cambio_de_contrato_se_guarda_por_empresa_con_valores_jsonb()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        await using (var conexion = await AbrirAsync(api, ct))
            await InsertarSoloTenantAsync(conexion, empresas.A.Id, ct);

        await using (var db = empresas.A.Db())
        {
            db.Add(new CambioContrato { UsuarioId = Guid.NewGuid(), Campo = "fuentes_habilitadas", ValorAnterior = "[\"RUNT\"]", ValorNuevo = "[\"RUNT\",\"AVALUO\"]" });
            await db.SaveChangesAsync(ct);
        }

        await using var leer = empresas.A.Db();
        var cambio = await leer.Set<CambioContrato>().AsNoTracking().SingleAsync(ct);
        cambio.TenantId.Should().Be(empresas.A.Id);
        cambio.ValorNuevo.Should().Be("[\"RUNT\", \"AVALUO\"]", "jsonb normaliza el texto");
        cambio.Fecha.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        await using var deB = empresas.B.Db();
        (await deB.Set<CambioContrato>().CountAsync(ct)).Should().Be(0, "B no ve los cambios de A");
    }

    private static async Task<NpgsqlConnection> AbrirAsync(ApiDePrueba api, CancellationToken ct)
    {
        var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        return conexion;
    }

    private static async Task InsertarSoloTenantAsync(NpgsqlConnection conexion, Guid tenant, CancellationToken ct)
    {
        await using var insertar = new NpgsqlCommand("INSERT INTO diagnostico.configuracion_empresa (tenant_id) VALUES (@t)", conexion);
        insertar.Parameters.AddWithValue("t", tenant);
        await insertar.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Actualiza la fila con una asignación literal de la propia prueba (nunca entrada externa).</summary>
    private static async Task ActualizarAsync(NpgsqlConnection conexion, Guid tenant, string asignacion, CancellationToken ct)
    {
        await using var actualizar = new NpgsqlCommand("UPDATE diagnostico.configuracion_empresa SET " + asignacion + " WHERE tenant_id = @t", conexion);
        actualizar.Parameters.AddWithValue("t", tenant);
        await actualizar.ExecuteNonQueryAsync(ct);
    }
}
