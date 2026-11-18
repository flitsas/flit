using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;

namespace Flit.Diagnostico.Tests.Estadisticas;

/// <summary>Lote sembrado para las estadísticas: la cabecera, sus vehículos y sus celdas.</summary>
internal sealed record LoteEstadistico(CabeceraDiagnostico Cabecera, IReadOnlyList<VehiculoDiagnostico> Vehiculos, IReadOnlyList<CeldaConsulta> Celdas)
{
    public Guid Id => Cabecera.Id;
}

/// <summary>
/// Siembra directa para las pruebas de estadísticas (HU #14579, BE-1): lotes con <c>creado_en</c> y modalidad elegidos (son inmutables
/// tras el primer guardado), celdas por fuente y estado, intentos con duración y disparador, dictamen vigente y derivaciones. Placas
/// ficticias del rango EST###; los documentos son bytes inventados (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// var lote = await EstadisticasDePrueba.LoteAsync(empresa, EstadisticasDePrueba.Dia(-1), ct, express: true, placas: ["EST101"]);
/// var r = await empresa.Cliente().GetAsync(EstadisticasDePrueba.Ruta(EstadisticasDePrueba.Hoy.AddDays(-6), EstadisticasDePrueba.Hoy), ct);
/// </code>
/// </example>
internal static class EstadisticasDePrueba
{
    public const string RutaCompania = Program.Prefijo + "/estadisticas";
    public const string RutaGlobal = Program.Prefijo + "/backoffice/estadisticas/global";
    public const string RutaEmpresa = Program.Prefijo + "/backoffice/estadisticas";

    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

