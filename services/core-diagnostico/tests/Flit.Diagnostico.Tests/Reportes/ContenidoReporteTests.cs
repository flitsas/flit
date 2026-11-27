using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using Flit.Diagnostico.Tests.Plataforma;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Intempo = Flit.Consultas.Grpc.V1;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// HU #13847 (F07-H2, F07-C1/C2/C3/C9/C10) — <see cref="LectorContenidoReporte"/>: el contenido del reporte de un vehículo (y del
/// lote) trae solo las fuentes del alcance en el orden de la grilla; una celda manual y una de API con el mismo dato dan el mismo
/// contenido; las personas salen con «****» + 4 últimos y el rótulo de declarado, sin nombre; N/A con su motivo y ERROR como
/// «consulta no disponible»; el encabezado con el nombre del creador de Identidad («No disponible» si no responde), la empresa del
/// claim <c>company_name</c>, la hora Colombia y el rótulo del tipo de análisis; un vehículo de otra compañía no se lee.
/// Postgres real (base efímera de <see cref="ApiDePrueba"/>); placas TST### y documentos ficticios cifrados.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Reportes.ContenidoReporteTests
/// </code>
/// </example>
public sealed class ContenidoReporteTests
{
    private const string Empresa = "Transportes Prueba SAS";

    private const string PayloadRunt =
        """{"placa":"TST901","vin":"9ZZPRUEBA00000901","servicio":"PARTICULAR","clase":"AUTOMOVIL","organismo":"STRIA PRUEBA","servicio_que_resolvio":"INTEMPO_SIMULADO","id_transaccion":"TX-SECRETA-901","propietarios":[{"tipo_documento":"C","tipo_propiedad":"PROPIA"}],"locatarios":0}""";

    private const string PayloadSimit =
        """{"semaforo":"AMARILLO","cantidad_comparendos":1,"comparendos":[{"numero":"TST-C-0001","fecha":"2026-09-30","valor":"468500","estado":"PENDIENTE","organismo":"Organismo de prueba"}],"transaccion_proveedor_id":"TX-SIMIT-901"}""";

    private const string PayloadAvaluo =
        """{"sin_valor_comercial":false,"valor_sugerido":48500000,"fuente_principal":"fasecolda","fuentes":[{"fuente":"fasecolda","estado":"OK","valor":48500000,"muestras":3,"fecha":"2026-10-22"}]}""";

    private static readonly string[] OrdenVehiculo = ["RUNT", "SIMIT_VEHICULO", "AVALUO"];

    private static readonly DateTimeOffset Dato = new(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);

