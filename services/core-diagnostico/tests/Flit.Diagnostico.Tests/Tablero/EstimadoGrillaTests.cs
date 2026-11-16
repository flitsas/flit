using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #13796 (F05-H7, F05-C13/C14) — con <c>estimado_consultas=AL_FINALIZAR</c> y el lote cerrado, la grilla estima las fuentes
/// de persona con las filas de <c>personas_vehiculo</c> por rol y cuenta como consumido un intento por persona (sin los
/// <c>desde_cache</c> de la reutilización ni los de llenado manual). Las personas de otra compañía nunca suman. PostgreSQL
/// real (<see cref="ApiDePrueba"/>); lotes, personas e intentos se siembran directo, con documentos cifrados inventados.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Tablero.EstimadoGrillaTests
/// </code>
/// </example>
public sealed class EstimadoGrillaTests
{
    private static readonly Fuente[] Alcance = [Fuente.Runt, Fuente.RuntPropietario];

    // AC2 (F05-C13) — lote cerrado con un vehículo de tres propietarios: RUNT_PROPIETARIO muestra 3 estimadas y 3 consumidas
    // (un intento por persona); el reutilizado y el manual no consumen, y la fuente ya no es un mínimo.
    [Fact]
    public async Task Consumido_cuenta_un_intento_por_persona()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteCerradoAsync(empresa, "TST161", ct);
        var personas = await PersonasAsync(empresa, lote.Vehiculos[0].Id, propietarios: 3, ct);
        await Tablero.IntentoAsync(empresa, lote.Celda("TST161", Fuente.Runt), desdeCache: false, ct);
        var celda = lote.Celda("TST161", Fuente.RuntPropietario);
        foreach (var persona in personas)
            await IntentoDePersonaAsync(empresa, celda, persona, ct);
        await IntentoDePersonaAsync(empresa, celda, personas[0], ct, desdeCache: true);
        await IntentoDePersonaAsync(empresa, celda, personas[1], ct, disparador: DisparadorIntento.Manual);

        var estimado = await EstimadoAsync(empresa, lote.Id, ct);

        estimado.Should().Equal(["RUNT 1 1 False", "RUNT_PROPIETARIO 3 3 False"]);
    }

    // AC3 (F05-C13, F05-C14, fuga) — personas e intentos de la compañía B en la misma base no cuentan en el estimado del lote de A.
    [Fact]
    public async Task Personas_de_otra_compania_no_suman()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var dos = DosEmpresas.En(api);
        var loteA = await LoteCerradoAsync(dos.A, "TST171", ct);
        var loteB = await LoteCerradoAsync(dos.B, "TST171", ct);
        var personasA = await PersonasAsync(dos.A, loteA.Vehiculos[0].Id, propietarios: 2, ct);
        var personasB = await PersonasAsync(dos.B, loteB.Vehiculos[0].Id, propietarios: 5, ct);
        foreach (var persona in personasA)
            await IntentoDePersonaAsync(dos.A, loteA.Celda("TST171", Fuente.RuntPropietario), persona, ct);
        foreach (var persona in personasB)
            await IntentoDePersonaAsync(dos.B, loteB.Celda("TST171", Fuente.RuntPropietario), persona, ct);

        var deA = await EstimadoAsync(dos.A, loteA.Id, ct);
        var deB = await EstimadoAsync(dos.B, loteB.Id, ct);

        deA.Should().Equal(["RUNT 1 0 False", "RUNT_PROPIETARIO 2 2 False"], "las cinco personas de B no suman en A");
        deB.Should().Equal(["RUNT 1 0 False", "RUNT_PROPIETARIO 5 5 False"]);
    }

    private static Task<LoteSembrado> LoteCerradoAsync(EmpresaDePrueba empresa, string placa, CancellationToken ct) =>
        Tablero.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Completado, estimado: "AL_FINALIZAR", alcance: Alcance, vehiculos:
            [new(placa, [new(Fuente.Runt, EstadoCelda.Completado, 2), new(Fuente.RuntPropietario, EstadoCelda.Completado, 3)])]);

    /// <summary>Siembra propietarios del vehículo con documentos cifrados ficticios (bytes inventados, nunca en claro).</summary>
    private static async Task<List<PersonaVehiculo>> PersonasAsync(EmpresaDePrueba empresa, Guid vehiculo, int propietarios, CancellationToken ct)
    {
        var personas = Enumerable.Range(1, propietarios).Select(orden => new PersonaVehiculo
        {
            VehiculoId = vehiculo,
            Rol = RolPersona.Propietario,
            Orden = orden,
            DocTipo = "CC",
            DocCifrado = [0x0A, (byte)orden],
            Origen = OrigenDocumento.Proveedor,
            CreadoEn = DateTimeOffset.UtcNow,
        }).ToList();
        await using var db = empresa.Db();
        db.AddRange(personas);
        await db.SaveChangesAsync(ct);
        return personas;
    }

    private static async Task IntentoDePersonaAsync(
        EmpresaDePrueba empresa, CeldaConsulta celda, PersonaVehiculo persona, CancellationToken ct, bool desdeCache = false,
        DisparadorIntento disparador = DisparadorIntento.Auto)
    {
        var ahora = DateTimeOffset.UtcNow;
        await using var db = empresa.Db();
        db.Add(new IntentoConsulta
        {
            DiagnosticoId = celda.DiagnosticoId,
            VehiculoId = celda.VehiculoId,
            ConsultaId = celda.Id,
            Fuente = celda.Fuente,
            Servicio = desdeCache ? null : ServicioConsulta.Intempo,
            Disparador = disparador,
            Inicio = ahora,
            Fin = ahora,
            Estado = ResultadoIntento.Success,
            DesdeCache = desdeCache,
            UsuarioId = disparador == DisparadorIntento.Manual ? Guid.NewGuid() : null,
            PersonaId = persona.Id,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>El estimado de la grilla como «FUENTE estimadas consumidas minimo».</summary>
    private static async Task<List<string>> EstimadoAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote), ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        return [.. (await Tablero.JsonAsync(respuesta, ct)).GetProperty("estimado_consultas").EnumerateArray().Select(e =>
            $"{e.GetProperty("fuente").GetString()} {e.GetProperty("estimadas").GetInt32()} {e.GetProperty("consumidas").GetInt32()} {e.GetProperty("minimo").GetBoolean()}")];
    }
}
