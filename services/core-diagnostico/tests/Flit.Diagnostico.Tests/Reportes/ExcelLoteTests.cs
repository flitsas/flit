using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Diagnosticos;
using Flit.Diagnostico.Tests.Plataforma;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ImpuestoDePrueba = Flit.Diagnostico.Tests.Tablero.ImpuestoDePrueba;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// HU #13849 (F07-H4, F07-C5/C6/C8..C11, R-20) — <c>GET /diagnosticos/{id}/excel</c>: el Excel del lote. Las pruebas leen las CELDAS
/// del .xlsx (<see cref="ExcelDePrueba"/>), no solo el código HTTP: hoja «Vehículos» con una fila por vehículo y solo las columnas
/// de las fuentes del alcance; lo no terminado lleva solo su estado; hoja «Consultas» solo con AL_FINALIZAR y el lote cerrado;
/// manual y API dan la misma fila; sin origen, usuario manual, proveedor ni transacción; documentos «****» + 4; texto que empieza por
/// <c>=</c>, <c>+</c>, <c>-</c> o <c>@</c> queda como texto literal; misma huella → mismos bytes y un cambio de celda invalida el
/// anterior; 404 para otra compañía o PROPIOS ajeno, 403 sin permiso y 409 <c>LOTE_SIN_LANZAR</c> con el lote CREADO.
/// Postgres real (base efímera de <see cref="ApiDePrueba"/>); placas TST4## y documentos ficticios cifrados.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Reportes.ExcelLoteTests
/// </code>
/// </example>
public sealed class ExcelLoteTests
{
    private const string PayloadRunt =
        """{"placa":"TST401","vin":"9ZZPRUEBA00000401","servicio":"PARTICULAR","clase":"AUTOMOVIL","organismo":"STRIA PRUEBA","servicio_que_resolvio":"INTEMPO_SIMULADO","id_transaccion":"TX-SECRETA-401","propietarios":[{"tipo_documento":"C","tipo_propiedad":"PROPIA"}],"locatarios":1}""";

    private const string PayloadSimit =
        """{"semaforo":"AMARILLO","cantidad_comparendos":1,"comparendos":[{"numero":"TST-C-0401","fecha":"2026-09-30","valor":"468500","estado":"PENDIENTE","organismo":"Organismo de prueba"}],"transaccion_proveedor_id":"TX-SIMIT-401"}""";

    private const string PayloadSimitReprocesado =
        """{"semaforo":"ROJO","cantidad_comparendos":2,"comparendos":[{"numero":"TST-C-0402","fecha":"2026-10-01","valor":"1200000","estado":"PENDIENTE","organismo":"Organismo de prueba"},{"numero":"TST-C-0403","fecha":"2026-10-02","valor":"604100","estado":"PENDIENTE","organismo":"Organismo de prueba"}]}""";

    private const string PayloadAvaluo =
        """{"sin_valor_comercial":false,"valor_sugerido":48500000,"fuente_principal":"fasecolda","fuentes":[{"fuente":"fasecolda","estado":"OK","valor":48500000,"muestras":3,"fecha":"2026-10-22"}]}""";

    private static readonly string[] Fijas = ["Orden", "Identificador", "Tipo de identificador", "Placa", "VIN", "Estado del vehículo"];

    private static readonly string[] DeRunt =
    [
        "RUNT — Estado", "RUNT — Servicio", "RUNT — Clase", "RUNT — Organismo de tránsito", "RUNT — Marca", "RUNT — Línea", "RUNT — Modelo",
        "RUNT — Propietarios", "RUNT — Locatarios",
    ];

    private static readonly string[] DeSimitVehiculo =
        ["SIMIT vehículo — Estado", "SIMIT vehículo — Semáforo", "SIMIT vehículo — Comparendos", "SIMIT vehículo — Valor total"];

    private static readonly DateTimeOffset Dato = new(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);

