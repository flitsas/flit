using System.Globalization;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace Flit.Diagnostico.Api.Modulos.Vehiculos;

/// <summary>
/// Filtros de <c>GET /vehiculos</c> ya validados (plan §6.6, HU #14576 AC39, SP-10). Todos los valores son de lista cerrada; un
/// valor fuera de su lista o de su rango responde 400 (nunca se recorta en silencio) con <c>campo</c>:
/// <c>FILTRO_INVALIDO</c>, <c>RANGO_INVALIDO</c> (<c>hasta</c> antes de <c>desde</c>) o <c>PAGINACION_INVALIDA</c>
/// (<c>pagina</c> &lt; 1, <c>tamano</c> fuera de 1..<see cref="TamanoMaximo"/>, <c>pagina × tamano</c> &gt; <see cref="DesplazamientoMaximo"/>).
/// </summary>
internal sealed record FiltroVehiculos(
    string? Placa,
    IReadOnlySet<string> Estados,
    IReadOnlySet<string> Veredictos,
    bool? ConSaneamiento,
    IReadOnlySet<EstadoSaneamiento> EstadosSaneamiento,
    ModoDiagnostico? Modo,
    bool? ConTramite,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta,
    int Pagina,
    int Tamano)
{
    public const string EstadoEnProceso = "EN_PROCESO";
    public const string EstadoTerminado = "TERMINADO";
    public const string SinDictamen = "SIN_DICTAMEN";
    public const int TamanoPorDefecto = 25;
    public const int TamanoMaximo = 100;
    public const int DesplazamientoMaximo = 10_000;
    public const int PlacaMaxima = 20;

    private static readonly string[] EstadosValidos = [EstadoEnProceso, EstadoTerminado];
    private static readonly string[] VeredictosValidos = ["VIABLE", "BLOQUEADO", SinDictamen];

    /// <summary>Estados del saneamiento por los que se filtra: los no anulados (plan §6.6).</summary>
    public static readonly IReadOnlyList<EstadoSaneamiento> EstadosSaneamientoFiltrables =
        [EstadoSaneamiento.Solicitado, EstadoSaneamiento.EnGestion, EstadoSaneamiento.Saneado, EstadoSaneamiento.NoProcede];

    public static FiltroVehiculos Leer(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string? placa = null;
        if (Unico(query, "placa") is { Length: > 0 } texto)
        {
            if (texto.Length > PlacaMaxima)
                throw Invalido(FiltroListado.CodigoFiltroInvalido, $"placa admite hasta {PlacaMaxima} caracteres.", "placa");
            // SP-10: la misma regla del identificador; lo que no es A-Z ni 0-9 (también % y _) se quita, y si no queda nada es 400.
            placa = ValidadorIdentificador.Normalizar(texto);
            if (placa.Length == 0)
                throw Invalido(FiltroListado.CodigoFiltroInvalido, "placa debe tener letras o dígitos.", "placa");
        }

        var estados = Cerrado(query, "estado", EstadosValidos);
        var veredictos = Cerrado(query, "veredicto", VeredictosValidos);
        bool? conSaneamiento = Unico(query, "saneamiento") switch
        {
            null or "" => null,
            "CON" => true,
            "SIN" => false,
            _ => throw Invalido(FiltroListado.CodigoFiltroInvalido, "saneamiento es CON o SIN.", "saneamiento"),
        };
        var estadosSaneamiento = new HashSet<EstadoSaneamiento>();
        foreach (var valor in Valores(query, "saneamiento_estado"))
        {
            if (!CodigosDeEnum.TryLeer<EstadoSaneamiento>(valor, out var estado) || !EstadosSaneamientoFiltrables.Contains(estado))
                throw Invalido(FiltroListado.CodigoFiltroInvalido, "saneamiento_estado es SOLICITADO, EN_GESTION, SANEADO o NO_PROCEDE.", "saneamiento_estado");
            estadosSaneamiento.Add(estado);
        }

        if (estadosSaneamiento.Count > 0)
        {
            if (conSaneamiento == false)
                throw Invalido(FiltroListado.CodigoFiltroInvalido, "saneamiento_estado implica saneamiento=CON.", "saneamiento_estado");
            conSaneamiento = true;
        }

        ModoDiagnostico? modo = null;
        if (Unico(query, "modo") is { Length: > 0 } m)
        {
            if (!CodigosDeEnum.TryLeer<ModoDiagnostico>(m, out var elegido))
                throw Invalido(FiltroListado.CodigoFiltroInvalido, "modo es SIMPLE o CON_ANALISIS.", "modo");
            modo = elegido;
        }

        bool? conTramite = Unico(query, "con_tramite") switch
        {
            null or "" => null,
            "true" => true,
            "false" => false,
            _ => throw Invalido(FiltroListado.CodigoFiltroInvalido, "con_tramite es true o false.", "con_tramite"),
        };

        // Fechas, rango y paginación con las reglas del listado de diagnósticos; el tamaño por defecto y el desplazamiento son de aquí.
        var fechas = FiltroListado.Leer(Unico(query, "desde"), Unico(query, "hasta"), null, null, null, "1", "1");
        var pagina = Entero(Unico(query, "pagina"), "pagina", 1);
        if (pagina < 1)
            throw Invalido(FiltroListado.CodigoPaginacionInvalida, "pagina empieza en 1.", "pagina");
        var tamano = Entero(Unico(query, "tamano"), "tamano", TamanoPorDefecto);
        if (tamano is < 1 or > TamanoMaximo)
            throw Invalido(FiltroListado.CodigoPaginacionInvalida, $"tamano va de 1 a {TamanoMaximo}.", "tamano");
        if ((long)pagina * tamano > DesplazamientoMaximo)
            throw Invalido(FiltroListado.CodigoPaginacionInvalida, $"pagina × tamano no puede pasar de {DesplazamientoMaximo}.", "pagina");

        return new FiltroVehiculos(placa, estados, veredictos, conSaneamiento, estadosSaneamiento, modo, conTramite, fechas.Desde, fechas.Hasta, pagina, tamano);
    }

    private static string? Unico(IQueryCollection query, string campo)
    {
        var valores = query[campo];
        if (valores.Count > 1)
            throw Invalido(FiltroListado.CodigoFiltroInvalido, $"{campo} va una sola vez.", campo);
        return valores.Count == 0 ? null : valores[0];
    }

    private static IEnumerable<string> Valores(IQueryCollection query, string campo) =>
        query[campo].Where(v => !string.IsNullOrEmpty(v)).Select(v => v!);

    private static HashSet<string> Cerrado(IQueryCollection query, string campo, string[] validos)
    {
        var elegidos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var valor in Valores(query, campo))
        {
            if (!validos.Contains(valor, StringComparer.Ordinal))
                throw Invalido(FiltroListado.CodigoFiltroInvalido, $"{campo} es {string.Join(", ", validos)}.", campo);
            elegidos.Add(valor);
        }

        return elegidos;
    }

    private static int Entero(string? valor, string campo, int porDefecto)
    {
        if (string.IsNullOrEmpty(valor))
            return porDefecto;
        return int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw Invalido(FiltroListado.CodigoPaginacionInvalida, $"{campo} es un número entero.", campo);
    }

    private static ReglaException Invalido(string codigo, string mensaje, string campo) =>
        new(codigo, mensaje, StatusCodes.Status400BadRequest) { Campo = campo };
}

