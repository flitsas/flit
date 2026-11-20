using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13727 (ADR-0075) — una persona sin documento consultable (NIT sin dígitos: no hay qué pedir al RUES) no publica trabajo y
/// se resuelve de inmediato como antes del bus: ERROR definitivo <c>ESTADO_NO_OK</c> de ESA persona. Si es la única del rol, la
/// celda se cierra sin publicar nada; si comparte la celda con personas consultables, no bloquea el cierre: la celda se cierra
/// con la última respuesta de las que sí se consultaron (ERROR <c>PERSONAS_CON_ERROR</c>, conservando las OK), sin esperar al
/// reconciliador. Documentos del rango ficticio 99999####; placas TST31#.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~PersonaSinDocumentoTests
/// </code>
/// </example>
public sealed class PersonaSinDocumentoTests
{
    private const string NitSinDigitos = "NIT-X";
    private const string Natural = "9999900311";

    [Fact]
    public async Task Persona_sin_documento_consultable_junto_a_otra_no_bloquea_el_cierre_de_la_celda()
    {
        var ct = TestContext.Current.CancellationToken;
        var runt = new RuntConPersonas();
        var fuentes = new FuentesPersonaProgramables();
        await using var api = await MotorPersonas.ArrancarAsync(fuentes, runt: runt);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        runt.PorEmpresa[(empresa.Id, "TST311")] = [RuntConPersonas.Propio("CC", Natural), RuntConPersonas.Propio("NIT", NitSinDigitos)];
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST311")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Bus(api), ct);

        fuentes.Llamadas.Select(l => (l.Consulta, l.Numero)).Should().BeEquivalentTo([("RUNT_PERSONA", Natural)], "el NIT sin dígitos no se consulta");
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST311", Fuente.RuntPropietario, ct);
        (celda.Estado, celda.TipoError).Should().Be((EstadoCelda.Error, EjecutorPorPersonas.PersonasConError), "no espera al reconciliador");
        celda.TrabajoId.Should().NotBeNull();
        var personas = MotorPersonas.PersonasDelPayload(celda.Payload);
        personas.Select(p => p.GetProperty("estado").GetString()).Should().Equal("OK", "ERROR");
        personas[1].GetProperty("tipo_error").GetString().Should().Be("ESTADO_NO_OK");
        var ids = (await MotorPersonas.PersonasAsync(empresa, celda.VehiculoId, RolPersona.Propietario, ct)).Select(p => p.Id);
        personas.Select(p => p.GetProperty("persona_id").GetGuid()).Should().Equal(ids);
    }

    [Fact]
    public async Task Unica_persona_sin_documento_consultable_cierra_la_celda_sin_publicar()
    {
        var ct = TestContext.Current.CancellationToken;
        var runt = new RuntConPersonas();
        var fuentes = new FuentesPersonaProgramables();
        await using var api = await MotorPersonas.ArrancarAsync(fuentes, runt: runt);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        runt.PorEmpresa[(empresa.Id, "TST312")] = [RuntConPersonas.Propio("NIT", NitSinDigitos)];
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST312")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Bus(api), ct);

        fuentes.Llamadas.Should().BeEmpty();
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST312", Fuente.RuntPropietario, ct);
        (celda.Estado, celda.TipoError, celda.TrabajoId).Should().Be((EstadoCelda.Error, "ESTADO_NO_OK", (Guid?)null));
        MotorPersonas.PersonasDelPayload(celda.Payload).Should().ContainSingle().Which.GetProperty("estado").GetString().Should().Be("ERROR");
    }

    [Fact]
    public async Task El_reproceso_vuelve_a_resolver_de_inmediato_a_la_persona_sin_documento()
    {
        var ct = TestContext.Current.CancellationToken;
        var runt = new RuntConPersonas();
        var fuentes = new FuentesPersonaProgramables();
        fuentes.Responder(Natural, FuentesPersonaProgramables.Invalida);
        await using var api = await MotorPersonas.ArrancarAsync(fuentes, runt: runt);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        runt.PorEmpresa[(empresa.Id, "TST313")] = [RuntConPersonas.Propio("CC", Natural), RuntConPersonas.Propio("NIT", NitSinDigitos)];
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST313")], ct);
        var bus = Motor.Bus(api);
        await MotorRunt.ProcesarTodoAsync(bus, ct);
        var enError = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST313", Fuente.RuntPropietario, ct);
        (enError.Estado, enError.TipoError).Should().Be((EstadoCelda.Error, EjecutorPorPersonas.PersonasConError), "fallan las dos, cada una por su causa definitiva");

        var respuesta = await api.Cliente(MotorPersonas.Admin(api, empresa))
            .PostAsync($"{Flit.Diagnostico.Api.Program.Prefijo}/consultas/{enError.Id}/reprocesar", null, ct);
        respuesta.IsSuccessStatusCode.Should().BeTrue(await respuesta.Content.ReadAsStringAsync(ct));
        await MotorRunt.ProcesarTodoAsync(bus, ct);

        var final = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST313", Fuente.RuntPropietario, ct);
        (final.Estado, final.TipoError).Should().Be((EstadoCelda.Error, EjecutorPorPersonas.PersonasConError));
        MotorPersonas.PersonasDelPayload(final.Payload).Select(p => p.GetProperty("estado").GetString()).Should().Equal("OK", "ERROR");
        fuentes.LlamadasA(Natural).Should().Be(2);
    }
}