    // AC1 (F07-C5) — lote de tres vehículos con alcance RUNT y SIMIT_VEHICULO: «Vehículos» tiene tres filas y solo las columnas fijas,
    // las de RUNT y las de SIMIT_VEHICULO (en el orden de la grilla); adjunto diagnostico-{8}.xlsx sin caché; «Diagnóstico» con el ID,
    // el lote, la empresa, el creador, la fecha y «Diagnóstico Simple».
    [Fact]
    public async Task Una_fila_por_vehiculo_y_columnas_solo_del_alcance()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new UsuariosIdentidadDePrueba();
        await using var api = await ListadoDePrueba.ArrancarAsync(identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var creador = Guid.NewGuid();
        identidad.Nombres[creador] = "Ana Prueba";
        string[] placas = ["TST401", "TST402", "TST403"];
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, creador: creador, nombre: "Flota octubre", alcance: [Fuente.Runt, Fuente.SimitVehiculo],
            vehiculos: [.. placas.Select(p => new Tablero.VehiculoSembrado(p,
                [new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1), new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.Avaluo, EstadoCelda.Pendiente, 1)]))]);
        foreach (var placa in placas)
        {
            await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.Runt), PayloadRunt.Replace("TST401", placa, StringComparison.Ordinal), ct);
            await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.SimitVehiculo), PayloadSimit, ct);
            await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.Avaluo), PayloadAvaluo, ct);
            await TecnicosAsync(empresa, lote.Vehiculos.Single(v => v.Placa == placa).Id, ct);
        }

        var respuesta = await api.Cliente(ExcelDePrueba.Token(api, empresa.Id)).GetAsync(ExcelDePrueba.Ruta(lote.Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await ExcelDePrueba.CuerpoSiNoEsExcel(respuesta, ct));
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be(ExcelDePrueba.ContentType);
        respuesta.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        respuesta.Content.Headers.ContentDisposition.FileName!.Trim('"').Should().Be($"diagnostico-{lote.Id.ToString()[..8]}.xlsx");
        respuesta.Headers.CacheControl!.NoStore.Should().BeTrue("lleva documentos enmascarados y datos de la compañía");
        var libro = ExcelDePrueba.Leer(await respuesta.Content.ReadAsByteArrayAsync(ct));

        libro.Hojas.Keys.Should().Equal(["Vehículos", "Diagnóstico"], "sin AL_FINALIZAR no hay hoja «Consultas»");
        var vehiculos = libro.Hojas["Vehículos"];
        vehiculos[0].Should().Equal([.. Fijas, .. DeRunt, .. DeSimitVehiculo], "solo las columnas de RUNT y SIMIT_VEHICULO, en el orden de la grilla");
        vehiculos.Should().HaveCount(4, "encabezado + una fila por vehículo");
        vehiculos.Skip(1).Select(f => f[1]).Should().Equal(placas, "en el orden de la carga");
        var fila = libro.Fila("Vehículos", "TST401");
        libro.Valor("Vehículos", fila, "Orden").Should().Be("1");
        libro.Valor("Vehículos", fila, "Tipo de identificador").Should().Be("PLACA_CARRO");
        libro.Valor("Vehículos", fila, "Estado del vehículo").Should().Be("Terminado");
        libro.Valor("Vehículos", fila, "RUNT — Estado").Should().Be("Completado");
        libro.Valor("Vehículos", fila, "RUNT — Servicio").Should().Be("PARTICULAR");
        libro.Valor("Vehículos", fila, "RUNT — Clase").Should().Be("AUTOMOVIL");
        libro.Valor("Vehículos", fila, "RUNT — Organismo de tránsito").Should().Be("STRIA PRUEBA");
        libro.Valor("Vehículos", fila, "RUNT — Marca").Should().Be("MARCA PRUEBA");
        libro.Valor("Vehículos", fila, "RUNT — Línea").Should().Be("LINEA PRUEBA");
        libro.Valor("Vehículos", fila, "RUNT — Modelo").Should().Be("2020");
        libro.Valor("Vehículos", fila, "RUNT — Propietarios").Should().Be("1");
        libro.Valor("Vehículos", fila, "RUNT — Locatarios").Should().Be("1");
        libro.Valor("Vehículos", fila, "SIMIT vehículo — Semáforo").Should().Be("AMARILLO");
        libro.Valor("Vehículos", fila, "SIMIT vehículo — Comparendos").Should().Be("1");
        libro.Valor("Vehículos", fila, "SIMIT vehículo — Valor total").Should().Be("468500");
        libro.Texto.Should().NotContainAny(["48500000", "fasecolda", "Avalúo"], "AVALUO no está en el alcance");

        var diagnostico = libro.Hojas["Diagnóstico"];
        diagnostico.Select(f => f[0]).Should().Equal(["Campo", "ID del diagnóstico", "Nombre del lote", "Empresa", "Usuario creador", "Fecha de creación", "Tipo de análisis"]);
        diagnostico.Skip(1).Select(f => f[1]).Should().Equal(
            [lote.Id.ToString(), "Flota octubre", "Empresa de prueba", "Ana Prueba", lote.Cabecera.CreadoEn.ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " (hora Colombia)", "Diagnóstico Simple"]);
    }

    // AC2 (F07-C5) — lote EN_PROCESO: el vehículo terminado trae sus datos; el de celdas PENDIENTE trae solo el estado de cada fuente
    // (columnas de dato vacías); el de SIMIT en ERROR sale «Con error» y «Consulta no disponible», sin tipo de error ni causa.
    [Fact]
    public async Task Lote_en_proceso_exporta_lo_completado_y_el_estado_de_lo_demas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo], vehiculos:
        [
            new("TST411", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)]),
            new("TST412", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)]),
            new("TST413", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)]),
        ]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST411", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST411", Fuente.SimitVehiculo), PayloadSimit, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST413", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST413", Fuente.SimitVehiculo), """{"detalle":"Timeout del proveedor tras 30 s"}""", ct, EstadoCelda.Error, "TIMEOUT");

        var libro = await ExcelDePrueba.LibroOkAsync(api, ExcelDePrueba.Token(api, empresa.Id), lote.Id, ct);

        var terminado = libro.Fila("Vehículos", "TST411");
        libro.Valor("Vehículos", terminado, "Estado del vehículo").Should().Be("Terminado");
        libro.Valor("Vehículos", terminado, "RUNT — Clase").Should().Be("AUTOMOVIL");
        libro.Valor("Vehículos", terminado, "SIMIT vehículo — Semáforo").Should().Be("AMARILLO");

        var pendiente = libro.Fila("Vehículos", "TST412");
        libro.Valor("Vehículos", pendiente, "Estado del vehículo").Should().Be("En proceso");
        libro.Valor("Vehículos", pendiente, "RUNT — Estado").Should().Be("Pendiente");
        libro.Valor("Vehículos", pendiente, "SIMIT vehículo — Estado").Should().Be("Pendiente");
        foreach (var columna in DeRunt.Skip(1).Concat(DeSimitVehiculo.Skip(1)))
            libro.Valor("Vehículos", pendiente, columna).Should().BeEmpty($"lo no terminado lleva solo su estado ({columna})");

        var conError = libro.Fila("Vehículos", "TST413");
        libro.Valor("Vehículos", conError, "Estado del vehículo").Should().Be("Con error");
        libro.Valor("Vehículos", conError, "SIMIT vehículo — Estado").Should().Be("Consulta no disponible");
        libro.Valor("Vehículos", conError, "SIMIT vehículo — Semáforo").Should().BeEmpty();
        libro.Valor("Vehículos", conError, "RUNT — Clase").Should().Be("AUTOMOVIL", "su RUNT sí terminó");
        libro.Texto.Should().NotContainAny(["TIMEOUT", "Timeout", "timeout", "30 s"], "sin código de error ni causa técnica");
    }

    // AC3 (F07-C6) — COMPLETADO con AL_FINALIZAR: hoja «Consultas» con estimadas y consumidas por fuente (intentos sin caché y no
    // manuales; el cálculo de la grilla, #13796). Con NO_MOSTRAR o con el lote EN_PROCESO no hay hoja «Consultas».
    [Fact]
    public async Task Hoja_consultas_solo_con_al_finalizar_y_lote_cerrado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var token = ExcelDePrueba.Token(api, empresa.Id);
        var cerrado = await LoteSimitAsync(empresa, "TST421", ct, estado: EstadoDiagnostico.Completado, estimado: "AL_FINALIZAR");
        var runt = cerrado.Celda("TST421", Fuente.Runt);
        await TableroAyudas.IntentoAsync(empresa, runt, desdeCache: false, ct);
        await TableroAyudas.IntentoAsync(empresa, runt, desdeCache: false, ct);
        await TableroAyudas.IntentoAsync(empresa, runt, desdeCache: true, ct);
        await TableroAyudas.IntentoAsync(empresa, cerrado.Celda("TST421", Fuente.SimitVehiculo), desdeCache: false, ct, DisparadorIntento.Manual);
        var sinMostrar = await LoteSimitAsync(empresa, "TST422", ct, estado: EstadoDiagnostico.Completado, estimado: "NO_MOSTRAR");
        var abierto = await LoteSimitAsync(empresa, "TST423", ct, estado: EstadoDiagnostico.EnProceso, estimado: "AL_FINALIZAR");

        var libro = await ExcelDePrueba.LibroOkAsync(api, token, cerrado.Id, ct);

        libro.Hojas.Keys.Should().Equal(["Vehículos", "Diagnóstico", "Consultas"]);
        var consultas = libro.Hojas["Consultas"];
        consultas[0].Should().Equal(["Fuente", "Consultas estimadas", "Consultas consumidas", "Observación"]);
        consultas.Skip(1).Should().BeEquivalentTo(
            new[] { new[] { "RUNT", "1", "2", string.Empty }, ["SIMIT vehículo", "1", "0", string.Empty] },
            o => o.WithStrictOrdering(), "RUNT: dos sin caché (la de caché no cuenta); SIMIT: la manual no cuenta");
        (await ExcelDePrueba.LibroOkAsync(api, token, sinMostrar.Id, ct)).Hojas.Keys.Should().NotContain("Consultas", "NO_MOSTRAR");
        (await ExcelDePrueba.LibroOkAsync(api, token, abierto.Id, ct)).Hojas.Keys.Should().NotContain("Consultas", "el lote sigue EN_PROCESO");
    }

    // AC4 (F07-C9, R-20) — dos vehículos con los mismos resultados, uno llenado a mano: las filas son idénticas salvo la identificación.
    [Fact]
    public async Task Celda_manual_y_de_api_dan_la_misma_fila()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo, Fuente.Avaluo], vehiculos:
        [
            new("TST431", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1), new(Fuente.Avaluo, EstadoCelda.Pendiente, 1)]),
            new("TST432", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1), new(Fuente.Avaluo, EstadoCelda.Pendiente, 1)]),
        ]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST431", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST432", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST431", Fuente.SimitVehiculo), PayloadSimit, ct);
        var manual = await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST432", Fuente.SimitVehiculo), PayloadSimit, ct, manual: true);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST431", Fuente.Avaluo), PayloadAvaluo, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST432", Fuente.Avaluo), PayloadAvaluo, ct, manual: true);
        await MismaFechaAsync(empresa, lote.Id, ct);

        var libro = await ExcelDePrueba.LibroOkAsync(api, ExcelDePrueba.Token(api, empresa.Id), lote.Id, ct);

        var deApi = libro.Hojas["Vehículos"][libro.Fila("Vehículos", "TST431")];
        var aMano = libro.Hojas["Vehículos"][libro.Fila("Vehículos", "TST432")];
        aMano.Skip(5).Should().Equal(deApi.Skip(5), "salvo orden, identificador, tipo, placa y VIN, la fila manual es la de API");
        libro.Valor("Vehículos", libro.Fila("Vehículos", "TST432"), "Avalúo — Valor sugerido").Should().Be("48500000");
        libro.Texto.Should().NotContain(manual.UsuarioManualId!.Value.ToString());
        libro.Texto.Should().NotContainEquivalentOf("manual");
    }

    // AC4 (F07-C9, R-20) — todas las fuentes con celdas manuales y payloads con proveedor, transacción y nombres: ninguna columna ni
    // celda dice origen, usuario manual, operador, proveedor ni transacción; sí están los datos normalizados.
    [Fact]
    public async Task Sin_columna_de_origen_ni_usuario_manual()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteTodasLasFuentesAsync(api, empresa, "TST441", ct);

        var libro = await ExcelDePrueba.LibroOkAsync(api, ExcelDePrueba.Token(api, empresa.Id), lote.Lote.Id, ct);

        var encabezados = libro.Hojas["Vehículos"][0];
        encabezados.Should().Contain(["RUNT propietarios — Estado", "RUNT locatarios — Documentos", "SIMIT propietarios — Valor total", "Avalúo — Fuente principal"]);
        var fila = libro.Fila("Vehículos", "TST441");
        libro.Valor("Vehículos", fila, "RUNT propietarios — Personas consultadas").Should().Be("1");
        libro.Valor("Vehículos", fila, "RUNT propietarios — Personas con error").Should().Be("0");
        libro.Valor("Vehículos", fila, "SIMIT propietarios — Comparendos").Should().Be("2");
        libro.Valor("Vehículos", fila, "SIMIT propietarios — Valor total").Should().Be("300000");
        libro.Valor("Vehículos", fila, "SIMIT locatarios — Estado").Should().StartWith("No aplica");
        libro.Valor("Vehículos", fila, "Avalúo — Fuente principal").Should().Be("Fasecolda");
        libro.Valor("Vehículos", fila, "Avalúo — Sin valor comercial").Should().Be("No");
        libro.Texto.Should().NotContainAny(["INTEMPO", "TX-", "servicio_que_resolvio", "NOMBRE FICTICIO", "PROVEEDOR"]);
        libro.Texto.Should().NotContainAny([lote.UsuarioManual.ToString(), lote.Propietario.ToString(), lote.Locatario.ToString()]);
        foreach (var prohibido in new[] { "manual", "operador", "proveedor", "transacci", "origen", "backoffice" })
            libro.Texto.Should().NotContainEquivalentOf(prohibido);
    }

    // AC4 (F07-C10) — documentos de propietarios y locatario: «****» + 4 últimos separados por «; », el declarado con su rótulo, nunca
    // el documento completo.
    [Fact]
    public async Task Documentos_salen_enmascarados()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.RuntPropietario, Fuente.RuntLocatario], vehiculos:
            [new("TST451", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.RuntPropietario, EstadoCelda.Pendiente, 1),
                new(Fuente.RuntLocatario, EstadoCelda.Pendiente, 1)])]);
        var vehiculo = lote.Vehiculos.Single().Id;
        var uno = await PersonaAsync(api, empresa, vehiculo, RolPersona.Propietario, 1, "10005678", ct);
        var dos = await PersonaAsync(api, empresa, vehiculo, RolPersona.Propietario, 2, "30009012", ct);
        var locatario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Locatario, 1, "20001234", ct, OrigenDocumento.DeclaradoCliente);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST451", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST451", Fuente.RuntPropietario), Personas(
            (uno.Id, 1, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","licencias":[]}"""),
            (dos.Id, 2, "RUNT_PERSONA", "ERROR", "null")), ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST451", Fuente.RuntLocatario), Personas(
            (locatario.Id, 1, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","licencias":[]}""")), ct);

        var libro = await ExcelDePrueba.LibroOkAsync(api, ExcelDePrueba.Token(api, empresa.Id), lote.Id, ct);

        var fila = libro.Fila("Vehículos", "TST451");
        libro.Valor("Vehículos", fila, "RUNT propietarios — Documentos").Should().Be("C ****5678; C ****9012");
        libro.Valor("Vehículos", fila, "RUNT propietarios — Personas consultadas").Should().Be("1");
        libro.Valor("Vehículos", fila, "RUNT propietarios — Personas con error").Should().Be("1");
        libro.Valor("Vehículos", fila, "RUNT locatarios — Documentos").Should().Be($"C ****1234 ({PersonaReporte.Declarado})");
        libro.Texto.Should().NotContainAny(["10005678", "0005678", "30009012", "0009012", "20001234", "0001234"], "el documento nunca sale completo");
    }

    // Inyección de fórmulas (contexto técnico de la HU) — el nombre del lote y datos del RUNT que empiezan por =, +, - o @ quedan como
    // texto literal: celda de texto, sin fórmula y con el prefijo de comilla (quotePrefix) que impide que Excel la evalúe al editarla.
    [Fact]
    public async Task Texto_que_empieza_por_igual_no_queda_como_formula()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        const string nombre = "=HYPERLINK(\"http://tst.invalid\",\"clic\")";
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, nombre: nombre, alcance: [Fuente.Runt], vehiculos:
            [new("TST461", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celdas.Single(),
            """{"placa":"TST461","servicio":"-2+3","clase":"+CMD","organismo":"@SUM(1+1)","propietarios":[],"locatarios":0}""", ct);

        var libro = await ExcelDePrueba.LibroOkAsync(api, ExcelDePrueba.Token(api, empresa.Id), lote.Id, ct);

        libro.Formulas.Should().Be(0, "ninguna celda lleva fórmula");
        var fila = libro.Fila("Vehículos", "TST461");
        foreach (var (columna, valor) in new[] { ("RUNT — Servicio", "-2+3"), ("RUNT — Clase", "+CMD"), ("RUNT — Organismo de tránsito", "@SUM(1+1)") })
        {
            libro.Valor("Vehículos", fila, columna).Should().Be(valor, "el texto sale literal");
            libro.ComoTextoLiteral("Vehículos", fila, columna).Should().BeTrue($"{columna} es texto con quotePrefix");
        }

        var nombreLote = libro.Hojas["Diagnóstico"].FindIndex(f => f[0] == "Nombre del lote");
        libro.Valor("Diagnóstico", nombreLote, "Valor").Should().Be(nombre);
        libro.ComoTextoLiteral("Diagnóstico", nombreLote, "Valor").Should().BeTrue();
        libro.ComoTextoLiteral("Vehículos", fila, "Identificador").Should().BeFalse("solo el texto peligroso lleva el prefijo");
    }

    // AC5 (F07-C8) — pedido dos veces sin cambios: los mismos bytes y una sola evidencia XLSX; después de un llenado manual, uno nuevo
    // con el dato nuevo y el anterior no vigente (conservado).
    [Fact]
    public async Task Misma_huella_no_regenera_y_un_cambio_de_celda_si()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteSimitAsync(empresa, "TST471", ct);
        var cliente = api.Cliente(ExcelDePrueba.Token(api, empresa.Id));

        var primero = await ExcelDePrueba.BytesOkAsync(cliente, lote.Id, ct);
        var primera = await VigenteAsync(empresa, lote.Id, ct);
        await Task.Delay(TimeSpan.FromMilliseconds(1100), ct);
        var segundo = await ExcelDePrueba.BytesOkAsync(cliente, lote.Id, ct);

        segundo.Should().Equal(primero, "misma huella: los bytes guardados, sin regenerar");
        (await EvidenciasAsync(empresa, lote.Id, ct)).Should().ContainSingle().Which.Id.Should().Be(primera.Id);
        primera.Tipo.Should().Be(TipoEvidencia.Xlsx);
        primera.VehiculoId.Should().BeNull("el Excel es del lote");

        await CambiarCeldaManualAsync(empresa, lote.Celda("TST471", Fuente.SimitVehiculo).Id, PayloadSimitReprocesado, ct);
        var tercero = await ExcelDePrueba.BytesOkAsync(cliente, lote.Id, ct);
        var segunda = await VigenteAsync(empresa, lote.Id, ct);

        tercero.Should().NotEqual(primero);
        var libro = ExcelDePrueba.Leer(tercero);
        var fila = libro.Fila("Vehículos", "TST471");
        libro.Valor("Vehículos", fila, "SIMIT vehículo — Comparendos").Should().Be("2", "el Excel nuevo trae el dato del llenado manual");
        libro.Valor("Vehículos", fila, "SIMIT vehículo — Valor total").Should().Be("1804100");
        segunda.Id.Should().NotBe(primera.Id);
        segunda.Huella.Should().NotBe(primera.Huella);
        var todas = await EvidenciasAsync(empresa, lote.Id, ct);
        todas.Should().HaveCount(2, "la anterior se conserva");
        todas.Single(e => e.Id == primera.Id).Vigente.Should().BeFalse();
    }

    // AC6 (F07-C11, negativo) — desde A: el Excel de un lote de B y (con PROPIOS) el de otro usuario de A responden 404 NO_ENCONTRADO
    // sin datos y sin generar; sin diagnostico.reportes.descargar, 403 sin cuerpo.
    [Fact]
    public async Task Otra_compania_responde_404_y_sin_permiso_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var deB = await LoteSimitAsync(empresas.B, "TST481", ct, nombre: "Lote secreto de B");
        var creador = ExcelDePrueba.Token(api, empresas.A.Id);
        var otro = ExcelDePrueba.Token(api, empresas.A.Id);
        var deA = await LoteSimitAsync(empresas.A, "TST482", ct, creador: TableroAyudas.Usuario(creador));

        var ajena = await api.Cliente(creador).GetAsync(ExcelDePrueba.Ruta(deB.Id), ct);
        var cuerpo = await ajena.Content.ReadAsStringAsync(ct);
        ajena.StatusCode.Should().Be(HttpStatusCode.NotFound, cuerpo);
        JsonDocument.Parse(cuerpo).RootElement.GetProperty("code").GetString().Should().Be("NO_ENCONTRADO");
        cuerpo.Should().NotContainAny(["Lote secreto de B", "TST481", deB.Vehiculos.Single().Id.ToString(), empresas.B.Id.ToString()]);

        await ConfiguracionPropiosAsync(empresas.A, ct);
        var propiosAjeno = await api.Cliente(otro).GetAsync(ExcelDePrueba.Ruta(deA.Id), ct);
        propiosAjeno.StatusCode.Should().Be(HttpStatusCode.NotFound, "PROPIOS: el lote es de otro usuario");
        (await propiosAjeno.Content.ReadAsStringAsync(ct)).Should().NotContain("TST482");
        (await api.Cliente(creador).GetAsync(ExcelDePrueba.Ruta(deA.Id), ct)).StatusCode.Should().Be(HttpStatusCode.OK, "el creador sí descarga");

        var soloVer = api.Token(empresa: empresas.A.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);
        var sinPermiso = await api.Cliente(soloVer).GetAsync(ExcelDePrueba.Ruta(deA.Id), ct);
        sinPermiso.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sinPermiso.Content.ReadAsStringAsync(ct)).Should().BeEmpty();
        await using var db = empresas.B.Db();
        (await db.Set<Evidencia>().CountAsync(ct)).Should().Be(0, "nada se generó para B");
    }

    // Contexto técnico — lote CREADO (sin vehículos lanzados): 409 LOTE_SIN_LANZAR sin generar; uno de otra compañía en CREADO sigue
    // siendo 404 (el 404 se comprueba antes que el 409).
    [Fact]
    public async Task Lote_sin_lanzar_responde_409_y_el_ajeno_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var creado = await TableroAyudas.SembrarAsync(empresas.A, ct, estado: EstadoDiagnostico.Creado, vehiculos: [new("TST491", [])]);
        var deB = await TableroAyudas.SembrarAsync(empresas.B, ct, estado: EstadoDiagnostico.Creado, vehiculos: [new("TST492", [])]);
        var token = ExcelDePrueba.Token(api, empresas.A.Id);

        var respuesta = await api.Cliente(token).GetAsync(ExcelDePrueba.Ruta(creado.Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("LOTE_SIN_LANZAR");
        (await api.Cliente(token).GetAsync(ExcelDePrueba.Ruta(deB.Id), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var db = empresas.A.Db();
        (await db.Set<Evidencia>().CountAsync(ct)).Should().Be(0);
    }

    // HU #13864 AC3 / AC4 (F09-C9, Q6 = a, R-20) — lote con el impuesto en el alcance: después de las de RUNT van «Impuesto
    // departamental — Estado», «— Total adeudado» (número en pesos) y «— Vigencias pendientes» (cantidad de no pagadas); el vehículo
    // en N/A DEPARTAMENTO_NO_SOPORTADO lleva el texto del motivo en la columna de estado y los datos vacíos; ninguna celda trae el
    // proveedor inventado, la transacción ni el servicio del intento (fuga) y ninguna es fórmula. Un lote sin el impuesto en el
    // alcance (aunque tenga la celda) no tiene ninguna de esas columnas.
    [Fact]
    public async Task Columnas_del_impuesto_solo_con_la_fuente_en_el_alcance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var conImpuesto = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.ImpuestoDepartamental], vehiculos:
        [
            new("TST471", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.ImpuestoDepartamental, EstadoCelda.Pendiente, 1)]),
            new("TST472", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.ImpuestoDepartamental, EstadoCelda.Pendiente, 1)]),
        ]);
        foreach (var placa in new[] { "TST471", "TST472" })
            await TableroAyudas.PayloadAsync(empresa, conImpuesto.Celda(placa, Fuente.Runt), PayloadRunt.Replace("TST401", placa, StringComparison.Ordinal), ct);
        var impuesto = conImpuesto.Celda("TST471", Fuente.ImpuestoDepartamental);
        await TableroAyudas.PayloadAsync(empresa, impuesto, ImpuestoDePrueba.ConVigencias, ct);
        await TableroAyudas.IntentoAsync(empresa, impuesto, desdeCache: false, ct);
        await ImpuestoDePrueba.NoAplicaAsync(empresa, conImpuesto.Celda("TST472", Fuente.ImpuestoDepartamental), MotivoNa.DepartamentoNoSoportado, ct);
        var token = ExcelDePrueba.Token(api, empresa.Id);

        var libro = await ExcelDePrueba.LibroOkAsync(api, token, conImpuesto.Id, ct);

        libro.Hojas["Vehículos"][0].Should().Equal(
            [.. Fijas, .. DeRunt, "Impuesto departamental — Estado", "Impuesto departamental — Total adeudado", "Impuesto departamental — Vigencias pendientes"],
            "las columnas del impuesto van después de RUNT, en el orden de la grilla");
        var completado = libro.Fila("Vehículos", "TST471");
        libro.Valor("Vehículos", completado, "Impuesto departamental — Estado").Should().Be("Completado");
        libro.Valor("Vehículos", completado, "Impuesto departamental — Total adeudado").Should().Be("350000", "pesos enteros, como número");
        libro.Valor("Vehículos", completado, "Impuesto departamental — Vigencias pendientes").Should().Be("1", "2026 pendiente; 2025 pagada no cuenta");
        var na = libro.Fila("Vehículos", "TST472");
        libro.Valor("Vehículos", na, "Impuesto departamental — Estado").Should().Be("No aplica: " + ImpuestoDePrueba.TextoDepartamentoNoSoportado);
        libro.Valor("Vehículos", na, "Impuesto departamental — Total adeudado").Should().BeEmpty();
        libro.Valor("Vehículos", na, "Impuesto departamental — Vigencias pendientes").Should().BeEmpty();
        libro.Valor("Vehículos", na, "Estado del vehículo").Should().Be("Terminado", "N/A cuenta como terminado");
        libro.Formulas.Should().Be(0);
        libro.Texto.Should().NotContainAny([ImpuestoDePrueba.ProveedorFicticio, ImpuestoDePrueba.TransaccionFicticia, "INTEMPO", "departamento_codigo"]);
        foreach (var prohibido in new[] { "proveedor", "transacci", "origen" })
            libro.Texto.Should().NotContainEquivalentOf(prohibido);

        var sinImpuesto = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt], vehiculos:
            [new("TST473", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.ImpuestoDepartamental, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresa, sinImpuesto.Celda("TST473", Fuente.Runt), PayloadRunt.Replace("TST401", "TST473", StringComparison.Ordinal), ct);
        await TableroAyudas.PayloadAsync(empresa, sinImpuesto.Celda("TST473", Fuente.ImpuestoDepartamental), ImpuestoDePrueba.ConVigencias, ct);

        var sinLibro = await ExcelDePrueba.LibroOkAsync(api, token, sinImpuesto.Id, ct);

        sinLibro.Hojas["Vehículos"][0].Should().Equal([.. Fijas, .. DeRunt], "sin el impuesto en el alcance no hay columnas del impuesto");
        sinLibro.Texto.Should().NotContainAny(["Impuesto departamental", "Total adeudado", "Vigencias pendientes", "350000"]);
    }

    /// <summary>Lote con un vehículo con RUNT y SIMIT_VEHICULO COMPLETADO.</summary>
    private static async Task<Tablero.LoteSembrado> LoteSimitAsync(
        EmpresaDePrueba empresa, string placa, CancellationToken ct, string nombre = "Lote del Excel", Guid? creador = null,
        EstadoDiagnostico estado = EstadoDiagnostico.EnProceso, string estimado = "NO_MOSTRAR")
    {
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo], nombre: nombre, creador: creador,
            estado: estado, estimado: estimado,
            vehiculos: [new(placa, [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.Runt), PayloadRunt.Replace("TST401", placa, StringComparison.Ordinal), ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.SimitVehiculo), PayloadSimit, ct);
        return lote;
    }

    /// <summary>Un vehículo con todas las fuentes: celdas manuales y payloads que traen proveedor, transacción y nombres.</summary>
    private static async Task<(Tablero.LoteSembrado Lote, Guid UsuarioManual, Guid Propietario, Guid Locatario)> LoteTodasLasFuentesAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, string placa, CancellationToken ct)
    {
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance:
            [Fuente.Runt, Fuente.RuntPropietario, Fuente.RuntLocatario, Fuente.SimitVehiculo, Fuente.SimitPropietario, Fuente.SimitLocatario, Fuente.Avaluo],
            vehiculos:
            [new(placa, [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.RuntPropietario, EstadoCelda.Pendiente, 1),
                new(Fuente.RuntLocatario, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1),
                new(Fuente.SimitPropietario, EstadoCelda.Pendiente, 1), new(Fuente.SimitLocatario, EstadoCelda.Na, 1), new(Fuente.Avaluo, EstadoCelda.Pendiente, 1)])]);
        var vehiculo = lote.Vehiculos.Single().Id;
        var propietario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Propietario, 1, "10005678", ct);
        var locatario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Locatario, 1, "20001234", ct, OrigenDocumento.DeclaradoCliente);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.Runt), PayloadRunt, ct, manual: true);
        var manual = await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.SimitVehiculo), PayloadSimit, ct, manual: true);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.Avaluo), PayloadAvaluo, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.RuntPropietario), Personas(
            (propietario.Id, 1, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","nombre":"NOMBRE FICTICIO","proveedor":"INTEMPO","licencias":[]}""")), ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.RuntLocatario), Personas(
            (locatario.Id, 1, "RUNT_PERSONA", "OK", """{"inscrito":false,"estado_conductor":null,"licencias":[]}""")), ct, manual: true);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.SimitPropietario), Personas(
            (propietario.Id, 1, "SIMIT", "OK", """{"semaforo":"ROJO","cantidad_comparendos":2,"comparendos":[{"numero":"TST-C-0441","valor":"100000"},{"numero":"TST-C-0442","valor":"200000"}],"id_transaccion":"TX-PERSONA-441"}""")), ct);
        return (lote, manual.UsuarioManualId!.Value, propietario.Id, locatario.Id);
    }

    /// <summary>Datos técnicos del bien que el RUNT deja en <c>datos_consolidados.vehiculo</c> (HU #13795).</summary>
    private static async Task TecnicosAsync(EmpresaDePrueba empresa, Guid vehiculo, CancellationToken ct)
    {
        await using var db = empresa.Db();
        var fila = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.Id == vehiculo, ct);
        fila.DatosConsolidados = """{"vehiculo":{"marca":"MARCA PRUEBA","linea":"LINEA PRUEBA","modelo":2020,"cilindraje":1600}}""";
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Llenado manual de una celda: nuevo payload, nueva fecha y la versión del tablero sube.</summary>
    private static async Task CambiarCeldaManualAsync(EmpresaDePrueba empresa, Guid celda, string payload, CancellationToken ct)
    {
        await using var db = empresa.Db();
        var fila = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == celda, ct);
        fila.Payload = payload;
        fila.Origen = OrigenCelda.Manual;
        fila.UsuarioManualId = Guid.NewGuid();
        fila.DatoObtenidoEn = DateTimeOffset.UtcNow;
        fila.VersionTablero += 1;
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Evidencia> VigenteAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<Evidencia>().AsNoTracking().SingleAsync(e => e.DiagnosticoId == lote && e.Tipo == TipoEvidencia.Xlsx && e.Vigente, ct);
    }

    private static async Task<List<Evidencia>> EvidenciasAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<Evidencia>().AsNoTracking().Where(e => e.DiagnosticoId == lote && e.Tipo == TipoEvidencia.Xlsx).ToListAsync(ct);
    }

    private static async Task ConfiguracionPropiosAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        await using var db = empresa.Db();
        db.Add(new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios });
        await db.SaveChangesAsync(ct);
    }

    private static async Task MismaFechaAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        await db.Set<CeldaConsulta>().Where(c => c.DiagnosticoId == lote).ExecuteUpdateAsync(s => s.SetProperty(c => c.DatoObtenidoEn, Dato), ct);
    }

    private static async Task<PersonaVehiculo> PersonaAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, Guid vehiculo, RolPersona rol, int orden, string numero, CancellationToken ct,
        OrigenDocumento origen = OrigenDocumento.Proveedor)
    {
        var persona = new PersonaVehiculo
        {
            VehiculoId = vehiculo,
            Rol = rol,
            Orden = orden,
            DocTipo = "C",
            DocCifrado = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Proteger(numero),
            Origen = origen,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        await using var db = empresa.Db();
        db.Add(persona);
        await db.SaveChangesAsync(ct);
        return persona;
    }

    /// <summary>Payload de una celda de persona con la forma de <c>EjecutorPorPersonas</c>.</summary>
    private static string Personas(params (Guid Id, int Orden, string Consulta, string Estado, string Resultado)[] personas) =>
        "{\"personas\":[" + string.Join(',', personas.Select(p =>
            $$"""{"persona_id":"{{p.Id}}","orden":{{p.Orden}},"consulta":"{{p.Consulta}}","estado":"{{p.Estado}}","resultado":{{p.Resultado}},"consultado_en":"2026-10-22T15:00:00+00:00"}""")) + "]}";
}

