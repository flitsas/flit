using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Configuracion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13790 (F05-H1), AC1-AC5 (F05-C3, F05-C11, F05-C14, decisión Q1) — al completar la celda RUNT se guardan TODOS los
/// propietarios y TODOS los locatarios del vehículo en <c>diagnostico.personas_vehiculo</c>, con el documento cifrado y sin
/// nombre; si el proveedor no trae locatario entra el declarado por el cliente; al reutilizar el RUNT del mismo día se copian
/// las personas del vehículo de origen con el mismo cifrado. PostgreSQL real (<see cref="ApiDePrueba"/>), fuente RUNT simulada
/// de Intempo más <see cref="FuenteRuntFija"/> (copropiedad y leasing con varios locatarios); placas TST### y documentos del
/// rango ficticio 99999####.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~PersonasVehiculoTests
/// </code>
/// </example>
public sealed class PersonasVehiculoTests
{
    private const string Copropietario1 = "9999900201";
    private const string Copropietario2 = "9999900202";
    private const string Copropietario3 = "999990203";
    private const string PropietarioLeasing = "999990204";
    private const string Locatario1 = "9999900205";
    private const string Locatario2 = "9999900206";
    private const string LocatarioDeclarado = "9999900207";
    private const string OtroDeclarado = "9999900208";
    private const string PropietarioNatural = "9999900001";
    private const string LocatarioLeasingSimulado = "9999900013";
    private static readonly string[] Nombres = ["LEASING FICTICIO SA", "LOCATARIO PRUEBA DOS", "PERSONA PRUEBA UNO", "EMPRESA LOCATARIA FICTICIA SAS"];
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

