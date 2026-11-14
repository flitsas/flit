using System.Net.Http.Json;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Ayudas del panel de deuda (HU #14576 AC36-AC38): contenido de reporte armado en memoria (para las pruebas puras de
/// <c>ConsultaDeuda.Armar</c>, <c>PdfDeuda</c> y <c>ExcelDeuda</c>), rutas, tokens y siembra de personas con documento cifrado.
/// Placas TST7## y documentos ficticios.
/// </summary>
/// <example>
/// <code>
/// var contenido = DeudaDePrueba.Contenido("TST701", [DeudaDePrueba.Completada("SIMIT_VEHICULO", DeudaDePrueba.Simit(2))]);
/// var panel = ConsultaDeuda.Armar(Guid.NewGuid(), contenido, ModoDiagnostico.Simple, null, null, DeudaDePrueba.Hoy, null);
/// </code>
/// </example>
internal static class DeudaDePrueba
{
    public static readonly DateOnly Hoy = new(2026, 11, 14);

    public static readonly DateTimeOffset Dato = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);

    public const string DocumentoPropietario = "10203040";

    public static string RutaPanel(Guid id, Guid vid) => $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{id}/vehiculos/{vid}/deuda";

    public static string RutaPdf(Guid id, Guid vid) => RutaPanel(id, vid) + "/pdf";

    public static string RutaExcel(Guid id, Guid vid) => RutaPanel(id, vid) + "/excel";

    public static string RutaExcelVarios => $"{Program.Prefijo}/vehiculos/deuda/excel";

    public static string TokenVer(ApiDePrueba api, Guid empresa) =>
        api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);

    public static string TokenDescargar(ApiDePrueba api, Guid empresa) =>
        api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosVer, Permisos.ReportesDescargar], roles: ["usuario_diagnostico"]);

    public static ContenidoReporteVehiculo Contenido(
        string placa, IEnumerable<SeccionReporte> secciones, DetalleRuntReporte? runt = null, string nombreLote = "Lote deuda") =>
        new(
            new EncabezadoReporte(Guid.NewGuid(), nombreLote, "Empresa de prueba", "Ana Prueba", Dato, "Diagnóstico Simple", placa, "PLACA_CARRO", placa, null),
            [.. secciones],
            runt);

    public static SeccionReporte Completada(string fuente, ResultadoDetalleDto? resultado, IReadOnlyList<PersonaReporte>? personas = null) =>
        new(fuente, "POR_VEHICULO", EstadoSeccion.Completado, null, null, Dato, resultado, personas);

    public static SeccionReporte NoAplica(string fuente, string motivo, bool porPersona = true) =>
        new(fuente, "POR_PERSONA", EstadoSeccion.NoAplica, motivo, "No aplica", null, null, porPersona ? [] : null);

    public static SeccionReporte ConError(string fuente) =>
        new(fuente, "POR_VEHICULO", EstadoSeccion.NoDisponible, null, SeccionReporte.ConsultaNoDisponible, null, null, null);

    public static PersonaReporte Persona(int orden, string rol, string documento, ResultadoSimitDetalle? resultado) =>
        new(orden, rol, "C", "****" + documento[^4..], false, null, EjecutorCeldaSimitPersona.ConsultaSimit,
            resultado is null ? EstadoPersonaReporte.NoDisponible : EstadoPersonaReporte.Ok, Dato, resultado, documento);

    public static ResultadoSimitDetalle Simit(int comparendos, string valor = "450000.00", bool? pazYSalvo = null, string? saldoAcuerdo = null, string estado = "PENDIENTE", string? numero = null) =>
        new("ROJO", comparendos,
            [.. Enumerable.Range(1, Math.Min(comparendos, 100)).Select(i => new ComparendoDetalle(numero ?? $"TST-C-{i:000}", "2026-09-30", valor, estado, "Organismo de prueba"))],
            comparendos > 100, TotalGeneral: "999999", TotalPorPagar: "888888", PazYSalvo: pazYSalvo,
            Acuerdos: saldoAcuerdo is null ? null : [new AcuerdoSimitDetalle("VIGENTE", "1", "3", saldoAcuerdo, "2026-01-01", "Organismo de prueba")]);

    public static ResultadoImpuestoDetalle Impuesto(long monto, bool tieneDeuda = true) =>
        new(monto, [new VigenciaImpuestoDetalle(2026, monto, false), new VigenciaImpuestoDetalle(2025, 180000, true)], tieneDeuda);

    public static ResultadoRuntDetalle RuntResultado(bool leasing = false, int locatarios = 0) =>
        new("TST701", null, "PARTICULAR", "AUTOMOVIL", "STRIA PRUEBA", [new PropietarioRuntDetalle("C", leasing ? "LEASING" : "PROPIO")], locatarios);

    /// <summary>Detalle del RUNT con los estados de SOAT y RTM dados (orden de la fuente) y la clase y matrícula dadas.</summary>
    public static DetalleRuntReporte DetalleRunt(
        string placa, string[] soat, string[] rtm, string clase = "AUTOMOVIL", string matricula = "15/03/2015", string vence = "31/05/2026")
    {
        var vehiculo = new VehiculoIntempo
        {
            Plate = placa,
            InformacionGeneral = new InformacionGeneralIntempo
            {
                NoPlaca = placa, TipoServicio = "PARTICULAR", Clasificacion = clase, ClaseVehiculo = clase, FechaMatricula = matricula,
                EstadoDelVehiculo = "ACTIVO", Prendas = "NO", TieneGravamenes = "NO",
            },
        };
        foreach (var estado in soat)
            vehiculo.Soat.Add(new SoatIntempo { Estado = estado, NoPoliza = "SOAT-0001", FechaVencimiento = vence, EntidadExpideSoat = "ASEGURADORA FICTICIA" });
        foreach (var vigente in rtm)
            vehiculo.TecnoMecanica.Add(new TecnoMecanicaIntempo { Vigente = vigente, NroCertificado = "RTM-0001", FechaVigente = "10/01/2027", CdaExpide = "CDA FICTICIO" });
        return LectorDetalleRunt.Desde(new ConsultarVehiculoIntempoResponse { Resultado = ResultadoRuntIntempo.Encontrado, Vehiculo = vehiculo })
            ?? throw new InvalidOperationException("Sin detalle.");
    }

    /// <summary>Persona con documento cifrado de verdad (el protector de la API).</summary>
    public static async Task<PersonaVehiculo> PersonaAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, Guid vehiculo, RolPersona rol, int orden, string numero, CancellationToken ct)
    {
        var persona = new PersonaVehiculo
        {
            VehiculoId = vehiculo,
            Rol = rol,
            Orden = orden,
            DocTipo = "C",
            DocCifrado = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Proteger(numero),
            Origen = OrigenDocumento.Proveedor,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        await using var db = empresa.Db();
        db.Add(persona);
        await db.SaveChangesAsync(ct);
        return persona;
    }

    /// <summary>Payload de una celda de persona con la forma de <c>EjecutorPorPersonas</c>.</summary>
    public static string PayloadPersonas(params (Guid Id, int Orden, string Resultado)[] personas) =>
        "{\"personas\":[" + string.Join(',', personas.Select(p =>
            $$"""{"persona_id":"{{p.Id}}","orden":{{p.Orden}},"consulta":"SIMIT","estado":"OK","resultado":{{p.Resultado}},"consultado_en":"2026-11-13T15:00:00+00:00"}""")) + "]}";

    public static async Task PropiosAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        await using var db = empresa.Db();
        db.Add(new Flit.Diagnostico.Api.Modulos.Configuracion.ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios });
        await db.SaveChangesAsync(ct);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta, CancellationToken ct) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

    /// <summary>Todas las claves de propiedad del JSON, a cualquier profundidad.</summary>
    public static IEnumerable<string> Claves(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                yield return p.Name;
                foreach (var hija in Claves(p.Value))
                    yield return hija;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
            {
                foreach (var hija in Claves(item))
                    yield return hija;
            }
        }
    }

    /// <summary>Las cuatro cabeceras de descarga segura (SP-18).</summary>
    public static void CabecerasSeguras(HttpResponseMessage respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        FluentAssertions.AssertionExtensions.Should(respuesta.Headers.GetValues("X-Content-Type-Options").Single()).Be("nosniff");
        FluentAssertions.AssertionExtensions.Should(respuesta.Headers.GetValues("Content-Security-Policy").Single()).Be("sandbox; default-src 'none'");
        FluentAssertions.AssertionExtensions.Should(respuesta.Headers.CacheControl!.NoStore).BeTrue();
        FluentAssertions.AssertionExtensions.Should(respuesta.Headers.Pragma.ToString()).Be("no-cache");
        FluentAssertions.AssertionExtensions.Should(respuesta.Content.Headers.ContentDisposition!.DispositionType).Be("attachment");
    }
}