/// <summary>
/// Ayudas del Excel del lote (HU #13849): la ruta, un token con <c>reportes.descargar</c> y la lectura de las CELDAS del .xlsx con OpenXml
/// (texto en línea o <c>sharedStrings</c>, número tal cual). También es el lector del cuerpo del escenario de fuga
/// (<see cref="EscenariosDeFuga.RutaExcelLote"/>): un cuerpo que no es .xlsx (el 404) se devuelve tal cual.
/// </summary>
/// <example>
/// <code>
/// var libro = await ExcelDePrueba.LibroOkAsync(api, ExcelDePrueba.Token(api, empresa.Id), lote.Id, ct);
/// libro.Valor("Vehículos", libro.Fila("Vehículos", "TST401"), "RUNT — Clase").Should().Be("AUTOMOVIL");
/// </code>
/// </example>
internal static class ExcelDePrueba
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static string Ruta(Guid id) => $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{id}/excel";

    public static string Token(ApiDePrueba api, Guid empresa) =>
        api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosVer, Permisos.ReportesDescargar], roles: ["usuario_diagnostico"]);

    /// <summary>Todas las celdas del libro como texto (una por línea); si la respuesta no es un .xlsx, el cuerpo tal cual.</summary>
    public static async Task<string> TextoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        if (respuesta.Content.Headers.ContentType?.MediaType != ContentType)
            return await respuesta.Content.ReadAsStringAsync(ct);
        return Leer(await respuesta.Content.ReadAsByteArrayAsync(ct)).Texto;
    }

    public static async Task<string> CuerpoSiNoEsExcel(HttpResponseMessage respuesta, CancellationToken ct) =>
        respuesta.Content.Headers.ContentType?.MediaType == ContentType ? "(xlsx)" : await respuesta.Content.ReadAsStringAsync(ct);

    public static async Task<byte[]> BytesOkAsync(HttpClient cliente, Guid id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        var respuesta = await cliente.GetAsync(Ruta(id), ct);
        var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, System.Text.Encoding.UTF8.GetString(bytes));
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be(ContentType);
        return bytes;
    }

    public static async Task<LibroLeido> LibroOkAsync(ApiDePrueba api, string token, Guid id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(api);
        return Leer(await BytesOkAsync(api.Cliente(token), id, ct));
    }

    /// <summary>Lee cada hoja (en el orden del libro) como filas de texto; cuenta las fórmulas y marca las celdas con quotePrefix.</summary>
    public static LibroLeido Leer(byte[] xlsx)
    {
        using var flujo = new MemoryStream(xlsx);
        using var documento = SpreadsheetDocument.Open(flujo, false);
        var libro = documento.WorkbookPart!;
        var compartidas = libro.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];
        var formatos = libro.WorkbookStylesPart?.Stylesheet.CellFormats?.Elements<CellFormat>().ToList() ?? [];
        var hojas = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        var literales = new HashSet<(string, int, int)>();
        var formulas = 0;
        foreach (var hoja in libro.Workbook.Sheets!.Elements<Sheet>())
        {
            var parte = (WorksheetPart)libro.GetPartById(hoja.Id!);
            var filas = new List<string[]>();
            foreach (var fila in parte.Worksheet.Descendants<Row>())
            {
                var valores = new List<string>();
                foreach (var celda in fila.Elements<Cell>())
                {
                    var columna = celda.CellReference?.Value is { } referencia ? Columna(referencia) : valores.Count;
                    while (valores.Count < columna)
                        valores.Add(string.Empty);
                    if (celda.CellFormula is not null)
                        formulas++;
                    var texto = celda.DataType?.Value == CellValues.InlineString ? celda.InlineString?.InnerText ?? string.Empty
                        : celda.DataType?.Value == CellValues.SharedString ? compartidas[int.Parse(celda.CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture)]
                        : celda.CellValue?.Text ?? string.Empty;
                    if (celda.StyleIndex?.Value is { } estilo && (int)estilo < formatos.Count && formatos[(int)estilo].QuotePrefix?.Value == true)
                        literales.Add((hoja.Name!.Value!, filas.Count, valores.Count));
                    valores.Add(texto);
                }

                filas.Add([.. valores]);
            }

            hojas[hoja.Name!.Value!] = filas;
        }

        return new LibroLeido(hojas, literales, formulas);
    }

    private static int Columna(string referencia)
    {
        var indice = 0;
        foreach (var c in referencia.TakeWhile(char.IsLetter))
            indice = (indice * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        return indice - 1;
    }
}

