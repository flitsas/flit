using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #14087 AC8 (decisión del usuario 2026-10-27; enmienda de ADR-0075 en Propuesto) — el consumidor de resultados guarda la
/// respuesta COMPLETA de la fuente tal como llegó por el bus (el texto exacto del proto-JSON) CIFRADA con Data Protection
/// (propósito <see cref="Proposito"/>) en <c>consultas.respuesta_fuente_cifrada</c>, sin tocar el <c>payload</c> normalizado.
/// En las fuentes de persona el texto cifrado es un objeto <c>{"&lt;orden&gt;": "&lt;proto-JSON de esa persona&gt;"}</c>. Un ERROR
/// (sin respuesta) deja la columna en null. PostgreSQL real (<see cref="ApiDePrueba"/>) con el bus de prueba; placas TST### y
/// documentos 99999####.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~RespuestaFuenteTests
/// </code>
/// </example>
public sealed class RespuestaFuenteTests
{
    /// <summary>Propósito de Data Protection de la respuesta de la fuente (distinto del de los documentos de persona).</summary>
    internal const string Proposito = "Flit.Diagnostico.RespuestaFuente.v1";

    [Fact]
    public async Task Resultado_OK_guarda_cifrada_la_respuesta_del_bus_tal_cual_y_el_payload_sigue_normalizado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var bus = Motor.Bus(api);
        var trabajo = (await bus.DescartarTrabajosAsync(ct)).Should().ContainSingle().Subject;
        var sobre = bus.Resultado(trabajo, await ConsultasSimuladas.ResponderAsync(api, trabajo, ct));
        var cruda = sobre.Data.GetProperty("respuesta").GetRawText();

        (await bus.EntregarAsync(sobre, ct)).Should().BeTrue();

        var celda = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single(c => c.Fuente == Fuente.Runt);
        celda.Estado.Should().Be(EstadoCelda.Completado);
        var cifrada = await CifradaAsync(api, celda.Id, ct);
        cifrada.Should().NotBeNull("la respuesta completa de la fuente queda guardada");
        Encoding.UTF8.GetString(cifrada!).Should().NotContain("TST001", "va cifrada, nunca en claro");
        Descifrar(api, cifrada!).Should().Be(cruda, "el texto exacto que llegó por el bus");
        cruda.Should().Contain("\"informacion_general\"", "el bus trae los nombres del .proto");
        celda.Payload.Should().NotContain("informacion_general", "el payload sigue siendo el normalizado (R-13)");
    }

    [Fact]
    public async Task Resultado_ERROR_sin_respuesta_deja_la_columna_en_null()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var runt = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        await GuardarCifradaAsync(api, runt.Id, Cifrar(api, """{"vieja":true}"""), ct);
        var bus = Motor.Bus(api, maxReintentos: 0);
        bus.Responder = _ => RespuestaSimulada.Fallo(StatusCode.InvalidArgument);

        await bus.ProcesarTodoAsync(ct);

        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado.Should().Be(EstadoCelda.Error);
        (await CifradaAsync(api, runt.Id, ct)).Should().BeNull("un ERROR no trae respuesta de la fuente");
    }

    [Fact]
    public async Task Celda_de_persona_guarda_la_respuesta_de_cada_persona_por_su_orden()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorPersonas.ArrancarAsync(null, b =>
        {
            b.Services.AddSingleton<IFuenteRuntPersona, FuenteRuntPersonaSimulada>();
            b.Services.AddSingleton<IFuenteRues, FuenteRuesSimulada>();
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST301")], ct);
        var bus = Motor.Bus(api);
        (await bus.ProcesarLoteAsync(ct)).Should().Be(1, "primero la RUNT");
        var trabajos = await bus.DescartarTrabajosAsync(ct);
        trabajos.Should().HaveCount(3);
        var crudas = new Dictionary<Guid, string>();

        foreach (var trabajo in trabajos)
        {
            var sobre = bus.Resultado(trabajo, await ConsultasSimuladas.ResponderAsync(api, trabajo, ct));
            crudas[trabajo.PersonaId!.Value] = sobre.Data.GetProperty("respuesta").GetRawText();
            (await bus.EntregarAsync(sobre, ct)).Should().BeTrue();
        }

        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST301", Fuente.RuntPropietario, ct);
        celda.Estado.Should().Be(EstadoCelda.Completado);
        var personas = MotorPersonas.PersonasDelPayload(celda.Payload);
        var mapa = JsonDocument.Parse(Descifrar(api, (await CifradaAsync(api, celda.Id, ct))!)).RootElement;
        mapa.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["1", "2", "3"], "una respuesta por persona, por su orden");
        foreach (var persona in personas)
        {
            var id = persona.GetProperty("persona_id").GetGuid();
            var orden = persona.GetProperty("orden").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
            mapa.GetProperty(orden).GetString().Should().Be(crudas[id], $"la persona de orden {orden} guarda su respuesta exacta");
        }
    }

    // ── Apoyo (también lo usan DetalleCeldaTests, ReutilizacionTests y LlenadoManualTests) ─────────────────────────

    internal static IDataProtector Protector(ApiDePrueba api) =>
        api.App.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Proposito);

    internal static byte[] Cifrar(ApiDePrueba api, string texto) => Protector(api).Protect(Encoding.UTF8.GetBytes(texto));

    internal static string Descifrar(ApiDePrueba api, byte[] cifrada) => Encoding.UTF8.GetString(Protector(api).Unprotect(cifrada));

    /// <summary>La columna <c>respuesta_fuente_cifrada</c> de la celda, leída por SQL directo.</summary>
    internal static async Task<byte[]?> CifradaAsync(ApiDePrueba api, Guid celda, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand("SELECT respuesta_fuente_cifrada FROM diagnostico.consultas WHERE id = @id", conexion);
        consulta.Parameters.AddWithValue("id", celda);
        var valor = await consulta.ExecuteScalarAsync(ct);
        return valor is byte[] bytes ? bytes : null;
    }

    /// <summary>Deja <paramref name="cifrada"/> en la columna <c>respuesta_fuente_cifrada</c> de la celda, por SQL directo.</summary>
    internal static async Task GuardarCifradaAsync(ApiDePrueba api, Guid celda, byte[]? cifrada, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("UPDATE diagnostico.consultas SET respuesta_fuente_cifrada = @valor WHERE id = @id", conexion);
        comando.Parameters.AddWithValue("id", celda);
        comando.Parameters.Add(new NpgsqlParameter("valor", NpgsqlTypes.NpgsqlDbType.Bytea) { Value = (object?)cifrada ?? DBNull.Value });
        (await comando.ExecuteNonQueryAsync(ct)).Should().Be(1);
    }
}