/// <summary>Una página de la grilla de placas.</summary>
internal sealed record ListadoVehiculosDto(
    [property: JsonPropertyName("pagina")] int Pagina,
    [property: JsonPropertyName("tamano")] int Tamano,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("vehiculos")] IReadOnlyList<FilaPlacaDto> Vehiculos);

/// <summary>Una fila de la grilla de placas (plan §6.6).</summary>
internal sealed record FilaPlacaDto(
    [property: JsonPropertyName("vehiculo_id")] Guid VehiculoId,
    [property: JsonPropertyName("placa")] string Placa,
    [property: JsonPropertyName("diagnostico_id")] Guid DiagnosticoId,
    [property: JsonPropertyName("diagnostico_nombre")] string DiagnosticoNombre,
    [property: JsonPropertyName("diagnostico_creado_en")] DateTimeOffset DiagnosticoCreadoEn,
    [property: JsonPropertyName("modo")] string Modo,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("veredicto")] string? Veredicto,
    [property: JsonPropertyName("saneamiento")] SaneamientoFilaPlacaDto Saneamiento,
    [property: JsonPropertyName("tramite")] TramiteFilaPlacaDto? Tramite);

/// <summary>Saneamiento de la fila: estado no anulado (o null), si se puede solicitar y por qué no.</summary>
internal sealed record SaneamientoFilaPlacaDto(
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("elegible")] bool Elegible,
    [property: JsonPropertyName("motivo_no_elegible")] string? MotivoNoElegible);