/// <summary>El libro leído: filas de texto por hoja (fila 0 = encabezado), celdas con quotePrefix y cantidad de fórmulas.</summary>
internal sealed class LibroLeido(Dictionary<string, List<string[]>> hojas, HashSet<(string, int, int)> literales, int formulas)
{
    public Dictionary<string, List<string[]>> Hojas { get; } = hojas;

    public int Formulas { get; } = formulas;

    /// <summary>Todas las celdas de todas las hojas, una por línea.</summary>
    public string Texto => string.Join('\n', Hojas.Values.SelectMany(f => f).SelectMany(f => f));

    /// <summary>Índice de la fila cuya columna «Identificador» (o la primera) es <paramref name="identificador"/>.</summary>
    public int Fila(string hoja, string identificador)
    {
        var filas = Hojas[hoja];
        var columna = Math.Max(0, Array.IndexOf(filas[0], "Identificador"));
        var indice = filas.FindIndex(1, f => f.Length > columna && f[columna] == identificador);
        if (indice < 0)
            throw new InvalidOperationException($"La hoja «{hoja}» no tiene la fila «{identificador}»: {Texto}");
        return indice;
    }

    public string Valor(string hoja, int fila, string columna)
    {
        var indice = IndiceColumna(hoja, columna);
        var valores = Hojas[hoja][fila];
        return indice < valores.Length ? valores[indice] : string.Empty;
    }

    public bool ComoTextoLiteral(string hoja, int fila, string columna) => literales.Contains((hoja, fila, IndiceColumna(hoja, columna)));

    private int IndiceColumna(string hoja, string columna)
    {
        var indice = Array.IndexOf(Hojas[hoja][0], columna);
        if (indice < 0)
            throw new InvalidOperationException($"La hoja «{hoja}» no tiene la columna «{columna}»: {string.Join(" | ", Hojas[hoja][0])}");
        return indice;
    }
}