    // AC1 (F07-C3) — lote Simple con alcance RUNT, SIMIT_VEHICULO y AVALUO (y una celda de persona fuera del alcance): tres
    // secciones en el orden de la grilla, ninguna de persona; el contenido del lote trae las mismas secciones por vehículo.
    [Fact]
    public async Task Solo_trae_las_fuentes_del_alcance_en_el_orden_de_la_grilla()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        Fuente[] alcance = [Fuente.Runt, Fuente.SimitVehiculo, Fuente.Avaluo];
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: alcance, vehiculos:
        [
            new("TST901", [new(Fuente.Avaluo, EstadoCelda.Pendiente, 1), new(Fuente.SimitPropietario, EstadoCelda.Pendiente, 1),
                new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1), new(Fuente.Runt, EstadoCelda.Pendiente, 1)]),
            new("TST902", [new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1), new(Fuente.Runt, EstadoCelda.Pendiente, 1),
                new(Fuente.Avaluo, EstadoCelda.Pendiente, 1)]),
        ]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST901", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST901", Fuente.SimitVehiculo), PayloadSimit, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST901", Fuente.Avaluo), PayloadAvaluo, ct);
        var vehiculo = lote.Vehiculos.Single(v => v.Placa == "TST901").Id;

        await using var db = empresa.Db();
        var contenido = await Lector(api, db, new UsuariosIdentidadDePrueba()).VehiculoAsync(Usuario(empresa.Id), lote.Id, vehiculo, ct);

        contenido.Secciones.Select(s => s.Fuente).Should().Equal(["RUNT", "SIMIT_VEHICULO", "AVALUO"], "el orden de las columnas de la grilla");
        contenido.Secciones.Select(s => s.Fuente).Should().Equal(Alcance.Agrupar(lote.Cabecera.Alcance).Fuentes.Select(Codigo));
        contenido.Secciones.Should().OnlyContain(s => s.Grupo == "POR_VEHICULO" && s.Personas == null, "ninguna sección de persona");
        contenido.Secciones.Should().OnlyContain(s => s.Estado == EstadoSeccion.Completado && s.Leyenda == null);
        contenido.Secciones[0].Resultado.Should().BeOfType<ResultadoRuntDetalle>().Which.Clase.Should().Be("AUTOMOVIL");
        contenido.Secciones[1].Resultado.Should().BeOfType<ResultadoSimitDetalle>().Which.CantidadComparendos.Should().Be(1);
        contenido.Secciones[2].Resultado.Should().BeOfType<ResultadoAvaluoDetalle>().Which.ValorSugerido.Should().Be(48_500_000);
        contenido.Secciones[0].DatoObtenidoEn.Should().NotBeNull();
        Texto(contenido).Should().NotContainAny(["INTEMPO", "TX-SECRETA-901", "TX-SIMIT-901", "servicio_que_resolvio", "SIMIT_PROPIETARIO"]);

        var delLote = await Lector(api, db, new UsuariosIdentidadDePrueba()).LoteAsync(Usuario(empresa.Id), lote.Id, ct);
        delLote.Vehiculos.Select(v => v.Placa).Should().Equal(["TST901", "TST902"], "en el orden de la carga");
        delLote.Vehiculos.Should().OnlyContain(v => v.Secciones.Select(s => s.Fuente).SequenceEqual(OrdenVehiculo));
        delLote.Vehiculos[1].Secciones.Should().OnlyContain(s => s.Estado == EstadoSeccion.Pendiente && s.Resultado == null);
        Texto(delLote.Vehiculos[0].Secciones).Should().Be(Texto(contenido.Secciones), "el Excel y el PDF salen del mismo contenido");
    }

    // AC2 (F07-C9, R-20) — dos vehículos con el mismo resultado SIMIT, uno llenado a mano y otro por API: sus secciones son iguales
    // campo por campo y no dicen MANUAL, API ni el usuario del llenado.
    [Fact]
    public async Task Celda_manual_y_de_api_con_el_mismo_dato_dan_el_mismo_contenido()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo], vehiculos:
        [
            new("TST911", [new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1), new(Fuente.Runt, EstadoCelda.Pendiente, 1)]),
            new("TST912", [new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1), new(Fuente.Runt, EstadoCelda.Pendiente, 1)]),
        ]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST911", Fuente.SimitVehiculo), PayloadSimit, ct);
        var manual = await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST912", Fuente.SimitVehiculo), PayloadSimit, ct, manual: true);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST911", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST912", Fuente.Runt), PayloadRunt, ct, manual: true);
        await MismaFechaAsync(empresa, lote.Id, ct);

        await using var db = empresa.Db();
        var lector = Lector(api, db, new UsuariosIdentidadDePrueba());
        var deApi = await lector.VehiculoAsync(Usuario(empresa.Id), lote.Id, lote.Vehiculos.Single(v => v.Placa == "TST911").Id, ct);
        var aMano = await lector.VehiculoAsync(Usuario(empresa.Id), lote.Id, lote.Vehiculos.Single(v => v.Placa == "TST912").Id, ct);

        aMano.Secciones.Should().BeEquivalentTo(deApi.Secciones, o => o.WithStrictOrdering().RespectingRuntimeTypes());
        Texto(aMano.Secciones).Should().Be(Texto(deApi.Secciones), "campo por campo");
        Texto(aMano).Should().NotContainAny(["MANUAL", "\"API\"", manual.UsuarioManualId!.Value.ToString()]);
    }

    // HU #14563 B6 AC18 (CF-23) + hallazgo M1 (R-20, opción a) — avalúo llenado a mano con referencia Fasecolda: la referencia
    // revela el llenado manual, así que no sale en el contenido del reporte de la compañía ni en su PDF; el valor sí.
    [Fact]
    public async Task B6_AC18_Referencia_Fasecolda_no_sale_en_el_contenido_ni_en_el_PDF()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.Avaluo], vehiculos:
            [new("TST921", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.Avaluo, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST921", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST921", Fuente.Avaluo),
            """{"sin_valor_comercial":false,"valor_sugerido":52000000,"fuente_principal":"fasecolda","fuentes":[{"fuente":"fasecolda","estado":"OK","valor":52000000,"fecha":"2026-11-13"}],"referencia":{"codigo_fasecolda":"01601234","marca":"MARCA PRUEBA","linea":"LINEA PRUEBA","modelo":2020}}""",
            ct, manual: true);

        await using var db = empresa.Db();
        var contenido = await Lector(api, db, new UsuariosIdentidadDePrueba())
            .VehiculoAsync(Usuario(empresa.Id), lote.Id, lote.Vehiculos.Single().Id, ct);

        var avaluo = contenido.Secciones.Single(s => s.Fuente == "AVALUO").Resultado;
        var json = JsonSerializer.Serialize(avaluo, avaluo!.GetType());
        json.Should().NotContain("referencia").And.NotContain("codigo_fasecolda").And.NotContain("01601234");
        json.Should().Contain("\"valor_sugerido\":52000000");

        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(contenido, Dato));
        PdfDePrueba.Tiene(texto, "Código Fasecolda").Should().BeFalse(texto);
        PdfDePrueba.Tiene(texto, "01601234").Should().BeFalse();
        PdfDePrueba.Tiene(texto, "MARCA PRUEBA").Should().BeFalse();
        PdfDePrueba.Tiene(texto, "LINEA PRUEBA").Should().BeFalse();
    }

    // AC3 (F07-C10) — RUNT_LOCATARIO con un locatario declarado: tipo de documento, «****» y los cuatro últimos, rótulo de
    // declarado y sin nombre; RUNT_PROPIETARIO en ERROR por una persona: esa persona y la sección «consulta no disponible», sin
    // TIMEOUT. Nada del documento completo ni del id interno de la persona en el contenido; la máscara entra en la huella.
    [Fact]
    public async Task Personas_salen_enmascaradas_y_con_rotulo_de_declarado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Incompleto,
            alcance: [Fuente.Runt, Fuente.RuntPropietario, Fuente.RuntLocatario], vehiculos:
            [new("TST921", [new(Fuente.RuntLocatario, EstadoCelda.Pendiente, 1), new(Fuente.RuntPropietario, EstadoCelda.Pendiente, 1),
                new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
        var vehiculo = lote.Vehiculos.Single().Id;
        var locatario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Locatario, 1, "C", "1012345678", ct, OrigenDocumento.DeclaradoCliente);
        var propietario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Propietario, 1, "C", "79111222", ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST921", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST921", Fuente.RuntLocatario), Personas(
            (locatario.Id, 1, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","nombre":"NOMBRE FICTICIO","licencias":[{"categoria":"B1","estado":"VIGENTE","vigente_hasta":"2030-01-01"}]}""", null)), ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST921", Fuente.RuntPropietario), Personas(
            (propietario.Id, 1, "RUNT_PERSONA", "ERROR", null, "TIMEOUT")), ct, EstadoCelda.Error, "PERSONAS_CON_ERROR");

        await using var db = empresa.Db();
        var contenido = await Lector(api, db, new UsuariosIdentidadDePrueba()).VehiculoAsync(Usuario(empresa.Id), lote.Id, vehiculo, ct);

        contenido.Secciones.Select(s => s.Fuente).Should().Equal(["RUNT", "RUNT_PROPIETARIO", "RUNT_LOCATARIO"]);
        var delLocatario = contenido.Secciones.Single(s => s.Fuente == "RUNT_LOCATARIO");
        delLocatario.Grupo.Should().Be("POR_LOCATARIO");
        delLocatario.Estado.Should().Be(EstadoSeccion.Completado);
        delLocatario.Resultado.Should().BeNull("el resultado va por persona");
        var persona = delLocatario.Personas.Should().ContainSingle().Subject;
        persona.Rol.Should().Be("LOCATARIO");
        persona.TipoDocumento.Should().Be("C");
        persona.DocumentoEnmascarado.Should().Be("****5678");
        persona.DeclaradoPorCliente.Should().BeTrue();
        persona.RotuloDeclarado.Should().Be(PersonaReporte.Declarado);
        persona.Estado.Should().Be(EstadoPersonaReporte.Ok);
        persona.Resultado.Should().BeOfType<ResultadoRuntPersonaDetalle>().Which.Licencias![0].Categoria.Should().Be("B1");

        var dePropietario = contenido.Secciones.Single(s => s.Fuente == "RUNT_PROPIETARIO");
        dePropietario.Estado.Should().Be(EstadoSeccion.NoDisponible);
        dePropietario.Leyenda.Should().Be(SeccionReporte.ConsultaNoDisponible);
        var enError = dePropietario.Personas.Should().ContainSingle().Subject;
        enError.Estado.Should().Be(EstadoPersonaReporte.NoDisponible);
        enError.DocumentoEnmascarado.Should().Be("****1222");
        enError.DeclaradoPorCliente.Should().BeFalse();
        enError.RotuloDeclarado.Should().BeNull();
        enError.Resultado.Should().BeNull();

        Texto(contenido).Should().Contain("1012345678").And.Contain("79111222");
        Texto(contenido).Should().NotContainAny(
            ["NOMBRE FICTICIO", "TIMEOUT", "PERSONAS_CON_ERROR", locatario.Id.ToString(), propietario.Id.ToString()]);
        var huella = contenido.ParaHuella();
        huella.Values.Should().Contain(v => v != null && v.Contains("****5678", StringComparison.Ordinal), "la persona enmascarada entra en la huella");
        huella.Values.Should().NotContain(v => v != null && v.Contains("12345678", StringComparison.Ordinal));
    }

    // HU #14582 (contrato §1.3, schema.md §2) — contenido del lote: cada vehículo trae su propietario actual declarado enmascarado
    // («TIPO ****1234») y el cambio_propietario persistido; la persona histórica DECLARADO_CLIENTE no entra en la sección del
    // locatario del Excel. El declarado completo no llega al contenido ni a la huella.
    [Fact]
    public async Task HU14582_lote_trae_el_declarado_enmascarado_el_cambio_y_sin_locatario_declarado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.RuntLocatario], vehiculos:
            [new("TST931", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.RuntLocatario, EstadoCelda.Pendiente, 1)]),
             new("TST932", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.RuntLocatario, EstadoCelda.Pendiente, 1)])]);
        var vehiculo = lote.Vehiculos.Single(v => v.Placa == "TST931").Id;
        var historico = await PersonaAsync(api, empresa, vehiculo, RolPersona.Locatario, 1, "CC", "1012340000", ct, OrigenDocumento.DeclaradoCliente);
        var deRunt = await PersonaAsync(api, empresa, vehiculo, RolPersona.Locatario, 2, "NIT", "900555444", ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST931", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST931", Fuente.RuntLocatario), Personas(
            (historico.Id, 1, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","licencias":[]}""", null),
            (deRunt.Id, 2, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","licencias":[]}""", null)), ct);
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        await using (var escritura = empresa.Db())
        {
            var fila = await escritura.Set<VehiculoDiagnostico>().SingleAsync(v => v.Id == vehiculo, ct);
            fila.PropietarioDeclaradoDocTipo = "CC";
            fila.PropietarioDeclaradoDocCifrado = protector.Proteger("1020304050");
            fila.CambioPropietario = true;
            await escritura.SaveChangesAsync(ct);
        }

        await using var db = empresa.Db();
        var delLote = await Lector(api, db, new UsuariosIdentidadDePrueba()).LoteAsync(Usuario(empresa.Id), lote.Id, ct);

        var conDeclarado = delLote.Vehiculos.Single(v => v.Placa == "TST931");
        conDeclarado.PropietarioDeclarado.Should().Be("CC ****4050");
        conDeclarado.CambioPropietario.Should().BeTrue();
        var locatarios = conDeclarado.Secciones.Single(s => s.Fuente == "RUNT_LOCATARIO").Personas;
        locatarios.Should().ContainSingle("la persona histórica DECLARADO_CLIENTE no se pinta como locatario")
            .Which.DocumentoEnmascarado.Should().Be("****5444");
        locatarios!.Should().OnlyContain(p => !p.DeclaradoPorCliente && p.RotuloDeclarado == null);
        var sinDeclarado = delLote.Vehiculos.Single(v => v.Placa == "TST932");
        sinDeclarado.PropietarioDeclarado.Should().BeNull();
        sinDeclarado.CambioPropietario.Should().BeNull();
        var huella = delLote.ParaHuella();
        huella.Values.Should().Contain(v => v != null && v.Contains("****4050", StringComparison.Ordinal), "el declarado enmascarado entra en la huella");
        huella.Values.Should().NotContain(v => v != null && (v.Contains("1020304050", StringComparison.Ordinal) || v.Contains("1012340000", StringComparison.Ordinal)));
    }

    // AC1 (F07-C3) y Q7 — N/A con su motivo en código y en texto; una fuente del vehículo en ERROR sale «consulta no disponible»
    // sin tipo de error, causa ni fecha del intento.
    /// <summary>
    /// HU-3: el lector lleva al contenido el estado de cola del dictamen vigente con la misma regla de <c>redaccion_en_cola</c>
    /// (<see cref="RedactorEnCola.EnCola"/>): encendida, PENDIENTE y con fecha. Apagada, o agotada (la cola deja la fecha en null), false.
    /// </summary>
    [Fact]
    public async Task HU3_el_contenido_lleva_el_estado_de_cola_del_dictamen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos: [new("TST961", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var vehiculo = lote.Vehiculos.Single().Id;
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST961", Fuente.Runt), PayloadRunt, ct);
        var fila = EmisorDictamen.FilaAlCerrar(
            empresa.Id, vehiculo, 1, "TRASPASO_STANDARD", new DictamenEvaluado("VIABLE", []), redaccionSuspendida: false, proveedorConfigurado: true, Dato);
        await using (var escritura = empresa.Db())
        {
            escritura.Add(fila);
            await escritura.SaveChangesAsync(ct);
        }

        var encendida = new OpcionesRedaccion { Habilitado = true, MaxReintentos = 2 };
        await using var db = empresa.Db();
        (await Lector(api, db, new UsuariosIdentidadDePrueba(), encendida).VehiculoAsync(Usuario(empresa.Id), lote.Id, vehiculo, ct))
            .Dictamen!.EnCola.Should().BeTrue();
        (await Lector(api, db, new UsuariosIdentidadDePrueba()).VehiculoAsync(Usuario(empresa.Id), lote.Id, vehiculo, ct))
            .Dictamen!.EnCola.Should().BeFalse("con la redacción apagada nadie toma la cola");

        await using (var escritura = empresa.Db())
        {
            var guardada = await escritura.Set<DictamenVehiculo>().SingleAsync(d => d.Id == fila.Id, ct);
            // Lo que deja Decidir al agotar reintentos (intentos 3 > MaxReintentos 2): la fecha de toma en null.
            guardada.RedaccionIntentos = 3;
            guardada.RedaccionDisponibleDesde = null;
            await escritura.SaveChangesAsync(ct);
        }

        db.ChangeTracker.Clear();
        (await Lector(api, db, new UsuariosIdentidadDePrueba(), encendida).VehiculoAsync(Usuario(empresa.Id), lote.Id, vehiculo, ct))
            .Dictamen!.EnCola.Should().BeFalse("reintentos agotados: sin fecha de toma la cola ya no la toma");
    }

    /// <summary>
    /// HU-3: el estado de cola entra en la huella del PDF solo con el dictamen PENDIENTE (clave <c>dictamen/en_cola</c>): al salir
    /// de la cola cambia el rótulo y la huella invalida el PDF sin subir la versión de plantilla. Con COMPLETADA no hay clave, así
    /// que los PDF de dictámenes ya redactados conservan su huella.
    /// </summary>
    [Fact]
    public void HU3_el_estado_de_cola_entra_en_la_huella_solo_si_el_dictamen_esta_pendiente()
    {
        var encabezado = new EncabezadoReporte(Guid.Empty, "Lote", "Empresa", "Ana", Dato, "Diagnóstico Simple", "TST962", "PLACA_CARRO", "TST962", null);
        var sin = new ContenidoReporteVehiculo(encabezado, []);
        var enCola = sin with { Dictamen = new DictamenEnHuella("VIABLE", "PENDIENTE", "texto", EnCola: true) };
        var fuera = sin with { Dictamen = new DictamenEnHuella("VIABLE", "PENDIENTE", "texto", EnCola: false) };
        enCola.ParaHuella()["dictamen/en_cola"].Should().Be("true");
        fuera.ParaHuella()["dictamen/en_cola"].Should().Be("false");
        enCola.ParaHuella().Should().NotBeEquivalentTo(fuera.ParaHuella());

        var completada = sin with { Dictamen = new DictamenEnHuella("VIABLE", "COMPLETADA", "texto", EnCola: true) };
        completada.ParaHuella().Keys.Should().NotContain("dictamen/en_cola");
        completada.ParaHuella().Should().BeEquivalentTo((sin with { Dictamen = new DictamenEnHuella("VIABLE", "COMPLETADA", "texto") }).ParaHuella());
    }

    [Fact]
    public async Task Na_trae_su_motivo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Incompleto,
            alcance: [Fuente.Runt, Fuente.SimitVehiculo, Fuente.SimitLocatario], vehiculos:
            [new("TST931", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1),
                new(Fuente.SimitLocatario, EstadoCelda.Na, 1)])]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST931", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST931", Fuente.SimitVehiculo), "{}", ct, EstadoCelda.Error, "TIMEOUT");

        await using var db = empresa.Db();
        var contenido = await Lector(api, db, new UsuariosIdentidadDePrueba()).VehiculoAsync(Usuario(empresa.Id), lote.Id, lote.Vehiculos.Single().Id, ct);

        var na = contenido.Secciones.Single(s => s.Fuente == "SIMIT_LOCATARIO");
        na.Estado.Should().Be(EstadoSeccion.NoAplica);
        na.MotivoNa.Should().Be("SIN_DOCUMENTO_LOCATARIO");
        na.Leyenda.Should().Be("El RUNT no reporta locatario");
        na.Resultado.Should().BeNull();
        na.Personas.Should().BeEmpty();
        var error = contenido.Secciones.Single(s => s.Fuente == "SIMIT_VEHICULO");
        error.Estado.Should().Be(EstadoSeccion.NoDisponible);
        error.Leyenda.Should().Be("consulta no disponible");
        error.MotivoNa.Should().BeNull();
        error.Resultado.Should().BeNull();
        error.DatoObtenidoEn.Should().BeNull();
        Texto(contenido).Should().NotContain("TIMEOUT");
    }

    // AC4 (F07-C1, F07-C2) — lote Simple de un usuario que Identidad conoce: ID, nombre del lote, nombre del usuario, empresa del
    // claim company_name, fecha de creación en hora Colombia y «Diagnóstico Simple»; además la identificación del vehículo.
    [Fact]
    public async Task Simple_se_rotula_Diagnostico_Simple()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var creador = Guid.NewGuid();
        var identidad = new UsuariosIdentidadDePrueba();
        identidad.Nombres[creador] = "Ana Prueba";
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, creador: creador, nombre: "Flota octubre",
            vehiculos: [new("TST941", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        await CreadoEnAsync(empresa, lote.Id, new DateTimeOffset(2026, 10, 22, 19, 30, 0, TimeSpan.Zero), ct);

        await using var db = empresa.Db();
        var lector = Lector(api, db, identidad);
        var encabezado = (await lector.VehiculoAsync(Usuario(empresa.Id), lote.Id, lote.Vehiculos.Single().Id, ct)).Encabezado;

        encabezado.DiagnosticoId.Should().Be(lote.Id);
        encabezado.NombreLote.Should().Be("Flota octubre");
        encabezado.UsuarioCreador.Should().Be("Ana Prueba");
        encabezado.Empresa.Should().Be(Empresa, "la empresa sale del claim company_name de quien pide");
        encabezado.CreadoEn.Offset.Should().Be(TimeSpan.FromHours(-5), "hora Colombia");
        encabezado.CreadoEn.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture).Should().Be("2026-10-22 14:30");
        encabezado.TipoAnalisis.Should().Be("Diagnóstico Simple");
        encabezado.Identificador.Should().Be("TST941");
        encabezado.TipoIdentificador.Should().Be("PLACA_CARRO");
        encabezado.Placa.Should().Be("TST941");
        identidad.Pedidos.Should().Contain(p => p.Usuario == creador.ToString() && p.Empresa == empresa.Id.ToString());

        var delLote = (await lector.LoteAsync(Usuario(empresa.Id), lote.Id, ct)).Encabezado;
        delLote.Should().Be(new EncabezadoLoteReporte(lote.Id, "Flota octubre", Empresa, "Ana Prueba", encabezado.CreadoEn, "Diagnóstico Simple"));
        (await lector.VehiculoAsync(lote.Cabecera, lote.Vehiculos.Single().Id, "Empresa del ZIP", ct)).Encabezado.Empresa
            .Should().Be("Empresa del ZIP", "el ZIP pasa evidencias.empresa_nombre");
        EtiquetaTipoAnalisis.Texto(ModoDiagnostico.Simple, null).Should().Be(EtiquetaTipoAnalisis.Simple);
    }

    // AC4 (F07-C2) — Con análisis: «Diagnóstico Con análisis — » más el nombre del tipo publicado en mayúsculas; con un
    // código que ya no está publicado o con el catálogo caído, el código.
    [Fact]
    public async Task Con_analisis_se_rotula_con_el_nombre_del_tipo_de_tramite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var conTipo = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos: [new("TST951", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var sinPublicar = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos: [new("TST952", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        await ModoAsync(empresa, conTipo.Id, "TRASPASO_UNILATERAL", ct);
        await ModoAsync(empresa, sinPublicar.Id, "TIPO_RETIRADO", ct);

        await using var db = empresa.Db();
        var lector = Lector(api, db, new UsuariosIdentidadDePrueba());
        (await lector.VehiculoAsync(Usuario(empresa.Id), conTipo.Id, conTipo.Vehiculos.Single().Id, ct)).Encabezado.TipoAnalisis
            .Should().Be("Diagnóstico Con análisis — TRASPASO UNILATERAL");
        (await lector.VehiculoAsync(Usuario(empresa.Id), sinPublicar.Id, sinPublicar.Vehiculos.Single().Id, ct)).Encabezado.TipoAnalisis
            .Should().Be("Diagnóstico Con análisis — TIPO_RETIRADO", "sin publicar sale el código");
        EtiquetaTipoAnalisis.Texto(ModoDiagnostico.ConAnalisis, "Traspaso").Should().Be("Diagnóstico Con análisis — TRASPASO");

        await using var api2 = await ApiDePrueba.ConBaseAsync();
        api2.Catalogos.FallaSiempre = StatusCode.Unavailable;
        var otra = DosEmpresas.En(api2).A;
        var caido = await TableroAyudas.SembrarAsync(otra, ct, vehiculos: [new("TST953", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        await ModoAsync(otra, caido.Id, "TRASPASO_UNILATERAL", ct);
        await using var db2 = otra.Db();
        (await Lector(api2, db2, new UsuariosIdentidadDePrueba()).VehiculoAsync(Usuario(otra.Id), caido.Id, caido.Vehiculos.Single().Id, ct))
            .Encabezado.TipoAnalisis.Should().Be("Diagnóstico Con análisis — TRASPASO_UNILATERAL", "con el catálogo caído, el código");
    }

    // AC4 (F07-C1, Q3) — Identidad no responde: el creador sale «No disponible» y el contenido se arma igual.
    [Fact]
    public async Task Identidad_caida_rotula_el_creador_no_disponible()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var creador = Guid.NewGuid();
        var identidad = new UsuariosIdentidadDePrueba { Error = new RpcException(new Status(StatusCode.Unavailable, "identidad caída de prueba")) };
        identidad.Nombres[creador] = "Ana Prueba";
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, creador: creador, vehiculos: [new("TST961", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);

        await using var db = empresa.Db();
        var contenido = await Lector(api, db, identidad).VehiculoAsync(Usuario(empresa.Id), lote.Id, lote.Vehiculos.Single().Id, ct);

        contenido.Encabezado.UsuarioCreador.Should().Be(ResolutorNombresUsuario.NoDisponible);
        contenido.Secciones.Should().ContainSingle(s => s.Fuente == "RUNT");
        contenido.ParaHuella()["usuario_creador"].Should().Be("No disponible");
    }

    // Fuga (F07-C10, R-1) — desde la compañía A: el lote de B, un vehículo de B bajo un lote de A y la cabecera de B pasada a
    // mano responden 404 NO_ENCONTRADO sin datos de B; el contenido del lote de B tampoco se arma.
    [Fact]
    public async Task Vehiculo_de_otra_compania_no_se_lee()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var deB = await TableroAyudas.SembrarAsync(empresas.B, ct, nombre: "Lote secreto de B", vehiculos: [new("TST971", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresas.B, deB.Celdas.Single(), PayloadRunt.Replace("TST901", "TST971", StringComparison.Ordinal), ct);
        var deA = await TableroAyudas.SembrarAsync(empresas.A, ct, vehiculos: [new("TST972", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var vehiculoB = deB.Vehiculos.Single().Id;

        await using var db = empresas.A.Db();
        var lector = Lector(api, db, new UsuariosIdentidadDePrueba());
        var intentos = new Func<Task>[]
        {
            () => lector.VehiculoAsync(Usuario(empresas.A.Id), deB.Id, vehiculoB, ct),
            () => lector.VehiculoAsync(Usuario(empresas.A.Id), deA.Id, vehiculoB, ct),
            () => lector.VehiculoAsync(deA.Cabecera, vehiculoB, Empresa, ct),
            () => lector.VehiculoAsync(deB.Cabecera, vehiculoB, Empresa, ct),
            () => lector.LoteAsync(Usuario(empresas.A.Id), deB.Id, ct),
            () => lector.LoteAsync(deB.Cabecera, Empresa, ct),
        };

        foreach (var intento in intentos)
        {
            var error = (await intento.Should().ThrowAsync<ReglaException>()).Which;
            error.Codigo.Should().Be("NO_ENCONTRADO");
            error.Message.Should().NotContainAny(["Lote secreto de B", "TST971", empresas.B.Id.ToString()]);
        }

        (await lector.VehiculoAsync(Usuario(empresas.A.Id), deA.Id, deA.Vehiculos.Single().Id, ct)).Encabezado.Identificador
            .Should().Be("TST972", "A sí lee su propio vehículo");
    }

    // Ajuste 2026-10-27 (PDF v2) — con la respuesta completa de la fuente guardada en la celda RUNT, el contenido trae el detalle
    // del RUNT: vehículo, información general, datos teóricos, 2 SOAT, 1 RTM, garantías (acreedor NIT con nombre y NIT completo;
    // persona natural sin nombre y con «****» + 4) y la accidentalidad sin datos de persona; ni auditoría del proveedor ni nombres.
    // El camino del ZIP (cabecera ya autorizada, lector creado por DI como EmpaquetadorZip) da el mismo detalle.
    [Fact]
    public async Task Detalle_runt_completo_sale_de_la_respuesta_de_la_fuente()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt], vehiculos: [new("ABC123", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
        var celda = await TableroAyudas.PayloadAsync(empresa, lote.Celda("ABC123", Fuente.Runt), PayloadRunt.Replace("TST901", "ABC123", StringComparison.Ordinal), ct);
        await RespuestaFuenteTests.GuardarCifradaAsync(api, celda.Id, RespuestaFuenteTests.Cifrar(api, ProtoJson(RespuestaRunt())), ct);
        var vehiculo = lote.Vehiculos.Single().Id;

        await using var db = empresa.Db();
        var contenido = await Lector(api, db, new UsuariosIdentidadDePrueba()).VehiculoAsync(Usuario(empresa.Id), lote.Id, vehiculo, ct);

        var detalle = contenido.DetalleRunt.Should().NotBeNull().And.Subject.As<DetalleRuntReporte>();
        detalle.Vehiculo.Should().Be(new DetalleRuntVehiculo("ABC123", "LT0000000123", "Particular", "AUTOMOVIL", "AUTOMOVIL", "2020", "15/03/2020", "STRIA TRANSITO PRUEBA"));
        detalle.General.Should().Be(new DetalleRuntGeneral(
            "MARCA PRUEBA", "LINEA PRUEBA", "1598", "BLANCO", "GASOLINA", "SEDAN", "4", "NO", "NO", "9ZZPRUEBA00000123", "MOTORPRUEBA123",
            "CHASISPRUEBA0123", "NO", "NO", "NO", "NO", "ACTIVO"));
        detalle.DatosTecnicos.Should().Be(new DetalleRuntTecnico("2", "0", "5", "1500"));
        detalle.Soat.Should().Equal(
            new DetalleRuntSoat("VIGENTE", "SOAT0002", "01/06/2026", "31/05/2026", "31/05/2027", "ASEGURADORA PRUEBA"),
            new DetalleRuntSoat("NO VIGENTE", "SOAT0001", "01/06/2025", "31/05/2025", "31/05/2026", "ASEGURADORA PRUEBA"));
        detalle.Rtm.Should().Equal(new DetalleRuntRtm("VIGENTE", "RTM0001", "01/02/2027", "01/02/2026", "CDA PRUEBA"));
        detalle.Solicitudes.Should().BeEmpty("Intempo manda siempre [] (§5.4)");
        detalle.GarantiasAFavor.Should().Equal(
            new DetalleRuntGarantia("BANCO PRUEBA SA", "NIT", "900123456", "SI", "10/05/2021"),
            new DetalleRuntGarantia(DetalleRuntGarantia.PersonaNatural, "CC", "1012345678", "NO", "11/06/2022"));
        detalle.Accidentalidad.Should().BeEmpty("cantidadAccidentes y nombreConductor no están en el catálogo de permitidos");
        detalle.PolizasResponsabilidadCivil.Should().BeEmpty("el contrato solo trae serialVersionUID");
        Texto(contenido).Should().NotContainAny(
            ["PERSONA NATURAL FICTICIA", "PROPIETARIO FICTICIO", "79111222", "CONDUCTOR FICTICIO", "SERIAL-SECRETO", "RTM-SERIAL",
                "RC-SERIAL", "proveedor.invalid", "intempo", "INTEMPO"]);
        contenido.ParaHuella().Should().ContainKey("runt/garantia/2").WhoseValue.Should().Contain("1012345678");

        var scope = api.App.Services.CreateScope();
        var delZip = await ActivatorUtilities.CreateInstance<LectorContenidoReporte>(scope.ServiceProvider, db)
            .VehiculoAsync(lote.Cabecera, vehiculo, Empresa, ct);
        delZip.DetalleRunt.Should().BeEquivalentTo(detalle, o => o.WithStrictOrdering(), "el ZIP lee el mismo detalle con el protector de la DI");
        delZip.ParaHuella().Should().Equal(contenido.ParaHuella());
    }

    // Ajuste 2026-10-27 (PDF v2) — el detalle es null (sin excepción) si la celda RUNT no tiene respuesta guardada (celdas de antes
    // del 2026-10-27), si el cifrado no se puede leer, si la celda no está COMPLETADO o si la respuesta no es ENCONTRADO; la huella
    // de esos vehículos no tiene claves del detalle.
    [Fact]
    public async Task Sin_respuesta_ilegible_o_celda_no_completada_el_detalle_es_null()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        string[] placas = ["TST981", "TST982", "TST983", "TST984"];
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt],
            vehiculos: [.. placas.Select(p => new Flit.Diagnostico.Tests.Tablero.VehiculoSembrado(p, [new(Fuente.Runt, EstadoCelda.Pendiente, 1)]))]);
        foreach (var placa in new[] { "TST981", "TST982", "TST984" })
            await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.Runt), PayloadRunt.Replace("TST901", placa, StringComparison.Ordinal), ct);
        await RespuestaFuenteTests.GuardarCifradaAsync(api, lote.Celda("TST982", Fuente.Runt).Id, [1, 2, 3, 4], ct);
        await RespuestaFuenteTests.GuardarCifradaAsync(api, lote.Celda("TST983", Fuente.Runt).Id, RespuestaFuenteTests.Cifrar(api, ProtoJson(RespuestaRunt("TST983"))), ct);
        var noEncontrado = new Intempo.ConsultarVehiculoIntempoResponse { Proveedor = "intempo", Resultado = Intempo.ResultadoRuntIntempo.NoEncontrado };
        await RespuestaFuenteTests.GuardarCifradaAsync(api, lote.Celda("TST984", Fuente.Runt).Id, RespuestaFuenteTests.Cifrar(api, ProtoJson(noEncontrado)), ct);

        await using var db = empresa.Db();
        var lector = Lector(api, db, new UsuariosIdentidadDePrueba());
        foreach (var placa in placas)
        {
            var contenido = await lector.VehiculoAsync(Usuario(empresa.Id), lote.Id, lote.Vehiculos.Single(v => v.Placa == placa).Id, ct);
            contenido.DetalleRunt.Should().BeNull($"{placa} no tiene detalle legible");
            contenido.ParaHuella().Keys.Should().NotContain(k => k.StartsWith("runt/", StringComparison.Ordinal));
        }
    }

    // Ajuste 2026-10-27 (PDF v2) — la huella: sin detalle es la misma de antes (sin claves runt/); con detalle cambia, y cambiar un
    // solo dato del detalle (el vencimiento de un SOAT) cambia la huella.
    [Fact]
    public void Huella_cambia_con_un_dato_del_detalle_y_sin_detalle_no_cambia()
    {
        var encabezado = new EncabezadoReporte(Guid.Empty, "Lote", Empresa, "Ana Prueba", Dato, "Simple", "ABC123", "PLACA_CARRO", "ABC123", null);
        var sinDetalle = new ContenidoReporteVehiculo(encabezado, []);
        var detalle = LectorDetalleRunt.Desde(RespuestaRunt())!;
        var conDetalle = sinDetalle with { DetalleRunt = detalle };
        var otroSoat = conDetalle with { DetalleRunt = detalle with { Soat = [detalle.Soat[0] with { FechaVencimiento = "30/05/2027" }, detalle.Soat[1]] } };

        sinDetalle.ParaHuella().Keys.Should().BeEquivalentTo(
            ["diagnostico_id", "nombre_lote", "empresa", "usuario_creador", "creado_en", "tipo_analisis", "identificador", "tipo_identificador", "placa", "vin"],
            "sin detalle la huella no cambia");
        conDetalle.ParaHuella().Should().NotEqual(sinDetalle.ParaHuella());
        otroSoat.ParaHuella()["runt/soat/1"].Should().NotBe(conDetalle.ParaHuella()["runt/soat/1"]);
        otroSoat.ParaHuella().Where(e => e.Key != "runt/soat/1").Should().BeEquivalentTo(conDetalle.ParaHuella().Where(e => e.Key != "runt/soat/1"));
    }

    // Ajuste 2026-10-27 (PDF v2) — el lector del detalle acepta el proto-JSON con los nombres del .proto (como publica
    // core-consultas) y en lowerCamelCase, y responde null sin excepción ante cifrado ilegible, JSON que no es objeto o sin cifrado.
    [Fact]
    public void Lector_del_detalle_lee_los_dos_formatos_y_tolera_lo_ilegible()
    {
        var protector = new ProtectorEnClaro();
        var camel = Google.Protobuf.JsonFormatter.Default.Format(RespuestaRunt());

        LectorDetalleRunt.Leer(protector, protector.Proteger(ProtoJson(RespuestaRunt()))).Should().BeEquivalentTo(LectorDetalleRunt.Desde(RespuestaRunt()));
        LectorDetalleRunt.Leer(protector, protector.Proteger(camel)).Should().BeEquivalentTo(LectorDetalleRunt.Desde(RespuestaRunt()));
        LectorDetalleRunt.Leer(protector, null).Should().BeNull();
        LectorDetalleRunt.Leer(protector, [9, 9, 9]).Should().BeNull("el cifrado no se puede leer");
        LectorDetalleRunt.Leer(protector, protector.Proteger("[1,2]")).Should().BeNull("no es un objeto");
        LectorDetalleRunt.Leer(protector, protector.Proteger("""{"resultado":{"no":"es del contrato"}}""")).Should().BeNull();
    }

    // HU #14752 SP-7 — el descifrado del documento del propietario falla con una excepción que cita PII: la celda sale vacía, el lote se
    // genera (el Excel se escribe sin excepción) y el log (Warning NoDescifradoExcel) solo lleva el vehículo, el rol y el tipo de la
    // excepción: ni el número, ni el nombre del locatario, ni el mensaje. El contenido del vehículo (PDF, VehiculoAsync) no cambia: no
    // trae los datos del Excel.
    [Fact]
    public async Task HU14752_SP7_documento_no_descifrable_deja_la_celda_vacia_y_el_log_sin_pii()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt], vehiculos: [new("TST9F1", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
        var celda = await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST9F1", Fuente.Runt), PayloadRunt.Replace("TST901", "TST9F1", StringComparison.Ordinal), ct);
        await RespuestaFuenteTests.GuardarCifradaAsync(api, celda.Id, RespuestaFuenteTests.Cifrar(api, ProtoJson(ExcelLoteTests.RespuestaExcel("TST9F1"))), ct);
        var real = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        byte[] ilegible = [0xBA, 0xD0, 0x01];
        var vehiculo = lote.Vehiculos.Single().Id;
        await using (var escritura = empresa.Db())
        {
            var fila = await escritura.Set<VehiculoDiagnostico>().SingleAsync(v => v.Id == vehiculo, ct);
            fila.PropietarioDocTipo = "CC";
            fila.PropietarioDocCifrado = ilegible;
            fila.LocatarioDocTipo = "CC";
            fila.LocatarioDocCifrado = real.Proteger("1098765432");
            fila.LocatarioDocOrigen = OrigenDocumento.Proveedor;
            await escritura.SaveChangesAsync(ct);
        }

        var captura = new Catalogo.CapturaLogs();
        using var fabrica = LoggerFactory.Create(b => b.AddProvider(captura).SetMinimumLevel(LogLevel.Trace));
        var protector = new ProtectorDocumentoQueFalla(real, ilegible, new InvalidOperationException("SECRETO 1055566677 LOCATARIO FICTICIO UNO"));
        await using var db = empresa.Db();
        var lector = Lector(api, db, new UsuariosIdentidadDePrueba(), protector: protector, logger: fabrica.CreateLogger<LectorContenidoReporte>());

        var delLote = await lector.LoteAsync(Usuario(empresa.Id), lote.Id, ct);

        var excel = delLote.Vehiculos.Single().Excel.Should().NotBeNull().And.Subject.As<DatosExcelVehiculo>();
        excel.PropietarioTipo.Should().Be("CC");
        excel.PropietarioNumero.Should().BeNull("no se pudo descifrar: la celda sale vacía");
        excel.LocatarioNumero.Should().Be("1098765432");
        excel.LocatarioNombre.Should().Be("LOCATARIO FICTICIO UNO");
        var generar = () => Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Write(
            ExcelLote.Hojas(delLote, new Dictionary<Guid, DatosTecnicosVehiculo>(), null));
        generar.Should().NotThrow();
        var avisos = captura.Lineas.Where(l => l.Contains("Excel del lote", StringComparison.Ordinal)).ToList();
        avisos.Should().ContainSingle().Which.Should().StartWith("Warning").And.Contain(vehiculo.ToString()).And.Contain("PROPIETARIO")
            .And.Contain(nameof(InvalidOperationException));
        captura.Lineas.Should().NotContain(l => l.Contains("1055566677", StringComparison.Ordinal) || l.Contains("1098765432", StringComparison.Ordinal)
            || l.Contains("LOCATARIO FICTICIO", StringComparison.Ordinal) || l.Contains("SECRETO", StringComparison.Ordinal));

        var delVehiculo = await Lector(api, db, new UsuariosIdentidadDePrueba()).VehiculoAsync(Usuario(empresa.Id), lote.Id, vehiculo, ct);
        Texto(delVehiculo).Should().NotContainAny(["1098765432", "LOCATARIO FICTICIO UNO"], "el PDF no lee los datos del Excel");
        delVehiculo.DetalleRunt!.GarantiasAFavor.Should().Contain(g => g.Acreedor == DetalleRuntGarantia.PersonaNatural, "el PDF sigue con «Persona natural»");
    }

    // HU #14752 (revisión del carril) — una respuesta RUNT legible cuya proyección para el Excel lanza: el lote se genera igual, las
    // columnas ampliadas del RUNT quedan vacías (salvo 1-4, que salen del vehículo), sale el aviso D4 y el log solo trae el vehículo y el
    // tipo de la excepción, nunca su mensaje.
    [Fact]
    public async Task HU14752_proyeccion_del_runt_que_falla_deja_las_columnas_vacias_y_el_aviso()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt], vehiculos: [new("TST9F2", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
        var celda = await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST9F2", Fuente.Runt), PayloadRunt.Replace("TST901", "TST9F2", StringComparison.Ordinal), ct);
        await RespuestaFuenteTests.GuardarCifradaAsync(api, celda.Id, RespuestaFuenteTests.Cifrar(api, ProtoJson(ExcelLoteTests.RespuestaExcel("TST9F2"))), ct);
        var vehiculo = lote.Vehiculos.Single().Id;
        await using (var escritura = empresa.Db())
        {
            var fila = await escritura.Set<VehiculoDiagnostico>().SingleAsync(v => v.Id == vehiculo, ct);
            fila.PropietarioDocTipo = "CC";
            fila.PropietarioDocCifrado = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Proteger("1055566677");
            await escritura.SaveChangesAsync(ct);
        }

        var captura = new Catalogo.CapturaLogs();
        using var fabrica = LoggerFactory.Create(b => b.AddProvider(captura).SetMinimumLevel(LogLevel.Trace));
        await using var db = empresa.Db();
        var lector = Lector(api, db, new UsuariosIdentidadDePrueba(), logger: fabrica.CreateLogger<LectorContenidoReporte>());
        lector.ProyectarRuntExcel = _ => throw new InvalidOperationException("SECRETO LOCATARIO FICTICIO UNO");

        var delLote = await lector.LoteAsync(Usuario(empresa.Id), lote.Id, ct);

        var excel = delLote.Vehiculos.Single().Excel!;
        excel.Runt.Should().BeNull();
        excel.RuntSinDetalle.Should().BeTrue("sale el aviso D4");
        excel.PropietarioNumero.Should().Be("1055566677", "1-4 salen del vehículo");
        var libro = ExcelDePrueba.Leer(Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Write(
            ExcelLote.Hojas(delLote, new Dictionary<Guid, DatosTecnicosVehiculo>(), null)));
        var filaExcel = libro.Fila("Vehículos", "TST9F2");
        foreach (var columna in ExcelLoteTests.NuevasRunt.Skip(4))
            libro.Valor("Vehículos", filaExcel, "RUNT — " + columna).Should().BeEmpty(columna);
        libro.Hojas["Diagnóstico"].Should().ContainSingle(f => f[0] == "Aviso").Which[1].Should().Be(ExcelLoteTests.AvisoD4);
        captura.Lineas.Should().ContainSingle(l => l.Contains("no proyectable", StringComparison.Ordinal))
            .Which.Should().StartWith("Warning").And.Contain(vehiculo.ToString()).And.Contain(nameof(InvalidOperationException));
        captura.Lineas.Should().NotContain(l => l.Contains("SECRETO", StringComparison.Ordinal) || l.Contains("1055566677", StringComparison.Ordinal));
    }

    /// <summary>Protector de documentos que falla con <paramref name="error"/> al descifrar <paramref name="marca"/>; lo demás va al real.</summary>
    private sealed class ProtectorDocumentoQueFalla(IProtectorDocumentoPersona real, byte[] marca, Exception error) : IProtectorDocumentoPersona
    {
        public byte[] Proteger(string numero) => real.Proteger(numero);

        public string Desproteger(byte[] cifrado) => cifrado.AsSpan().SequenceEqual(marca) ? throw error : real.Desproteger(cifrado);
    }

    /// <summary>Respuesta ENCONTRADO de core-consultas para un vehículo ficticio (placa ABC123), con datos de persona que no deben salir.</summary>
    internal static Intempo.ConsultarVehiculoIntempoResponse RespuestaRunt(string placa = "ABC123") => new()
    {
        Proveedor = "intempo",
        Resultado = Intempo.ResultadoRuntIntempo.Encontrado,
        DuracionMs = 120,
        Vehiculo = new Intempo.VehiculoIntempo
        {
            Plate = placa,
            Vin = "9ZZPRUEBA00000123",
            InformacionGeneral = new Intempo.InformacionGeneralIntempo
            {
                Cilidraje = "1598",
                ClaseVehiculo = "AUTOMOVIL",
                Clasificacion = "AUTOMOVIL",
                Color = "BLANCO",
                Linea = "LINEA PRUEBA",
                Marca = "MARCA PRUEBA",
                Modelo = "2020",
                FechaMatricula = "15/03/2020",
                NoChasis = "CHASISPRUEBA0123",
                NoLicenciaTransito = "LT0000000123",
                NoMotor = "MOTORPRUEBA123",
                NoPlaca = placa,
                NoVin = "9ZZPRUEBA00000123",
                OrganismoTransito = "STRIA TRANSITO PRUEBA",
                Puertas = "4",
                TipoCarroceria = "SEDAN",
                TipoCombustible = "GASOLINA",
                TipoServicio = "Particular",
                EsRegrabadoChasis = "NO",
                EsRegrabadoMotor = "NO",
                EsRegrabadoSerie = "NO",
                EsRegrabadoVin = "NO",
                Repotenciado = "NO",
                ClasicoAntiguo = "NO",
                EstadoDelVehiculo = "ACTIVO",
                SerialVersionUid = "SERIAL-SECRETO-1",
            },
            DatosTecnicos = new Intempo.DatosTecnicosIntempo
            {
                NoEjes = "2",
                CapacidadCarga = "0",
                PesoBrutoVehicular = "1500",
                PasajerosSentados = "5",
                SerialVersionUid = "SERIAL-SECRETO-2",
            },
            Soat =
            {
                new Intempo.SoatIntempo
                {
                    Estado = "VIGENTE", NoPoliza = "SOAT0002", FechaVigencia = "01/06/2026", FechaExpedicion = "31/05/2026",
                    FechaVencimiento = "31/05/2027", EntidadExpideSoat = "ASEGURADORA PRUEBA",
                },
                new Intempo.SoatIntempo
                {
                    Estado = "NO VIGENTE", NoPoliza = "SOAT0001", FechaVigencia = "01/06/2025", FechaExpedicion = "31/05/2025",
                    FechaVencimiento = "31/05/2026", EntidadExpideSoat = "ASEGURADORA PRUEBA",
                },
            },
            TecnoMecanica =
            {
                new Intempo.TecnoMecanicaIntempo
                {
                    SerialVersionUid = "RTM-SERIAL", NroCertificado = "RTM0001", TipoRevision = "PERIODICA", FechaExpedicion = "01/02/2026",
                    FechaVigente = "01/02/2027", CdaExpide = "CDA PRUEBA", Vigente = "SI", NumeroPlaca = placa, InformacionConsistente = "S",
                    Url = "https://proveedor.invalid/rtm",
                },
            },
            GarantiasFavorDe =
            {
                new Intempo.GarantiaFavorDeIntempo
                {
                    TipoDocumentoAcreedor = "NIT", NumeroDocumentoAcreedor = "900123456", Acreedor = "BANCO PRUEBA SA",
                    FechaInscripcion = "10/05/2021", Confecamaras = "SI",
                },
                new Intempo.GarantiaFavorDeIntempo
                {
                    TipoDocumentoAcreedor = "Cédula de ciudadanía", NumeroDocumentoAcreedor = "1012345678", Acreedor = "PERSONA NATURAL FICTICIA",
                    FechaInscripcion = "11/06/2022", Confecamaras = "NO",
                },
            },
            PolizasResponsabilidadCivil = { new Intempo.PolizaResponsabilidadCivilIntempo { SerialVersionUid = "RC-SERIAL" } },
            PropietariosActuales = { new Intempo.PropietarioActualIntempo { NombreCompleto = "PROPIETARIO FICTICIO", NoDocumento = "79111222", TipoDocumento = "Cédula Ciudadanía" } },
            AccidentRate = { new Intempo.AccidenteIntempo { Campos = { ["cantidadAccidentes"] = "1", ["nombreConductor"] = "CONDUCTOR FICTICIO", ["fechaUltimoAccidente"] = "" } } },
        },
    };

    /// <summary>El proto-JSON como lo publica core-consultas (nombres del .proto y valores por defecto, HU #14087 AC8).</summary>
    internal static string ProtoJson(Google.Protobuf.IMessage mensaje) => BusDePrueba.FormatoConsultas.Format(mensaje);

    /// <summary>Protector de prueba en claro con una marca: lo que no la trae no se puede «descifrar».</summary>
    private sealed class ProtectorEnClaro : IProtectorRespuestaFuente
    {
        private static readonly byte[] Marca = [0x46, 0x4C];

        public byte[] Proteger(string texto) => [.. Marca, .. Encoding.UTF8.GetBytes(texto)];

        public string Desproteger(byte[] cifrado) =>
            cifrado.Length >= Marca.Length && cifrado.AsSpan(0, Marca.Length).SequenceEqual(Marca)
                ? Encoding.UTF8.GetString(cifrado.AsSpan(Marca.Length))
                : throw new CryptographicException("cifrado de prueba ilegible");
    }

    internal static LectorContenidoReporte Lector(
        ApiDePrueba api, DiagnosticoDb db, UsuariosIdentidadDePrueba identidad, OpcionesRedaccion? redaccion = null,
        IProtectorDocumentoPersona? protector = null, ILogger<LectorContenidoReporte>? logger = null)
    {
        var scope = api.App.Services.CreateScope();
        return new LectorContenidoReporte(
            db, protector ?? api.App.Services.GetRequiredService<IProtectorDocumentoPersona>(), api.App.Services.GetRequiredService<IProtectorRespuestaFuente>(),
            identidad.Resolutor(), scope.ServiceProvider.GetRequiredService<ICatalogoTiposTramite>(),
            Options.Create(redaccion ?? new OpcionesRedaccion()), logger ?? NullLogger<LectorContenidoReporte>.Instance);
    }

    /// <summary>Quien pide: usuario de la empresa con <c>diagnosticos.ver</c> y el claim <c>company_name</c> ficticio.</summary>
    internal static UsuarioActual Usuario(Guid empresa, string companyName = Empresa)
    {
        var identidad = new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("tenant_id", empresa.ToString()), new Claim("company_name", companyName),
                new Claim("permissions", Permisos.DiagnosticosVer)],
            "prueba");
        return new UsuarioActual(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identidad) } }, new EmpresaFija(empresa));
    }

    /// <summary>El contenido serializado (propiedades en PascalCase, enums como número) para buscar fugas en el texto.</summary>
    internal static string Texto(object contenido) => JsonSerializer.Serialize(contenido, contenido.GetType());

    private static string Codigo(Fuente fuente) => JsonNamingPolicy.SnakeCaseUpper.ConvertName(fuente.ToString());

    private static async Task MismaFechaAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        await db.Set<CeldaConsulta>().Where(c => c.DiagnosticoId == lote).ExecuteUpdateAsync(s => s.SetProperty(c => c.DatoObtenidoEn, Dato), ct);
    }

    private static async Task CreadoEnAsync(EmpresaDePrueba empresa, Guid lote, DateTimeOffset creadoEn, CancellationToken ct)
    {
        await using var db = empresa.Db();
        await db.Set<CabeceraDiagnostico>().Where(c => c.Id == lote).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreadoEn, creadoEn), ct);
    }

    private static async Task ModoAsync(EmpresaDePrueba empresa, Guid lote, string tipo, CancellationToken ct)
    {
        await using var db = empresa.Db();
        await db.Set<CabeceraDiagnostico>().Where(c => c.Id == lote)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Modo, ModoDiagnostico.ConAnalisis).SetProperty(c => c.TipoTramiteCodigo, tipo), ct);
    }

    private static async Task<PersonaVehiculo> PersonaAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, Guid vehiculo, RolPersona rol, int orden, string tipo, string numero, CancellationToken ct,
        OrigenDocumento origen = OrigenDocumento.Proveedor)
    {
        var persona = new PersonaVehiculo
        {
            VehiculoId = vehiculo,
            Rol = rol,
            Orden = orden,
            DocTipo = tipo,
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
    private static string Personas(params (Guid Id, int Orden, string Consulta, string Estado, string? Resultado, string? TipoError)[] personas) =>
        "{\"personas\":[" + string.Join(",", personas.Select(p =>
            $$"""{"persona_id":"{{p.Id}}","orden":{{p.Orden}},"consulta":"{{p.Consulta}}","estado":"{{p.Estado}}"{{(p.Resultado is null ? string.Empty : ",\"resultado\":" + p.Resultado)}}{{(p.TipoError is null ? string.Empty : $",\"tipo_error\":\"{p.TipoError}\"")}}{{(p.Estado == "OK" ? ",\"consultado_en\":\"2026-10-22T15:00:00+00:00\"" : string.Empty)}}}""")) + "]}";
}