/// <summary>Trámite creado desde el vehículo.</summary>
internal sealed record TramiteFilaPlacaDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("numero")] string? Numero);

/// <summary>
/// Grilla de placas de la compañía (plan §6.6, HU #14576 AC39/AC34, S12): los vehículos en proceso y terminados de los lotes que el
/// usuario ve. Parte de <see cref="AccesoLote.VisiblesAsync"/> (la compañía la pone <c>TenantFilter</c>; con PROPIOS, solo los lotes
/// propios) unido por <c>(tenant_id, diagnostico_id)</c> (SP-5); los lotes sin lanzar (CREADO) no salen. El <c>estado</c> se calcula
/// con <see cref="AvanceVehiculo"/> sobre las celdas (C1): TERMINADO si tiene celdas y todas en COMPLETADO o NA, si no EN_PROCESO.
/// El veredicto es el del dictamen vigente (null sin dictamen; el filtro lo llama <c>SIN_DICTAMEN</c>). El saneamiento sale de la
/// columna desnormalizada <c>vehiculos.saneamiento_estado</c>, sin unir con <c>saneamientos</c>. Orden: creación del lote
/// descendente, luego placa e id. La placa se busca por prefijo con <c>StartsWith</c> (SP-10). Sin logs con placa ni documento.
/// </summary>
internal sealed class ListadoVehiculos(DiagnosticoDb db, UsuarioActual usuario)
{
    public async Task<ListadoVehiculosDto> ListarAsync(FiltroVehiculos filtro, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var cabeceras = await AccesoLote.VisiblesAsync(db.Set<CabeceraDiagnostico>().AsNoTracking(), db, usuario, ct).ConfigureAwait(false);
        cabeceras = cabeceras.Where(c => c.Estado != EstadoDiagnostico.Creado);
        if (filtro.Desde is { } desde)
            cabeceras = cabeceras.Where(c => c.CreadoEn >= desde);
        if (filtro.Hasta is { } hasta)
            cabeceras = cabeceras.Where(c => c.CreadoEn < hasta);
        if (filtro.Modo is { } modo)
            cabeceras = cabeceras.Where(c => c.Modo == modo);

        var celdas = db.Set<CeldaConsulta>().AsNoTracking();
        var dictamenes = db.Set<DictamenVehiculo>().AsNoTracking().Where(d => d.Vigente);
        var filas =
            from v in db.Set<VehiculoDiagnostico>().AsNoTracking()
            join c in cabeceras on new { v.TenantId, v.DiagnosticoId } equals new { c.TenantId, DiagnosticoId = c.Id }
            select new
            {
                Vehiculo = v,
                Cabecera = c,
                Terminado = celdas.Any(x => x.TenantId == v.TenantId && x.VehiculoId == v.Id)
                    && !celdas.Any(x => x.TenantId == v.TenantId && x.VehiculoId == v.Id && x.Estado != EstadoCelda.Completado && x.Estado != EstadoCelda.Na),
                Veredicto = dictamenes.Where(d => d.TenantId == v.TenantId && d.VehiculoId == v.Id).Select(d => d.Veredicto).FirstOrDefault(),
            };

        if (filtro.Placa is { } placa)
            filas = filas.Where(f => f.Vehiculo.IdentificadorNorm.StartsWith(placa));
        if (filtro.Estados.Count == 1)
        {
            var terminado = filtro.Estados.Contains(FiltroVehiculos.EstadoTerminado);
            filas = filas.Where(f => f.Terminado == terminado);
        }

        if (filtro.Veredictos.Count > 0)
        {
            var conValor = filtro.Veredictos.Where(x => x != FiltroVehiculos.SinDictamen).ToArray();
            var sinDictamen = filtro.Veredictos.Contains(FiltroVehiculos.SinDictamen);
            filas = filas.Where(f => (f.Veredicto != null && conValor.Contains(f.Veredicto)) || (sinDictamen && f.Veredicto == null));
        }

        if (filtro.ConSaneamiento is { } conSaneamiento)
        {
            filas = conSaneamiento
                ? filas.Where(f => f.Vehiculo.SaneamientoEstado != null && f.Vehiculo.SaneamientoEstado != EstadoSaneamiento.Anulado)
                : filas.Where(f => f.Vehiculo.SaneamientoEstado == null || f.Vehiculo.SaneamientoEstado == EstadoSaneamiento.Anulado);
        }

        if (filtro.EstadosSaneamiento.Count > 0)
        {
            var estados = filtro.EstadosSaneamiento.Cast<EstadoSaneamiento?>().ToArray();
            filas = filas.Where(f => estados.Contains(f.Vehiculo.SaneamientoEstado));
        }

        if (filtro.ConTramite is { } conTramite)
            filas = conTramite ? filas.Where(f => f.Vehiculo.TramiteId != null) : filas.Where(f => f.Vehiculo.TramiteId == null);

        var total = await filas.CountAsync(ct).ConfigureAwait(false);
        var pagina = await filas
            .OrderByDescending(f => f.Cabecera.CreadoEn).ThenBy(f => f.Vehiculo.IdentificadorNorm).ThenBy(f => f.Vehiculo.Id)
            .Skip((filtro.Pagina - 1) * filtro.Tamano).Take(filtro.Tamano)
            .Select(f => new
            {
                f.Vehiculo.Id,
                Placa = f.Vehiculo.Placa ?? f.Vehiculo.Vin ?? f.Vehiculo.IdentificadorNorm,
                DiagnosticoId = f.Cabecera.Id,
                f.Cabecera.Nombre,
                f.Cabecera.CreadoEn,
                f.Cabecera.Modo,
                f.Terminado,
                f.Veredicto,
                f.Vehiculo.SaneamientoEstado,
                f.Vehiculo.TramiteId,
                f.Vehiculo.TramiteNumero,
            })
            .ToListAsync(ct).ConfigureAwait(false);

        return new ListadoVehiculosDto(filtro.Pagina, filtro.Tamano, total, [.. pagina.Select(f =>
        {
            var vigente = f.SaneamientoEstado is { } s && s != EstadoSaneamiento.Anulado ? f.SaneamientoEstado : null;
            var motivo = ElegibilidadSaneamiento.MotivoNoElegible(f.Terminado, vigente);
            return new FilaPlacaDto(
                f.Id, f.Placa, f.DiagnosticoId, f.Nombre, f.CreadoEn, CodigosDeEnum.Codigo(f.Modo),
                f.Terminado ? FiltroVehiculos.EstadoTerminado : FiltroVehiculos.EstadoEnProceso, f.Veredicto,
                new SaneamientoFilaPlacaDto(vigente is { } e ? CodigosDeEnum.Codigo(e) : null, motivo is null, motivo),
                f.TramiteId is { } tramite ? new TramiteFilaPlacaDto(tramite, f.TramiteNumero) : null);
        })]);
    }
}