    // AC1 (F05-C3) — tres propietarios: tres filas PROPIETARIO con orden 1, 2 y 3, cada documento cifrado y sin nombre.
    [Fact]
    public async Task Copropiedad_guarda_los_tres_propietarios_cifrados()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, _, _) = await ArrancarAsync();
        await using var __ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var protector = Protector(api);

        var lote = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST201")], ct);

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, lote, "TST201", ct);
        var personas = await PersonasAsync(empresa, vehiculo.Id, ct);
        personas.Should().HaveCount(3).And.OnlyContain(p => p.Rol == RolPersona.Propietario && p.Origen == OrigenDocumento.Proveedor);
        personas.Select(p => p.Orden).Should().Equal(1, 2, 3);
        personas.Select(p => p.DocTipo).Should().Equal("CC", "CE", "NIT");
        personas.Select(p => protector.Desproteger(p.DocCifrado)).Should().Equal(Copropietario1, Copropietario2, Copropietario3);
        foreach (var (persona, numero) in personas.Zip([Copropietario1, Copropietario2, Copropietario3]))
        {
            Encoding.UTF8.GetString(persona.DocCifrado).Should().NotContain(numero, "el documento va cifrado");
            Encoding.Latin1.GetString(persona.DocCifrado).Should().NotContain(numero);
        }

        personas.Should().OnlyContain(p => p.TenantId == empresa.Id && p.CreadoEn != default);
        protector.Desproteger(vehiculo.PropietarioDocCifrado!).Should().Be(Copropietario1, "la columna de D2 se conserva con el primero");
    }

    // AC2 (F05-C3) — leasing con dos locatarios del proveedor (uno repetido entre lessee y locatarios) y un declarado
    // distinto: dos filas LOCATARIO de origen PROVEEDOR; el declarado sigue solo en su columna de respaldo.
    [Fact]
    public async Task Leasing_guarda_todos_los_locatarios_del_proveedor_sin_repetir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, _, _) = await ArrancarAsync();
        await using var __ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var protector = Protector(api);

        var lote = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST202", "CC", LocatarioDeclarado)], ct);

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, lote, "TST202", ct);
        var personas = await PersonasAsync(empresa, vehiculo.Id, ct);
        var locatarios = personas.Where(p => p.Rol == RolPersona.Locatario).ToList();
        locatarios.Should().HaveCount(2).And.OnlyContain(p => p.Origen == OrigenDocumento.Proveedor);
        locatarios.Select(p => p.Orden).Should().Equal(1, 2);
        locatarios.Select(p => (p.DocTipo, protector.Desproteger(p.DocCifrado))).Should().Equal(("CC", Locatario1), ("CE", Locatario2));
        personas.Should().ContainSingle(p => p.Rol == RolPersona.Propietario)
            .Which.Should().Match<PersonaVehiculo>(p => p.Orden == 1 && p.DocTipo == "NIT");
        personas.Select(p => protector.Desproteger(p.DocCifrado)).Should().NotContain(LocatarioDeclarado, "el declarado no entra como persona");

        vehiculo.LocatarioDocOrigen.Should().Be(OrigenDocumento.Proveedor);
        protector.Desproteger(vehiculo.LocatarioDocCifrado!).Should().Be(Locatario1);
        vehiculo.LocatarioDeclaradoDocTipo.Should().Be("CC");
        protector.Desproteger(vehiculo.LocatarioDeclaradoDocCifrado!).Should().Be(LocatarioDeclarado, "el declarado sigue en su respaldo");
    }

    // AC2 / decisión 1 (F05-C3) — sin locatario del proveedor, el declarado por el cliente entra como persona con origen
    // DECLARADO_CLIENTE y el mismo cifrado de su columna; sin declarado no hay fila LOCATARIO.
    [Fact]
    public async Task Sin_locatario_del_proveedor_guarda_el_declarado()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, _, _) = await ArrancarAsync();
        await using var __ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var protector = Protector(api);

        var lote = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST001", "CE", LocatarioDeclarado), Lotes.Fila("TST011")], ct);

        var conDeclarado = await MotorRunt.VehiculoAsync(api, empresa, lote, "TST001", ct);
        var personas = await PersonasAsync(empresa, conDeclarado.Id, ct);
        var locatario = personas.Should().ContainSingle(p => p.Rol == RolPersona.Locatario).Subject;
        locatario.Origen.Should().Be(OrigenDocumento.DeclaradoCliente);
        locatario.Orden.Should().Be(1);
        locatario.DocTipo.Should().Be("CE");
        locatario.DocCifrado.Should().Equal(conDeclarado.LocatarioDocCifrado!, "se guarda el mismo cifrado de la columna, sin volver a cifrar");
        protector.Desproteger(locatario.DocCifrado).Should().Be(LocatarioDeclarado);
        protector.Desproteger(personas.Single(p => p.Rol == RolPersona.Propietario).DocCifrado).Should().Be(PropietarioNatural);

        var sinDeclarado = await MotorRunt.VehiculoAsync(api, empresa, lote, "TST011", ct);
        (await PersonasAsync(empresa, sinDeclarado.Id, ct)).Should().ContainSingle().Which.Rol.Should().Be(RolPersona.Propietario);
    }

    // AC3 (F05-C3, F05-C11) — RUNT reutilizado del mismo día: las personas del vehículo nuevo son copia de las del origen con
    // el mismo cifrado; el declarado del origen nunca se copia (entra el del propio vehículo).
    [Fact]
    public async Task Reutilizacion_copia_las_personas_con_el_mismo_cifrado()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, reloj, orden) = await ArrancarAsync();
        await using var __ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var protector = Protector(api);

        IrA(reloj, 10, 0);
        var primero = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST202", "CC", LocatarioDeclarado), Lotes.Fila("TST001", "CC", LocatarioDeclarado)], ct);
        IrA(reloj, 11, 0);
        var segundo = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST202", "CE", OtroDeclarado), Lotes.Fila("TST001", "CE", OtroDeclarado)], ct);

        orden.Should().HaveCount(2, "el segundo lote reutiliza las dos placas");
        (await Motor.CeldasAsync(api, empresa.Id, segundo, ct)).Should().OnlyContain(c => c.ReutilizadaDeId != null);

        var leasingOrigen = await PersonasAsync(empresa, (await MotorRunt.VehiculoAsync(api, empresa, primero, "TST202", ct)).Id, ct);
        var leasingCopia = await PersonasAsync(empresa, (await MotorRunt.VehiculoAsync(api, empresa, segundo, "TST202", ct)).Id, ct);
        leasingCopia.Should().HaveCount(3);
        leasingCopia.Select(Clave).Should().Equal(leasingOrigen.Select(Clave), "misma persona, mismo rol, orden, origen y cifrado");
        leasingCopia.Select(p => p.Id).Should().NotIntersectWith(leasingOrigen.Select(p => p.Id));

        var naturalOrigen = await PersonasAsync(empresa, (await MotorRunt.VehiculoAsync(api, empresa, primero, "TST001", ct)).Id, ct);
        var naturalCopia = await PersonasAsync(empresa, (await MotorRunt.VehiculoAsync(api, empresa, segundo, "TST001", ct)).Id, ct);
        naturalCopia.Single(p => p.Rol == RolPersona.Propietario).DocCifrado
            .Should().Equal(naturalOrigen.Single(p => p.Rol == RolPersona.Propietario).DocCifrado);
        var declarado = naturalCopia.Single(p => p.Rol == RolPersona.Locatario);
        declarado.Origen.Should().Be(OrigenDocumento.DeclaradoCliente);
        declarado.DocTipo.Should().Be("CE");
        protector.Desproteger(declarado.DocCifrado).Should().Be(OtroDeclarado, "el declarado del otro vehículo no se copia");
    }

    // AC4 (F05-C14, prueba de fuga) — dos compañías: cada filtro devuelve solo sus personas, y una persona de B no se puede
    // ligar a un vehículo de A ni por el contexto de datos.
    [Fact]
    public async Task Filtro_de_tenant_aplica_a_personas()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, _, _) = await ArrancarAsync();
        await using var __ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var dos = DosEmpresas.En(api);

        var deA = await LoteProcesadoAsync(api, dos.A, [Lotes.Fila("TST201")], ct);
        var deB = await LoteProcesadoAsync(api, dos.B, [Lotes.Fila("TST202")], ct);
        var vehiculoA = await MotorRunt.VehiculoAsync(api, dos.A, deA, "TST201", ct);
        var vehiculoB = await MotorRunt.VehiculoAsync(api, dos.B, deB, "TST202", ct);

        await using (var db = dos.A.Db())
        {
            var personas = await db.Set<PersonaVehiculo>().AsNoTracking().ToListAsync(ct);
            personas.Should().HaveCount(3).And.OnlyContain(p => p.TenantId == dos.A.Id && p.VehiculoId == vehiculoA.Id);
        }

        await using (var db = dos.B.Db())
        {
            var personas = await db.Set<PersonaVehiculo>().AsNoTracking().ToListAsync(ct);
            personas.Should().HaveCount(3).And.OnlyContain(p => p.TenantId == dos.B.Id && p.VehiculoId == vehiculoB.Id);
            (await db.Set<PersonaVehiculo>().AsNoTracking().CountAsync(p => p.VehiculoId == vehiculoA.Id, ct)).Should().Be(0, "B no ve las de A");

            db.Add(new PersonaVehiculo
            {
                VehiculoId = vehiculoA.Id,
                Rol = RolPersona.Propietario,
                Orden = 9,
                DocTipo = "CC",
                DocCifrado = [0x01],
                Origen = OrigenDocumento.Proveedor,
                CreadoEn = DateTimeOffset.UtcNow,
            });
            var error = (await FluentActions.Awaiting(() => db.SaveChangesAsync(ct)).Should().ThrowAsync<DbUpdateException>()).Which;
            error.InnerException.Should().BeOfType<PostgresException>().Which.ConstraintName.Should().Be("fk_personas_vehiculo_vehiculos");
        }

        (await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.personas_vehiculo", ct)).Should().Be(6);
    }

    // AC1 (F05-C3, PII) — ninguna columna de personas_vehiculo guarda nombres ni documentos en claro (fixture de leasing con
    // nombres en el proveedor y copropiedad).
    [Fact]
    public async Task Ninguna_columna_guarda_nombre_ni_documento_en_claro()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, _, _) = await ArrancarAsync();
        await using var __ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;

        await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST003", "CC", LocatarioDeclarado), Lotes.Fila("TST201"), Lotes.Fila("TST202")], ct);

        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        var columnas = new List<string>();
        await using (var consulta = new NpgsqlCommand(
                         "SELECT column_name FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = 'personas_vehiculo'", conexion))
        await using (var lector = await consulta.ExecuteReaderAsync(ct))
        {
            while (await lector.ReadAsync(ct))
                columnas.Add(lector.GetString(0));
        }

        columnas.Should().BeEquivalentTo(["id", "tenant_id", "vehiculo_id", "rol", "orden", "doc_tipo", "doc_cifrado", "origen", "creado_en"]);

        var filas = new List<(string Json, byte[] Cifrado)>();
        await using (var consulta = new NpgsqlCommand("SELECT row_to_json(p)::text, p.doc_cifrado FROM diagnostico.personas_vehiculo p", conexion))
        await using (var lector = await consulta.ExecuteReaderAsync(ct))
        {
            while (await lector.ReadAsync(ct))
                filas.Add((lector.GetString(0), (byte[])lector[1]));
        }

        filas.Should().HaveCount(8, "TST003: propietario y locatario; TST201: tres; TST202: propietario y dos locatarios");
        string[] documentos =
            [Copropietario1, Copropietario2, Copropietario3, PropietarioLeasing, Locatario1, Locatario2, LocatarioDeclarado, "999990003", LocatarioLeasingSimulado];
        foreach (var (json, cifrado) in filas)
        {
            json.Should().NotContainAny(documentos).And.NotContainAny(Nombres);
            Encoding.UTF8.GetString(cifrado).Should().NotContainAny(documentos);
            Encoding.Latin1.GetString(cifrado).Should().NotContainAny(documentos);
        }
    }

    private static (int, RolPersona, OrigenDocumento, string, string) Clave(PersonaVehiculo p) =>
        (p.Orden, p.Rol, p.Origen, p.DocTipo, Convert.ToHexString(p.DocCifrado));

    private static IProtectorDocumentoPersona Protector(ApiDePrueba api) => api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();

    private static async Task<List<PersonaVehiculo>> PersonasAsync(EmpresaDePrueba empresa, Guid vehiculo, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<PersonaVehiculo>().AsNoTracking()
            .Where(p => p.VehiculoId == vehiculo).OrderBy(p => p.Rol).ThenBy(p => p.Orden).ToListAsync(ct);
    }

    /// <summary>API con la fuente RUNT fija (sobre la simulada de Intempo) espiada y un reloj manual.</summary>
    private static async Task<(ApiDePrueba Api, RelojManual Reloj, ConcurrentQueue<string> Orden)> ArrancarAsync()
    {
        var reloj = new RelojManual();
        var orden = new ConcurrentQueue<string>();
        var api = await MotorRunt.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            new FuenteRuntEspia(orden, new FuenteRuntFija()).Registrar(b.Services);
        });
        return (api, reloj, orden);
    }

    /// <summary>Lote confirmado (alcance RUNT) procesado hasta vaciar la cola; la configuración se siembra solo con el primero.</summary>
    private static async Task<Guid> LoteProcesadoAsync(ApiDePrueba api, EmpresaDePrueba empresa, Dictionary<string, object?>[] filas, CancellationToken ct)
    {
        Guid id;
        await using (var db = empresa.Db())
        {
            if (!await db.Set<ConfiguracionEmpresa>().AsNoTracking().AnyAsync(ct))
            {
                id = await MotorRunt.LoteAsync(api, empresa, filas, ct);
                await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
                return id;
            }
        }

        var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        var creada = await cliente.PostAsync(Cabeceras.Ruta, ConfiguracionOperativaTests.Cuerpo(Cabeceras.Json("Lote repetido", "SIMPLE", null, ["RUNT"])), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var confirmada = await Lotes.ConfirmarAsync(cliente, id, filas, ct);
        confirmada.StatusCode.Should().Be(HttpStatusCode.Accepted, await confirmada.Content.ReadAsStringAsync(ct));
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
        return id;
    }

    /// <summary>Avanza el reloj hasta la próxima hh:mm en hora Colombia (siempre hacia adelante).</summary>
    private static void IrA(RelojManual reloj, int hora, int minuto)
    {
        var ahora = reloj.GetUtcNow();
        var local = ahora.ToOffset(Colombia);
        var destino = new DateTimeOffset(local.Year, local.Month, local.Day, hora, minuto, 0, Colombia);
        if (destino <= ahora)
            destino = destino.AddDays(1);
        reloj.Avanzar(destino - ahora);
    }

    /// <summary>
    /// Fuente RUNT de prueba (HU #13790): TST201 es una copropiedad de tres propietarios y TST202 un leasing con dos locatarios
    /// del proveedor (uno repetido, como llega cuando está en <c>lessee[]</c> y en <c>locatarios[]</c>); el resto delega en la
    /// simulada de Intempo. Documentos del rango ficticio y sin nombres (el RUNT normalizado no los trae).
    /// </summary>
    private sealed class FuenteRuntFija : IFuenteRunt
    {
        private readonly FuenteRuntIntempoSimulada _simulada = new();

        public Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct) => identificador.Valor switch
        {
            "TST201" => Task.FromResult(Vehiculo("TST201", "9ZZPRUEBA00000201",
                [
                    new PropietarioRunt("CC", Copropietario1, TipoPropiedad.Propio, []),
                    new PropietarioRunt("CE", Copropietario2, TipoPropiedad.Propio, []),
                    new PropietarioRunt("NIT", Copropietario3, TipoPropiedad.Propio, []),
                ],
                [])),
            "TST202" => Task.FromResult(Vehiculo("TST202", "9ZZPRUEBA00000202",
                [new PropietarioRunt("NIT", PropietarioLeasing, TipoPropiedad.Leasing, [new LocatarioRunt("CC", Locatario1)])],
                [new LocatarioRunt("CC", Locatario1), new LocatarioRunt("CE", Locatario2), new LocatarioRunt("CC", Locatario1)])),
            _ => _simulada.ConsultarVehiculoAsync(empresa, identificador, ct),
        };

        private static ResultadoRunt Vehiculo(string placa, string vin, IReadOnlyList<PropietarioRunt> propietarios, IReadOnlyList<LocatarioRunt> locatarios) =>
            ResultadoRunt.Exito(new VehiculoRunt(
                placa, vin, "PARTICULAR", "AUTOMOVIL", "SECRETARIA DE TRANSITO DE PRUEBA", propietarios, locatarios, "INTEMPO_SIMULADO", "TX-PRUEBA-" + placa));
    }
}
