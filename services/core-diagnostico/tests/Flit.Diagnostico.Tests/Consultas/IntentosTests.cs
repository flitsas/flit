using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13737 (F03-H6), AC1 y AC5 (F03-C11, R-1) — cada ejecución de una celda inserta un intento en
/// <c>diagnostico.intentos_consulta</c> (solo inserción) con diagnóstico, vehículo, celda, identificador consultado (placa o VIN,
/// nunca documentos), fuente, servicio, inicio y fin, estado, tipo y mensaje de error, transacción del proveedor,
/// <c>disparador=AUTO</c> y <c>usuario_id</c> nulo. La auditoría se lee en <c>GET /backoffice/consultas/{consulta}/intentos</c>,
/// solo backoffice FLIT con <c>diagnostico.auditoria.ver</c>: un usuario de la compañía recibe 403. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~IntentosTests
/// </code>
/// </example>
public sealed class IntentosTests
{
    private const string PropietarioNatural = "9999900001";

    // AC1 (F03-C11) — RUNT por placa resuelta por la simulada de Intempo: un intento con todos los campos.
    [Fact]
    public async Task Cada_llamada_registra_un_intento_con_todos_los_campos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var antes = DateTimeOffset.UtcNow.AddSeconds(-1);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var celda = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single();
        var vehiculo = (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).Single();
        var intento = (await ReintentosTests.IntentosAsync(api, empresa, id, ct)).Should().ContainSingle("una ejecución, un intento").Subject;
        intento.TenantId.Should().Be(empresa.Id);
        intento.DiagnosticoId.Should().Be(id);
        intento.VehiculoId.Should().Be(vehiculo.Id);
        intento.ConsultaId.Should().Be(celda.Id);
        intento.Fuente.Should().Be(Fuente.Runt);
        intento.Servicio.Should().Be(ServicioConsulta.Intempo);
        intento.Estado.Should().Be(ResultadoIntento.Success);
        intento.TipoError.Should().BeNull();
        intento.MensajeError.Should().BeNull();
        intento.TransaccionProveedorId.Should().BeNull("Intempo no informa transacción (HU #13877)");
        intento.Disparador.Should().Be(DisparadorIntento.Auto);
        intento.UsuarioId.Should().BeNull("lo disparó el procesador");
        intento.DesdeCache.Should().BeFalse();
        intento.Inicio.Should().BeOnOrAfter(antes).And.BeOnOrBefore(intento.Fin);
        intento.Fin.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(1));

        var (identificador, fila, tipoInicio) = await EnBrutoAsync(api, intento.Id, ct);
        identificador.Should().Be("TST001", "el identificador consultado es la placa");
        tipoInicio.Should().Be("timestamp with time zone", "inicio y fin con milisegundos (timestamptz)");
        fila.Should().NotContain(PropietarioNatural, "el intento no guarda documentos");
    }

    // AC5 (F03-C11) — los intentos solo los ve el backoffice FLIT con diagnostico.auditoria.ver: la compañía recibe 403.
    [Fact]
    public async Task Usuario_de_compania_recibe_403_en_intentos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
        var celda = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single();
        var url = $"{Program.Prefijo}/backoffice/consultas/{celda.Id}/intentos?empresa={empresa.Id}";
        var flit = Guid.NewGuid();

        var compania = api.Cliente(api.Token(empresa: empresa.Id, permisos: [.. Permisos.Todos], roles: ["admin_diagnostico"]));
        (await compania.GetAsync(url, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "un usuario de la compañía no ve la auditoría");
        var sinAuditoria = api.Cliente(api.Token(empresa: flit, permisos: [Permisos.BackofficeVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT"));
        (await sinAuditoria.GetAsync(url, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sin diagnostico.auditoria.ver");

        var backoffice = api.Cliente(api.Token(
            empresa: flit, permisos: [Permisos.BackofficeVer, Permisos.AuditoriaVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT"));
        var respuesta = await backoffice.GetAsync(url, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("consulta").GetGuid().Should().Be(celda.Id);
        var intento = cuerpo.GetProperty("intentos").EnumerateArray().Should().ContainSingle().Subject;
        intento.GetProperty("fuente").GetString().Should().Be("RUNT");
        intento.GetProperty("servicio").GetString().Should().Be("INTEMPO");
        intento.GetProperty("identificador").GetString().Should().Be("TST001");
        intento.GetProperty("estado").GetString().Should().Be("SUCCESS");
        intento.GetProperty("disparador").GetString().Should().Be("AUTO");
        intento.GetProperty("usuario_id").ValueKind.Should().Be(JsonValueKind.Null);
        (await backoffice.GetAsync($"{Program.Prefijo}/backoffice/consultas/{Guid.NewGuid()}/intentos?empresa={empresa.Id}", ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "una celda que no es de esa empresa no existe para el backoffice");
    }

    /// <summary>Identificador consultado, la fila completa como texto y el tipo de la columna inicio, por SQL directo.</summary>
    private static async Task<(string? Identificador, string Fila, string TipoInicio)> EnBrutoAsync(ApiDePrueba api, Guid intento, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand(
            "SELECT i.identificador_consultado, row_to_json(i)::text, " +
            "(SELECT data_type FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = 'intentos_consulta' AND column_name = 'inicio') " +
            "FROM diagnostico.intentos_consulta i WHERE i.id = @id",
            conexion);
        consulta.Parameters.AddWithValue("id", intento);
        await using var lector = await consulta.ExecuteReaderAsync(ct);
        (await lector.ReadAsync(ct)).Should().BeTrue();
        return (lector.IsDBNull(0) ? null : lector.GetString(0), lector.GetString(1), lector.GetString(2));
    }
}