    /// <summary>Hoy en hora Colombia.</summary>
    public static DateOnly Hoy => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Colombia).DateTime);

    /// <summary>Mediodía (hora Colombia) del día <c>hoy + desplazamiento</c>, como instante UTC (Npgsql solo escribe offset 0).</summary>
    public static DateTimeOffset Dia(int desplazamiento) =>
        new DateTimeOffset(Hoy.AddDays(desplazamiento).ToDateTime(new TimeOnly(12, 0)), Colombia).ToUniversalTime();

    public static string Fecha(DateOnly dia) => dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Ruta(DateOnly desde, DateOnly hasta, string ruta = RutaCompania, string extra = "") =>
        $"{ruta}{(ruta.Contains('?', StringComparison.Ordinal) ? "&" : "?")}desde={Fecha(desde)}&hasta={Fecha(hasta)}{extra}";

    /// <summary>Un lote con un vehículo por placa y, por cada vehículo, las celdas dadas (RUNT completado por defecto).</summary>
    public static async Task<LoteEstadistico> LoteAsync(
        EmpresaDePrueba empresa, DateTimeOffset creadoEn, CancellationToken ct, bool express = false, ModoDiagnostico modo = ModoDiagnostico.Simple,
        Guid? creador = null, string[]? placas = null, (Fuente Fuente, EstadoCelda Estado)[]? celdas = null)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        var cabecera = new CabeceraDiagnostico
        {
            Nombre = "Lote de estadísticas",
            Modo = modo,
            TipoTramiteCodigo = modo == ModoDiagnostico.ConAnalisis ? "TRASPASO_STANDARD" : null,
            EsExpress = express,
            Alcance = [Fuente.Runt],
            Configuracion = """{"estimado_consultas":"NO_MOSTRAR","visibilidad_usuario":"EMPRESA"}""",
            Estado = EstadoDiagnostico.EnProceso,
            UsuarioCreadorId = creador ?? Guid.NewGuid(),
            CreadoEn = creadoEn,
        };
        var vehiculos = (placas ?? ["EST001"]).Select((p, i) => new VehiculoDiagnostico
        {
            DiagnosticoId = cabecera.Id,
            IdentificadorOriginal = p,
            IdentificadorNorm = p,
            TipoIdentificador = TipoIdentificador.PlacaCarro,
            Placa = p,
            Orden = i + 1,
        }).ToList();
        var ahora = DateTimeOffset.UtcNow;
        var filas = vehiculos.SelectMany(v => (celdas ?? [(Fuente.Runt, EstadoCelda.Completado)]).Select(c => new CeldaConsulta
        {
            DiagnosticoId = cabecera.Id,
            VehiculoId = v.Id,
            Fuente = c.Fuente,
            Estado = c.Estado,
            MotivoNa = c.Estado == EstadoCelda.Na ? MotivoNa.SinDocumentoLocatario : null,
            TipoError = c.Estado == EstadoCelda.Error ? "TIMEOUT" : null,
            Origen = c.Estado == EstadoCelda.Completado ? OrigenCelda.Api : null,
            Payload = c.Estado == EstadoCelda.Completado ? "{}" : null,
            DatoObtenidoEn = c.Estado == EstadoCelda.Completado ? ahora : null,
            DisponibleDesde = ahora,
            ActualizadoEn = ahora,
            VersionTablero = 1,
        })).ToList();

        await using var db = empresa.Db();
        db.Add(cabecera);
        db.AddRange(vehiculos);
        db.AddRange(filas);
        await db.SaveChangesAsync(ct);
        return new LoteEstadistico(cabecera, vehiculos, filas);
    }

    /// <summary>Un intento de la celda con la duración y el disparador dados.</summary>
    public static async Task IntentoAsync(EmpresaDePrueba empresa, CeldaConsulta celda, int milisegundos, DisparadorIntento disparador, CancellationToken ct)
    {
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-10);
        await using var db = empresa.Db();
        db.Add(new IntentoConsulta
        {
            DiagnosticoId = celda.DiagnosticoId,
            VehiculoId = celda.VehiculoId,
            ConsultaId = celda.Id,
            Fuente = celda.Fuente,
            Servicio = ServicioConsulta.Intempo,
            Disparador = disparador,
            Inicio = inicio,
            Fin = inicio.AddMilliseconds(milisegundos),
            Estado = ResultadoIntento.Success,
            UsuarioId = disparador == DisparadorIntento.Manual ? Guid.NewGuid() : null,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Dictamen vigente del vehículo.</summary>
    public static async Task DictamenAsync(EmpresaDePrueba empresa, Guid vehiculo, string veredicto, CancellationToken ct)
    {
        await using var db = empresa.Db();
        db.Add(new DictamenVehiculo
        {
            VehiculoId = vehiculo,
            Version = 1,
            Vigente = true,
            Veredicto = veredicto,
            Alertamientos = "[]",
            CatalogoVersion = 1,
            RedactadoPor = "PLANTILLA",
            EstadoRedaccion = "PENDIENTE",
            CreadoEn = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Una operación individual del lote, hoy, con un ítem PENDIENTE (en curso) del vehículo.</summary>
    public static async Task DerivacionAsync(EmpresaDePrueba empresa, Guid lote, Guid vehiculo, CancellationToken ct)
    {
        var ahora = DateTimeOffset.UtcNow;
        await using var db = empresa.Db();
        var operacion = OperacionDerivacion.Masiva(empresa.Id, lote, "TRASPASO_STANDARD", Guid.NewGuid(), 1, ahora);
        db.Add(operacion);
        db.Add(ItemDerivacion.Pendiente(operacion, vehiculo, 1, ahora));
        await db.SaveChangesAsync(ct);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta, CancellationToken ct) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

    /// <summary>Usuario de la compañía con <c>diagnosticos.ver</c> (no admin): con PROPIOS solo ve sus lotes.</summary>
    public static string TokenUsuario(EmpresaDePrueba empresa, Guid usuario) =>
        empresa.Api.Token(empresa: empresa.Id, sujeto: usuario.ToString(), roles: ["usuario_diagnostico"], permisos: [Permisos.DiagnosticosVer]);

    /// <summary>Admin de la compañía (<c>configuracion.editar</c>): ve todos los lotes.</summary>
    public static string TokenAdmin(EmpresaDePrueba empresa) =>
        empresa.Api.Token(empresa: empresa.Id, roles: ["admin_diagnostico"], permisos: [Permisos.DiagnosticosVer, Permisos.ConfiguracionEditar]);
}
