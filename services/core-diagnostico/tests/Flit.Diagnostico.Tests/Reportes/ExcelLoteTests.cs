using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Infraestructura.Excel;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
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

    // HU #14752 (Excel v4, contrato-excel §1): «ID del vehículo» al final de las fijas.
    private static readonly string[] Fijas =
    [
        "Orden", "Identificador", "Tipo de identificador", "Placa", "VIN", "Propietario actual declarado", "Cambio de propietario", "Estado del vehículo",
        "ID del vehículo",
    ];

    /// <summary>Los 25 rótulos nuevos del RUNT (columnas-excel-v1.md, aprobados), en el orden del contrato §2.1.</summary>
    internal static readonly string[] NuevasRunt =
    [
        "Tipo de documento del propietario", "Número de documento del propietario", "Tipo de documento del locatario",
        "Número de documento del locatario", "Nombre del locatario", "Estado SOAT", "Vencimiento SOAT", "Estado RTM", "Vencimiento RTM",
        "Garantías mobiliarias", "Garantías a favor de", "Limitaciones a la propiedad", "Accidentalidad", "Blindaje", "Nivel de blindaje",
        "Fecha de matrícula", "Cilindraje", "Clasificación", "Color", "Estado del vehículo en el RUNT", "N.º de chasis", "N.º de motor",
        "Tiene gravámenes", "Tipo de carrocería", "Tipo de combustible",
    ];

    internal static readonly string[] NuevasSimitVehiculo =
        ["Cantidad de resoluciones", "Estado de la placa", "Números de comparendo", "Fechas de comparendo", "Valores a pagar", "Total de infracciones"];

    internal static readonly string[] NuevasSimitLocatario = ["Detalle de comparendos del locatario", "Total de comparendos del locatario"];

    /// <summary>La cola sin fuente (D2), siempre presente y vacía, en el orden de v1.</summary>
    internal static readonly string[] SinFuente =
    [
        "Tipo de locatario", "Normalización / saneamiento", "N.º de serie", "Licencia de tránsito de importación", "Empresa transportadora",
        "Tomador póliza RC", "Análisis de saneamiento",
    ];

    private static readonly string[] DeRunt =
    [
        "RUNT — Estado", "RUNT — Servicio", "RUNT — Clase", "RUNT — Organismo de tránsito", "RUNT — Marca", "RUNT — Línea", "RUNT — Modelo",
        "RUNT — Propietarios", "RUNT — Locatarios", .. NuevasRunt.Select(r => "RUNT — " + r),
    ];

    private static readonly string[] DeSimitVehiculo =
    [
        "SIMIT vehículo — Estado", "SIMIT vehículo — Semáforo", "SIMIT vehículo — Comparendos", "SIMIT vehículo — Valor total",
        .. NuevasSimitVehiculo.Select(r => "SIMIT vehículo — " + r),
    ];

    private static readonly string[] DeImpuesto =
    [
        "Impuesto departamental — Estado", "Impuesto departamental — Total adeudado", "Impuesto departamental — Vigencias pendientes",
        "Impuesto departamental — Estado del impuesto",
    ];

    /// <summary>Aviso D4 de la hoja «Diagnóstico» (literal exacto, decisiones.md D4).</summary>
    internal const string AvisoD4 = "Algunas consultas son anteriores al detalle completo de la fuente: sus columnas ampliadas pueden ir vacías";

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
        vehiculos[0].Should().Equal([.. Fijas, .. DeRunt, .. DeSimitVehiculo, .. SinFuente], "solo las columnas de RUNT y SIMIT_VEHICULO, en el orden de la grilla, y la cola sin fuente");
        vehiculos.Should().HaveCount(4, "encabezado + una fila por vehículo");
        vehiculos.Skip(1).Select(f => f[1]).Should().Equal(placas, "en el orden de la carga");
        var fila = libro.Fila("Vehículos", "TST401");
        libro.Valor("Vehículos", fila, "Orden").Should().Be("1");
        libro.Valor("Vehículos", fila, "Tipo de identificador").Should().Be("Placa de carro");
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
        // HU #14752 D4: los RUNT completados sin respuesta guardada (celdas anteriores a #14087) traen la fila «Aviso».
        diagnostico.Select(f => f[0]).Should().Equal(["Campo", "ID del diagnóstico", "Nombre del lote", "Empresa", "Usuario creador", "Fecha de creación", "Tipo de análisis", "Aviso"]);
        diagnostico.Skip(1).Select(f => f[1]).Should().Equal(
            [lote.Id.ToString(), "Flota octubre", "Empresa de prueba", "Ana Prueba", lote.Cabecera.CreadoEn.ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " (hora Colombia)", "Diagnóstico Simple", AvisoD4]);
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
        var id = Array.IndexOf(libro.Hojas["Vehículos"][0], "ID del vehículo");
        aMano.Where((_, i) => i != id).Skip(5).Should().Equal(deApi.Where((_, i) => i != id).Skip(5),
            "salvo orden, identificador, tipo, placa, VIN e ID del vehículo, la fila manual es la de API");
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
        var historico = await PersonaAsync(api, empresa, vehiculo, RolPersona.Locatario, 1, "20009999", ct, OrigenDocumento.DeclaradoCliente);
        var locatario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Locatario, 2, "20001234", ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST451", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST451", Fuente.RuntPropietario), Personas(
            (uno.Id, 1, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","licencias":[]}"""),
            (dos.Id, 2, "RUNT_PERSONA", "ERROR", "null")), ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST451", Fuente.RuntLocatario), Personas(
            (historico.Id, 1, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","licencias":[]}"""),
            (locatario.Id, 2, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","licencias":[]}""")), ct);

        var libro = await ExcelDePrueba.LibroOkAsync(api, ExcelDePrueba.Token(api, empresa.Id), lote.Id, ct);

        var fila = libro.Fila("Vehículos", "TST451");
        libro.Valor("Vehículos", fila, "RUNT propietarios — Documentos").Should().Be("C ****5678; C ****9012");
        libro.Valor("Vehículos", fila, "RUNT propietarios — Personas consultadas").Should().Be("1");
        libro.Valor("Vehículos", fila, "RUNT propietarios — Personas con error").Should().Be("1");
        libro.Valor("Vehículos", fila, "RUNT locatarios — Documentos").Should()
            .Be("C ****1234", "HU #14582 §1.3: la persona histórica DECLARADO_CLIENTE no se pinta como locatario y sin el sufijo de declarado");
        libro.Valor("Vehículos", fila, "RUNT locatarios — Personas consultadas").Should().Be("1");
        libro.Texto.Should().NotContain(PersonaReporte.Declarado).And.NotContain("****9999");
        libro.Texto.Should().NotContainAny(["10005678", "0005678", "30009012", "0009012", "20001234", "0001234"], "el documento nunca sale completo");
    }

    // HU #14582 (contrato §1.3) — columnas fijas «Propietario actual declarado» («TIPO ****1234», enmascarado como el resto de la
    // hoja) y «Cambio de propietario» (Sí / No / vacío), leídas de la columna persistida sin recalcular (SP-1).
    [Fact]
    public async Task HU14582_propietario_declarado_enmascarado_y_cambio_de_propietario()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        Flit.Diagnostico.Tests.Tablero.CeldaSembrada[] runt = [new(Fuente.Runt, EstadoCelda.Pendiente, 1)];
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt], vehiculos:
            [new("TST481", runt), new("TST482", runt), new("TST483", runt)]);
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        await using (var db = empresa.Db())
        {
            var filas = await db.Set<VehiculoDiagnostico>().Where(v => v.DiagnosticoId == lote.Id).ToDictionaryAsync(v => v.IdentificadorNorm, ct);
            filas["TST481"].PropietarioDeclaradoDocTipo = "CC";
            filas["TST481"].PropietarioDeclaradoDocCifrado = protector.Proteger("1020304050");
            filas["TST481"].CambioPropietario = true;
            filas["TST482"].PropietarioDeclaradoDocTipo = "NIT";
            filas["TST482"].PropietarioDeclaradoDocCifrado = protector.Proteger("900123456");
            filas["TST482"].CambioPropietario = false;
            await db.SaveChangesAsync(ct);
        }

        var libro = await ExcelDePrueba.LibroOkAsync(api, ExcelDePrueba.Token(api, empresa.Id), lote.Id, ct);

        libro.Valor("Vehículos", libro.Fila("Vehículos", "TST481"), "Propietario actual declarado").Should().Be("CC ****4050");
        libro.Valor("Vehículos", libro.Fila("Vehículos", "TST481"), "Cambio de propietario").Should().Be("Sí");
        libro.Valor("Vehículos", libro.Fila("Vehículos", "TST482"), "Propietario actual declarado").Should().Be("NIT ****3456");
        libro.Valor("Vehículos", libro.Fila("Vehículos", "TST482"), "Cambio de propietario").Should().Be("No");
        libro.Valor("Vehículos", libro.Fila("Vehículos", "TST483"), "Propietario actual declarado").Should().BeNullOrEmpty();
        libro.Valor("Vehículos", libro.Fila("Vehículos", "TST483"), "Cambio de propietario").Should().BeNullOrEmpty();
        libro.Texto.Should().NotContainAny(["1020304050", "900123456"], "el declarado nunca sale completo en el Excel del lote");
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
        JsonElement.Parse(cuerpo).GetProperty("code").GetString().Should().Be("NO_ENCONTRADO");
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
            [.. Fijas, .. DeRunt, .. DeImpuesto, .. SinFuente],
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

        sinLibro.Hojas["Vehículos"][0].Should().Equal([.. Fijas, .. DeRunt, .. SinFuente], "sin el impuesto en el alcance no hay columnas del impuesto");
        sinLibro.Texto.Should().NotContainAny(["Impuesto departamental", "Total adeudado", "Vigencias pendientes", "350000"]);
    }

    // HU #14752 AC1 / SP-11 (contrato-excel §1, columnas-excel-v1.md) — snapshot de los rótulos v4: fijas con «ID del vehículo», los
    // nuevos de cada fuente al final de su lista, la cola sin fuente al final y la lista EN CLARO cerrada (6 columnas, D3).
    [Fact]
    public void HU14752_rotulos_v4_contra_columnas_v1_y_lista_en_claro_cerrada()
    {
        ColumnasExcel.Fijas.Should().Equal(Fijas);
        ColumnasExcel.DatosDe("RUNT").Skip(8).Should().Equal(NuevasRunt);
        ColumnasExcel.DatosDe("SIMIT_VEHICULO").Skip(3).Should().Equal(NuevasSimitVehiculo);
        ColumnasExcel.DatosDe("SIMIT_LOCATARIO").Skip(5).Should().Equal(NuevasSimitLocatario);
        ColumnasExcel.DatosDe("SIMIT_PROPIETARIO").Should().Equal(["Personas consultadas", "Personas con error", "Documentos", "Comparendos", "Valor total"]);
        ColumnasExcel.DatosDe("IMPUESTO_DEPARTAMENTAL").Should().Equal(["Total adeudado", "Vigencias pendientes", "Estado del impuesto"]);
        ColumnasExcel.DatosDe("RUNT_LOCATARIO").Should().Equal(["Personas consultadas", "Personas con error", "Documentos"]);
        ColumnasExcel.DatosDe("AVALUO").Should().Equal(["Valor sugerido", "Fuente principal", "Sin valor comercial"]);
        ColumnasExcel.SinFuente.Should().Equal(SinFuente);
        var nuevas = NuevasRunt.Concat(NuevasSimitVehiculo).Concat(NuevasSimitLocatario).Append("Estado del impuesto").Concat(SinFuente).Append("ID del vehículo").ToList();
        nuevas.Should().HaveCount(42, "42 columnas nuevas (1 fija + 25 + 6 + 2 + 1 + 7)").And.OnlyHaveUniqueItems();
        ColumnasExcel.EnClaro.Should().BeEquivalentTo(
            ["Número de documento del propietario", "Número de documento del locatario", "Nombre del locatario", "Garantías a favor de",
                "Garantías mobiliarias", "Detalle de comparendos del locatario"],
            "SP-11: solo los datos de D3 salen en claro");
        ColumnasExcel.Encabezados(["RUNT", "SIMIT_VEHICULO", "IMPUESTO_DEPARTAMENTAL"]).Should().Equal([.. Fijas, .. DeRunt, .. DeSimitVehiculo, .. DeImpuesto, .. SinFuente]);
    }

    // HU #14752 AC1/AC2/AC3 (contrato-excel §2) — cada columna nueva con su origen y su formato: RUNT en claro (documentos, nombre del
    // locatario, garantías y acreedores), fechas dd/MM/yyyy, SI/NO, listas «; », SIMIT con «; …» si viene truncado, detalle de comparendos
    // del locatario con el documento en claro y pesos «$ 468.500», «Estado del impuesto», «ID del vehículo» y la cola sin fuente vacía.
    // Sin fuente Completado sus columnas quedan vacías (salvo «Estado del impuesto» NO DISPONIBLE). Sin el detalle del RUNT: 1-4 sí, 5-25 no,
    // y la hoja «Diagnóstico» trae el aviso D4.
    [Fact]
    public void HU14752_columnas_nuevas_con_su_origen_y_formato()
    {
        var completo = Guid.Parse("0199f000-0000-7000-8000-00000000e001");
        var pendiente = Guid.Parse("0199f000-0000-7000-8000-00000000e002");
        var sinDetalle = Guid.Parse("0199f000-0000-7000-8000-00000000e003");
        var runt = DatosRuntExcel.Desde(RespuestaExcel("TST4A1"))!;
        var excel = new DatosExcelVehiculo("CC", PropietarioPrueba, "CC", LocatarioPrueba, NombreLocatarioPrueba, runt, RuntSinDetalle: false);
        var lote = new ContenidoReporteLote(
            new EncabezadoLoteReporte(completo, "Lote v4", "Empresa", "Ana", Dato, "Diagnóstico Simple"),
            [
                new VehiculoReporte(completo, 1, "TST4A1", "PLACA_CARRO", "TST4A1", null,
                    [RuntCompletado("TST4A1"), SimitVehiculo(), SimitLocatario(), Impuesto(EstadoSeccion.Completado, tieneDeuda: true)], Excel: excel),
                new VehiculoReporte(pendiente, 2, "TST4A2", "PLACA_CARRO", "TST4A2", null,
                    [Pendiente("RUNT"), Pendiente("SIMIT_VEHICULO"), Pendiente("SIMIT_LOCATARIO"), Impuesto(EstadoSeccion.NoDisponible)],
                    Excel: excel with { Runt = null }),
                new VehiculoReporte(sinDetalle, 3, "TST4A3", "PLACA_CARRO", "TST4A3", null,
                    [RuntCompletado("TST4A3"), Pendiente("SIMIT_VEHICULO"), Pendiente("SIMIT_LOCATARIO"), Impuesto(EstadoSeccion.Completado, tieneDeuda: false)],
                    Excel: new DatosExcelVehiculo("NIT", "900111222", null, null, null, null, RuntSinDetalle: true)),
            ]);

        var libro = ExcelDePrueba.Leer(TabularWorkbookWriter.Write(ExcelLote.Hojas(lote, new Dictionary<Guid, DatosTecnicosVehiculo>(), null)));

        var hoja = libro.Hojas["Vehículos"];
        hoja[0].Should().Equal([.. Fijas, .. DeRunt, .. DeSimitVehiculo, "SIMIT locatarios — Estado", "SIMIT locatarios — Personas consultadas",
            "SIMIT locatarios — Personas con error", "SIMIT locatarios — Documentos", "SIMIT locatarios — Comparendos", "SIMIT locatarios — Valor total",
            .. NuevasSimitLocatario.Select(r => "SIMIT locatarios — " + r), .. DeImpuesto, .. SinFuente]);
        var fila = libro.Fila("Vehículos", "TST4A1");
        var esperado = new Dictionary<string, string>
        {
            ["ID del vehículo"] = completo.ToString("D"),
            ["RUNT — Tipo de documento del propietario"] = "CC",
            ["RUNT — Número de documento del propietario"] = PropietarioPrueba,
            ["RUNT — Tipo de documento del locatario"] = "CC",
            ["RUNT — Número de documento del locatario"] = LocatarioPrueba,
            ["RUNT — Nombre del locatario"] = NombreLocatarioPrueba,
            ["RUNT — Estado SOAT"] = "VIGENTE",
            ["RUNT — Vencimiento SOAT"] = "31/05/2027",
            ["RUNT — Estado RTM"] = "SI",
            ["RUNT — Vencimiento RTM"] = "01/02/2027",
            ["RUNT — Garantías mobiliarias"] = "BANCO PRUEBA SA (NIT 900123456) - VIGENTE - 10/05/2021; ACREEDOR NATURAL FICTICIO (CC 1011122233) - 11/06/2022",
            ["RUNT — Garantías a favor de"] = "BANCO PRUEBA SA (NIT 900123456); PERSONA NATURAL FICTICIA (CC 1012345678)",
            ["RUNT — Limitaciones a la propiedad"] = "EMBARGO - JUZGADO PRUEBA - 01/02/2024",
            ["RUNT — Accidentalidad"] = "Fecha: 02/02/2025; Gravedad: CON HERIDOS",
            ["RUNT — Blindaje"] = "SI",
            ["RUNT — Nivel de blindaje"] = "3",
            ["RUNT — Fecha de matrícula"] = "15/03/2020",
            ["RUNT — Cilindraje"] = "1598",
            ["RUNT — Clasificación"] = "AUTOMOVIL",
            ["RUNT — Color"] = "BLANCO",
            ["RUNT — Estado del vehículo en el RUNT"] = "ACTIVO",
            ["RUNT — N.º de chasis"] = "CHASISPRUEBA0123",
            ["RUNT — N.º de motor"] = "MOTORPRUEBA123",
            ["RUNT — Tiene gravámenes"] = "SI",
            ["RUNT — Tipo de carrocería"] = "SEDAN",
            ["RUNT — Tipo de combustible"] = "GASOLINA",
            ["SIMIT vehículo — Valor total"] = "al menos 1804100.00", // columna existente, sin cambio (suma decimal invariante)
            ["SIMIT vehículo — Cantidad de resoluciones"] = "1",
            ["SIMIT vehículo — Estado de la placa"] = "Con multas",
            ["SIMIT vehículo — Números de comparendo"] = "TST-C-0701; TST-C-0702; …",
            ["SIMIT vehículo — Fechas de comparendo"] = "30/09/2026 10:15:00; 01/10/2026; …",
            ["SIMIT vehículo — Valores a pagar"] = "604100; 1200000; …",
            ["SIMIT vehículo — Total de infracciones"] = "3",
            ["SIMIT locatarios — Documentos"] = "CC ****5432; NIT ****3456; CC ****0000",
            ["SIMIT locatarios — Detalle de comparendos del locatario"] =
                $"[CC-{LocatarioPrueba}] Número: TST-C-0709 | Fecha: 30/09/2026 | Valor: $ 468.500; [NIT-900123456] Sin comparendos",
            ["SIMIT locatarios — Total de comparendos del locatario"] = "1",
            ["Impuesto departamental — Estado del impuesto"] = "CON DEUDA",
        };
        foreach (var (columna, valor) in esperado)
            libro.Valor("Vehículos", fila, columna).Should().Be(valor, columna);
        libro.Texto.Should().NotContain("1077788899", "SP-11: el documento del demandante de la limitación no está en D3");
        libro.Texto.Should().NotContain(DetalleRuntGarantia.PersonaNatural, "en el Excel el acreedor sale en claro, sin la sustitución del PDF");

        var vacia = libro.Fila("Vehículos", "TST4A2");
        foreach (var columna in DeRunt.Skip(1).Concat(DeSimitVehiculo.Skip(1)).Concat(NuevasSimitLocatario.Select(r => "SIMIT locatarios — " + r)))
            libro.Valor("Vehículos", vacia, columna).Should().BeEmpty($"una fuente no Completado deja vacías sus columnas ({columna})");
        libro.Valor("Vehículos", vacia, "Impuesto departamental — Estado").Should().Be("No confirmado");
        libro.Valor("Vehículos", vacia, "Impuesto departamental — Total adeudado").Should().BeEmpty();
        libro.Valor("Vehículos", vacia, "Impuesto departamental — Vigencias pendientes").Should().BeEmpty();
        libro.Valor("Vehículos", vacia, "Impuesto departamental — Estado del impuesto").Should().Be("NO DISPONIBLE", "la única excepción a la regla de vacío");
        libro.Valor("Vehículos", vacia, "ID del vehículo").Should().Be(pendiente.ToString("D"));

        var viejo = libro.Fila("Vehículos", "TST4A3");
        libro.Valor("Vehículos", viejo, "RUNT — Tipo de documento del propietario").Should().Be("NIT", "1-4 salen del vehículo aunque falte la respuesta");
        libro.Valor("Vehículos", viejo, "RUNT — Número de documento del propietario").Should().Be("900111222");
        foreach (var columna in NuevasRunt.Skip(4).Select(r => "RUNT — " + r))
            libro.Valor("Vehículos", viejo, columna).Should().BeEmpty($"sin la respuesta del RUNT, {columna} va vacía");
        libro.Valor("Vehículos", viejo, "Impuesto departamental — Estado del impuesto").Should().Be("VIGENTE");

        foreach (var placa in new[] { "TST4A1", "TST4A2", "TST4A3" })
        {
            foreach (var columna in SinFuente)
                libro.Valor("Vehículos", libro.Fila("Vehículos", placa), columna).Should().BeEmpty($"{columna} no tiene fuente (D2)");
        }

        libro.Hojas["Diagnóstico"].Should().ContainSingle(f => f[0] == "Aviso").Which[1].Should().Be(AvisoD4);
        libro.Hojas["Diagnóstico"].FindIndex(f => f[0] == "Aviso").Should().Be(libro.Hojas["Diagnóstico"].FindIndex(f => f[0] == "Tipo de análisis") + 1);
        var sinAviso = new ContenidoReporteLote(lote.Encabezado, [lote.Vehiculos[0]]);
        ExcelLote.Hojas(sinAviso, new Dictionary<Guid, DatosTecnicosVehiculo>(), null)[1].Rows
            .Should().NotContain(f => f[0] == TabularWorkbookWriter.Cell.Of("Aviso"), "sin RUNT completado sin detalle no hay aviso");
    }

    // HU #14752 AC2 — el nombre del locatario: el del RUNT cuyo tipo (texto de Intempo → código) y número (sin espacios) casan con el
    // locatario del vehículo; primero lessee[] (name o los nombres unidos) y luego propietarios_actuales[].locatarios[]; si no casa, null.
    [Fact]
    public void HU14752_nombre_del_locatario_casa_por_tipo_y_numero()
    {
        var runt = DatosRuntExcel.Desde(RespuestaExcel("TST4A4"))!;

        DatosExcelVehiculo.NombreDelLocatario(runt, "CC", LocatarioPrueba).Should().Be(NombreLocatarioPrueba, "lessee[] con los nombres unidos");
        DatosExcelVehiculo.NombreDelLocatario(runt, "CC", " 1098 765432 ").Should().Be(NombreLocatarioPrueba, "el número se compara sin espacios");
        DatosExcelVehiculo.NombreDelLocatario(runt, "NIT", "900777888").Should().Be("LEASING FICTICIO SA", "propietarios_actuales[].locatarios[]");
        DatosExcelVehiculo.NombreDelLocatario(runt, "NIT", LocatarioPrueba).Should().BeNull("el tipo no casa");
        DatosExcelVehiculo.NombreDelLocatario(runt, "CC", "1000000001").Should().BeNull("el número no casa");
        DatosExcelVehiculo.NombreDelLocatario(runt, null, null).Should().BeNull();
        DatosExcelVehiculo.NombreDelLocatario(null, "CC", LocatarioPrueba).Should().BeNull("sin respuesta del RUNT");
        DatosRuntExcel.Desde(new Flit.Consultas.Grpc.V1.ConsultarVehiculoIntempoResponse { Resultado = Flit.Consultas.Grpc.V1.ResultadoRuntIntempo.NoEncontrado })
            .Should().BeNull();
        runt.ToString().Should().NotContainAny([LocatarioPrueba, NombreLocatarioPrueba, "1012345678"], "SP-7: los records no imprimen sus datos");
        new DatosExcelVehiculo("CC", PropietarioPrueba, "CC", LocatarioPrueba, NombreLocatarioPrueba, runt, false).ToString()
            .Should().NotContainAny([PropietarioPrueba, LocatarioPrueba, NombreLocatarioPrueba]);
    }

    // HU #14752 SP-10 — el nombre del locatario y el acreedor vienen de la fuente: un «=HYPERLINK(…)» o «+…» inicial queda como texto
    // literal (quotePrefix) y el libro no tiene fórmulas.
    [Fact]
    public void HU14752_SP10_nombre_que_parece_formula_queda_literal()
    {
        var id = Guid.Parse("0199f000-0000-7000-8000-00000000e004");
        var respuesta = RespuestaExcel("TST4A5");
        respuesta.Vehiculo.GarantiasFavorDe[0].Acreedor = "+ACREEDOR FORMULA";
        const string nombre = "=HYPERLINK(\"x\")";
        var excel = new DatosExcelVehiculo("CC", PropietarioPrueba, "CC", LocatarioPrueba, nombre, DatosRuntExcel.Desde(respuesta), false);
        var lote = new ContenidoReporteLote(new EncabezadoLoteReporte(id, "Lote", "Empresa", "Ana", Dato, "Diagnóstico Simple"),
            [new VehiculoReporte(id, 1, "TST4A5", "PLACA_CARRO", "TST4A5", null, [RuntCompletado("TST4A5")], Excel: excel)]);

        var libro = ExcelDePrueba.Leer(TabularWorkbookWriter.Write(ExcelLote.Hojas(lote, new Dictionary<Guid, DatosTecnicosVehiculo>(), null)));

        var fila = libro.Fila("Vehículos", "TST4A5");
        libro.Formulas.Should().Be(0);
        libro.Valor("Vehículos", fila, "RUNT — Nombre del locatario").Should().Be(nombre);
        libro.ComoTextoLiteral("Vehículos", fila, "RUNT — Nombre del locatario").Should().BeTrue();
        libro.Valor("Vehículos", fila, "RUNT — Garantías a favor de").Should().StartWith("+ACREEDOR FORMULA");
        libro.ComoTextoLiteral("Vehículos", fila, "RUNT — Garantías a favor de").Should().BeTrue();
    }

    // HU #14752 AC2 / SP-1 (integración, Postgres real) — el lector descifra al generar: número del propietario y del locatario (solo si lo
    // informó el RUNT) en claro y el nombre del locatario desde la respuesta RUNT cifrada sembrada; el vehículo sin respuesta guardada
    // trae 1-4 (sin el locatario declarado por el cliente), deja 5-25 vacías y la hoja «Diagnóstico» lleva el aviso D4.
    [Fact]
    public async Task HU14752_SP1_documentos_y_nombre_del_locatario_en_claro_desde_la_respuesta_cifrada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        Flit.Diagnostico.Tests.Tablero.CeldaSembrada[] runt = [new(Fuente.Runt, EstadoCelda.Pendiente, 1)];
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt], vehiculos: [new("TST4B1", runt), new("TST4B2", runt)]);
        foreach (var placa in new[] { "TST4B1", "TST4B2" })
            await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.Runt), PayloadRunt.Replace("TST401", placa, StringComparison.Ordinal), ct);
        await Flit.Diagnostico.Tests.Consultas.RespuestaFuenteTests.GuardarCifradaAsync(api, lote.Celda("TST4B1", Fuente.Runt).Id,
            Flit.Diagnostico.Tests.Consultas.RespuestaFuenteTests.Cifrar(api, ContenidoReporteTests.ProtoJson(RespuestaExcel("TST4B1"))), ct);
        await DocumentosDelVehiculoAsync(api, empresa, lote.Vehiculos.Single(v => v.Placa == "TST4B1").Id, "CC", PropietarioPrueba, "CC", LocatarioPrueba, OrigenDocumento.Proveedor, ct);
        await DocumentosDelVehiculoAsync(api, empresa, lote.Vehiculos.Single(v => v.Placa == "TST4B2").Id, "NIT", "900111222", "CC", "1033344455", OrigenDocumento.DeclaradoCliente, ct);

        var libro = await ExcelDePrueba.LibroOkAsync(api, ExcelDePrueba.Token(api, empresa.Id), lote.Id, ct);

        var conRespuesta = libro.Fila("Vehículos", "TST4B1");
        libro.Valor("Vehículos", conRespuesta, "RUNT — Número de documento del propietario").Should().Be(PropietarioPrueba, "SP-1: el número sembrado completo");
        libro.Valor("Vehículos", conRespuesta, "RUNT — Tipo de documento del locatario").Should().Be("CC");
        libro.Valor("Vehículos", conRespuesta, "RUNT — Número de documento del locatario").Should().Be(LocatarioPrueba);
        libro.Valor("Vehículos", conRespuesta, "RUNT — Nombre del locatario").Should().Be(NombreLocatarioPrueba, "sale de la respuesta RUNT cifrada");
        libro.Valor("Vehículos", conRespuesta, "RUNT — Estado SOAT").Should().Be("VIGENTE");
        libro.Valor("Vehículos", conRespuesta, "RUNT — Garantías a favor de").Should().Contain("PERSONA NATURAL FICTICIA (CC 1012345678)");
        libro.Valor("Vehículos", conRespuesta, "ID del vehículo").Should().Be(lote.Vehiculos.Single(v => v.Placa == "TST4B1").Id.ToString("D"));
        var sinRespuesta = libro.Fila("Vehículos", "TST4B2");
        libro.Valor("Vehículos", sinRespuesta, "RUNT — Tipo de documento del propietario").Should().Be("NIT");
        libro.Valor("Vehículos", sinRespuesta, "RUNT — Número de documento del propietario").Should().Be("900111222");
        libro.Valor("Vehículos", sinRespuesta, "RUNT — Tipo de documento del locatario").Should().BeEmpty("el declarado por el cliente no sale");
        libro.Valor("Vehículos", sinRespuesta, "RUNT — Número de documento del locatario").Should().BeEmpty();
        libro.Valor("Vehículos", sinRespuesta, "RUNT — Estado SOAT").Should().BeEmpty("sin respuesta guardada");
        libro.Texto.Should().NotContain("1033344455");
        libro.Hojas["Diagnóstico"].Should().ContainSingle(f => f[0] == "Aviso").Which[1].Should().Be(AvisoD4);
    }

    // HU #14752 AC4 / SP-4 (integración) — VersionPlantilla = 4; una evidencia anterior (huella de v3 sembrada) se reemplaza en la próxima
    // descarga: queda una sola vigente, la anterior se conserva no vigente.
    [Fact]
    public async Task HU14752_SP4_version_4_y_una_evidencia_anterior_deja_una_sola_vigente()
    {
        ExcelLote.VersionPlantilla.Should().Be(4);
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteSimitAsync(empresa, "TST4C1", ct);
        var cliente = api.Cliente(ExcelDePrueba.Token(api, empresa.Id));
        await ExcelDePrueba.BytesOkAsync(cliente, lote.Id, ct);
        var deV3 = new string('3', 64);
        await using (var db = empresa.Db())
            await db.Set<Evidencia>().Where(e => e.DiagnosticoId == lote.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.Huella, deV3), ct);

        var libro = ExcelDePrueba.Leer(await ExcelDePrueba.BytesOkAsync(cliente, lote.Id, ct));

        libro.Hojas["Vehículos"][0].Should().Contain("ID del vehículo", "el archivo nuevo es v4");
        var todas = await EvidenciasAsync(empresa, lote.Id, ct);
        todas.Should().HaveCount(2, "la anterior se conserva");
        todas.Should().ContainSingle(e => e.Vigente).Which.Huella.Should().NotBe(deV3);
        todas.Single(e => e.Huella == deV3).Vigente.Should().BeFalse();
    }

    private const string PropietarioPrueba = "1055566677";

    private const string LocatarioPrueba = "1098765432";

    private const string NombreLocatarioPrueba = "LOCATARIO FICTICIO UNO";

    /// <summary>
    /// Respuesta RUNT ENCONTRADO para el Excel v4 (datos ficticios): la de <see cref="ContenidoReporteTests.RespuestaRunt"/> más locatario
    /// (lessee[] y locatarios de propietarios_actuales), garantías mobiliarias, blindaje, limitación, accidentalidad y gravámenes.
    /// </summary>
    internal static Flit.Consultas.Grpc.V1.ConsultarVehiculoIntempoResponse RespuestaExcel(
        string placa, string locatario = LocatarioPrueba, string nombre = NombreLocatarioPrueba)
    {
        var partes = nombre.Split(' ', 3);
        var respuesta = ContenidoReporteTests.RespuestaRunt(placa);
        var v = respuesta.Vehiculo;
        v.InformacionGeneral.TieneGravamenes = "S";
        v.Lessee.Add(new Flit.Consultas.Grpc.V1.PersonaIntempo
        {
            DocumentType = "Cédula de ciudadanía",
            DocumentNumber = locatario,
            FirstName = partes[0],
            FirstLastName = partes.ElementAtOrDefault(1) ?? string.Empty,
            SecondLastName = partes.ElementAtOrDefault(2) ?? string.Empty,
        });
        v.PropietariosActuales[0].Locatarios.Add(new Flit.Consultas.Grpc.V1.LocatarioIntempo { Tdoc = "NIT", Ndoc = "900777888", NombreRazonSocial = "LEASING FICTICIO SA" });
        v.GarantiasMobiliarias.Add(new Flit.Consultas.Grpc.V1.GarantiaMobiliariaIntempo
        {
            Entidad = "BANCO PRUEBA SA",
            TipoDocumentoEntidad = "NIT",
            NumeroDocumentoEntidad = "900123456",
            Estado = "VIGENTE",
            FechaRegistro = "2021-05-10",
        });
        v.GarantiasMobiliarias.Add(new Flit.Consultas.Grpc.V1.GarantiaMobiliariaIntempo
        {
            Entidad = "ACREEDOR NATURAL FICTICIO",
            TipoDocumentoEntidad = "Cédula de ciudadanía",
            NumeroDocumentoEntidad = "1011122233",
            FechaRegistro = "11/06/2022",
        });
        v.InformacionBlindaje = new Flit.Consultas.Grpc.V1.InformacionBlindajeIntempo { Blindado = "SI", NivelBlindaje = "NIVEL III", NivelBlindajeNumero = "3" };
        v.LimitacionPropiedad.Add(new Flit.Consultas.Grpc.V1.LimitacionPropiedadIntempo
        {
            TipoLimitacion = "EMBARGO",
            EntidadJuridica = "JUZGADO PRUEBA",
            FechaExpedicion = "2024-02-01",
            TipoDocumentoDemandante = "CC",
            NoDocumentoDemandante = "1077788899",
        });
        v.AccidentRate.Clear();
        v.AccidentRate.Add(new Flit.Consultas.Grpc.V1.AccidenteIntempo { Campos = { ["fecha"] = "02/02/2025", ["gravedad"] = "CON HERIDOS" } });
        return respuesta;
    }

    private static SeccionReporte RuntCompletado(string placa) => new(
        "RUNT", "VEHICULO", EstadoSeccion.Completado, null, null, Dato,
        new ResultadoRuntDetalle(placa, null, "PARTICULAR", "AUTOMOVIL", "STRIA PRUEBA", [new PropietarioRuntDetalle("C", "PROPIA")], 1), null);

    private static SeccionReporte SimitVehiculo() => new(
        "SIMIT_VEHICULO", "VEHICULO", EstadoSeccion.Completado, null, null, Dato,
        new ResultadoSimitDetalle("ROJO", 2,
            [new ComparendoDetalle("TST-C-0701", "2026-09-30T10:15:00", "604100.00", "PENDIENTE", null), new ComparendoDetalle("TST-C-0702", "2026-10-01", "1200000", "PENDIENTE", null)],
            ComparendosTruncados: true, CantidadResoluciones: 1),
        null);

    private static SeccionReporte SimitLocatario() => new(
        "SIMIT_LOCATARIO", "LOCATARIO", EstadoSeccion.Completado, null, null, Dato, null,
        [
            new PersonaReporte(1, "LOCATARIO", "CC", "****5432", false, null, "SIMIT", EstadoPersonaReporte.Ok, Dato,
                new ResultadoSimitDetalle("ROJO", 1, [new ComparendoDetalle("TST-C-0709", "2026-09-30", "468500.00", "PENDIENTE", null)], false), LocatarioPrueba),
            new PersonaReporte(2, "LOCATARIO", "NIT", "****3456", false, null, "SIMIT", EstadoPersonaReporte.Ok, Dato,
                new ResultadoSimitDetalle("VERDE", 0, [], false), "900123456"),
            new PersonaReporte(3, "LOCATARIO", "CC", "****0000", false, null, "SIMIT", EstadoPersonaReporte.NoDisponible, null, null, "1000000000"),
        ]);

    private static SeccionReporte Impuesto(EstadoSeccion estado, bool tieneDeuda = false) => new(
        "IMPUESTO_DEPARTAMENTAL", "VEHICULO", estado, null, estado == EstadoSeccion.NoDisponible ? SeccionReporte.ConsultaNoDisponible : null, Dato,
        estado == EstadoSeccion.Completado ? new ResultadoImpuestoDetalle(tieneDeuda ? 350000 : 0, [new VigenciaImpuestoDetalle(2026, 350000, !tieneDeuda)], tieneDeuda) : null,
        null);

    private static SeccionReporte Pendiente(string fuente) => new(fuente, "VEHICULO", EstadoSeccion.Pendiente, null, null, null, null, fuente.EndsWith("LOCATARIO", StringComparison.Ordinal) ? [] : null);

    /// <summary>
    /// Escenario de fuga del Excel (SP-9): el vehículo de la celda RUNT con <paramref name="documento"/> como propietario y locatario del RUNT
    /// (cifrados) y la respuesta RUNT cifrada con ese locatario llamado <paramref name="nombre"/>.
    /// </summary>
    internal static async Task SembrarLocatarioEnClaroAsync(EmpresaDePrueba empresa, CeldaConsulta celda, string documento, string nombre, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        ArgumentNullException.ThrowIfNull(celda);
        await Flit.Diagnostico.Tests.Consultas.RespuestaFuenteTests.GuardarCifradaAsync(empresa.Api, celda.Id,
            Flit.Diagnostico.Tests.Consultas.RespuestaFuenteTests.Cifrar(empresa.Api, ContenidoReporteTests.ProtoJson(RespuestaExcel("TST906", documento, nombre))), ct);
        await DocumentosDelVehiculoAsync(empresa.Api, empresa, celda.VehiculoId, "CC", documento, "CC", documento, OrigenDocumento.Proveedor, ct);
    }

    /// <summary>Documentos del vehículo (propietario del RUNT y locatario con su origen), cifrados como en producción.</summary>
    private static async Task DocumentosDelVehiculoAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, Guid vehiculo, string tipoPropietario, string propietario, string tipoLocatario, string locatario,
        OrigenDocumento origen, CancellationToken ct)
    {
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        await using var db = empresa.Db();
        var fila = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.Id == vehiculo, ct);
        fila.PropietarioDocTipo = tipoPropietario;
        fila.PropietarioDocCifrado = protector.Proteger(propietario);
        fila.LocatarioDocTipo = tipoLocatario;
        fila.LocatarioDocCifrado = protector.Proteger(locatario);
        fila.LocatarioDocOrigen = origen;
        await db.SaveChangesAsync(ct);
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
        var locatario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Locatario, 1, "20001234", ct);
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

    /// <summary>¿La celda (fila y columna base 0) va como texto literal (quotePrefix)? Para hojas cuyo encabezado no está en la fila 0.</summary>
    public bool EsLiteral(string hoja, int fila, int columna) => literales.Contains((hoja, fila, columna));

    private int IndiceColumna(string hoja, string columna)
    {
        var indice = Array.IndexOf(Hojas[hoja][0], columna);
        if (indice < 0)
            throw new InvalidOperationException($"La hoja «{hoja}» no tiene la columna «{columna}»: {string.Join(" | ", Hojas[hoja][0])}");
        return indice;
    }
}
