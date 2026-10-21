using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Estimacion;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Carga;

/// <summary>Lo que el análisis toma de la cabecera: su alcance y, de la copia de la configuración, el límite y si muestra el estimado.</summary>
internal sealed record ParametrosAnalisis(IReadOnlyList<Fuente> Alcance, int LimiteVehiculosLote, bool MostrarEstimado);

/// <summary>
/// Análisis local del archivo de carga (HU #13749, F02-H4, F02-C4/C7/C8/C10/C11/C13): sin estado, sin base y sin fuentes
/// externas. Por fila: <see cref="ValidadorIdentificador"/> (la regla de Trámites, HU #13740) y, aparte, la regla de la
/// carga para entradas demasiado largas (decisión del usuario 2026-10-21): si el identificador limpio (mayúsculas, solo
/// A-Z y 0-9, antes de recortar) pasa de 8 caracteres y el validador lo tomó como placa, o de 17 y lo tomó como VIN, la fila
/// es errónea; nunca se acepta el valor recortado. Después, la forma del documento del locatario
/// (<see cref="ValidadorDocumentoPersona"/>). Los identificadores válidos repetidos se consolidan en la primera fila y se
/// cuentan como duplicados. Avisa sin válidos y si los válidos superan el límite del lote (en ambos casos no se puede
/// continuar). El estimado (<see cref="EstimadorConsultas"/>, HU #13747) solo si la configuración lo muestra antes de
/// lanzar. El documento del locatario sale siempre enmascarado.
/// </summary>
/// <example>
/// <code>
/// var resumen = AnalizadorCarga.Analizar(FormatoCarga.Leer("carga.csv", bytes), new ParametrosAnalisis([Fuente.Runt], 500, false));
/// // resumen.TotalFilas == resumen.Validos + resumen.Erroneos + resumen.Duplicados
/// </code>
/// </example>
internal static partial class AnalizadorCarga
{
    public const string CodigoIdentificadorRequerido = "IDENTIFICADOR_REQUERIDO";
    public const string CodigoIdentificadorInvalido = "IDENTIFICADOR_INVALIDO";
    public const string CodigoIdentificadorDemasiadoLargo = "IDENTIFICADOR_DEMASIADO_LARGO";
    public const string CodigoSinValidos = "SIN_VALIDOS";
    public const string CodigoLimiteSuperado = "LIMITE_SUPERADO";
    public const string CodigoColumnaRequerida = "COLUMNA_REQUERIDA";

    private const int LargoPlaca = 8;
    private const int LargoVin = 17;

    public static ResumenValidacionDto Analizar(TablaCarga tabla, ParametrosAnalisis parametros)
    {
        ArgumentNullException.ThrowIfNull(tabla);
        ArgumentNullException.ThrowIfNull(parametros);
        var columnaIdentificador = Indice(tabla.Encabezados, PlantillaCarga.ColumnaIdentificador)
            ?? throw new Plataforma.ReglaException(CodigoColumnaRequerida,
                $"El archivo no tiene la columna {PlantillaCarga.ColumnaIdentificador} en la primera fila: use la plantilla.")
            {
                Campo = PlantillaCarga.ColumnaIdentificador,
            };
        var columnaTipo = Indice(tabla.Encabezados, PlantillaCarga.ColumnaTipoDocumento);
        var columnaDocumento = Indice(tabla.Encabezados, PlantillaCarga.ColumnaDocumento);

        var filas = new List<FilaEnCurso>();
        var porIdentificador = new Dictionary<string, FilaEnCurso>(StringComparer.Ordinal);
        var duplicados = 0;
        foreach (var leida in tabla.Filas)
        {
            var fila = Analizar(leida, columnaIdentificador, columnaTipo, columnaDocumento);
            if (fila.Valido is not null && porIdentificador.TryGetValue(fila.Dto.IdentificadorNorm, out var primera))
            {
                primera.Duplicadas.Add(leida.Numero);
                duplicados++;
                continue;
            }

            if (fila.Valido is not null)
                porIdentificador[fila.Dto.IdentificadorNorm] = fila;
            filas.Add(fila);
        }

        var resultado = filas.Select(f => f.Dto with { FilasDuplicadas = f.Duplicadas }).ToList();
        var validos = resultado.Count(f => f.Valida);
        var superaLimite = validos > parametros.LimiteVehiculosLote;
        var avisos = new List<AvisoCargaDto>();
        if (validos == 0)
            avisos.Add(new AvisoCargaDto(CodigoSinValidos, "El archivo no tiene ningún registro válido: corrija los errores o suba otro archivo para continuar."));
        if (superaLimite)
        {
            avisos.Add(new AvisoCargaDto(CodigoLimiteSuperado,
                $"El archivo tiene {validos} vehículos válidos y el límite por lote es {parametros.LimiteVehiculosLote}: divida el archivo o retire vehículos para continuar."));
        }

        var estimado = parametros.MostrarEstimado
            ? EstimadorConsultas.Estimar(parametros.Alcance, filas.Where(f => f.Dto.Valida).Select(f => new VehiculoAEstimar(true, f.ConDocumento)))
                .Select(e => new EstimadoFuenteDto(CodigosDeEnum.Codigo(e.Fuente), e.Consultas)).ToList()
            : null;

        return new ResumenValidacionDto(
            tabla.Filas.Count, validos, resultado.Count - validos, duplicados, PuedeContinuar: validos > 0 && !superaLimite,
            parametros.LimiteVehiculosLote, superaLimite, avisos, resultado, estimado);
    }

    /// <summary>Documento de persona enmascarado (como Comparendos, RN-011): <c>****</c> y los 4 últimos caracteres.</summary>
    public static string Enmascarar(string numero)
    {
        ArgumentNullException.ThrowIfNull(numero);
        return numero.Length <= 4 ? "****" : $"****{numero[^4..]}";
    }

    private static FilaEnCurso Analizar(FilaLeida leida, int columnaIdentificador, int? columnaTipo, int? columnaDocumento)
    {
        var original = Celda(leida, columnaIdentificador) ?? string.Empty;
        var tipoCrudo = columnaTipo is { } t ? Celda(leida, t) : null;
        var documentoCrudo = columnaDocumento is { } d ? Celda(leida, d) : null;
        var errores = new List<ErrorFilaDto>();

        var resultado = ValidadorIdentificador.Validar(original);
        var limpio = FueraDePlaca().Replace(original.ToUpperInvariant(), string.Empty);
        TipoIdentificador? tipo = resultado.Tipo;
        var normalizado = resultado.Normalizado;
        if (limpio.Length == 0)
        {
            errores.Add(ErrorIdentificador(CodigoIdentificadorRequerido, "Falta el identificador del vehículo (placa o VIN)."));
        }
        else if (tipo is { } detectado && limpio.Length > (detectado == TipoIdentificador.Vin ? LargoVin : LargoPlaca))
        {
            var esVin = detectado == TipoIdentificador.Vin;
            errores.Add(ErrorIdentificador(CodigoIdentificadorDemasiadoLargo, esVin
                ? $"El VIN tiene {limpio.Length} caracteres y el máximo es {LargoVin}: revise el valor (no se recorta)."
                : $"La placa tiene {limpio.Length} caracteres y el máximo es {LargoPlaca}: revise el valor (no se recorta)."));
            tipo = null;
            normalizado = limpio;
        }
        else if (tipo is null)
        {
            errores.Add(ErrorIdentificador(CodigoIdentificadorInvalido, "El identificador no es una placa ni un VIN válido."));
        }

        var documento = ValidadorDocumentoPersona.Validar(tipoCrudo, documentoCrudo);
        if (!documento.EsValido)
        {
            var campo = documento.Codigo == ValidadorDocumentoPersona.CodigoTipoInvalido ? PlantillaCarga.ColumnaTipoDocumento : PlantillaCarga.ColumnaDocumento;
            errores.Add(new ErrorFilaDto(campo, documento.Codigo!, documento.Mensaje!));
        }

        var numeroVisible = documento.Numero ?? (string.IsNullOrWhiteSpace(documentoCrudo) ? null : documentoCrudo.Trim());
        var tipoVisible = documento.Tipo ?? (string.IsNullOrWhiteSpace(tipoCrudo) ? null : tipoCrudo.Trim().ToUpperInvariant());
        var valida = errores.Count == 0;
        var dto = new FilaResumenDto(
            leida.Numero, original, normalizado, tipo is { } c ? CodigosDeEnum.Codigo(c) : null, tipoVisible,
            numeroVisible is null ? null : Enmascarar(numeroVisible), valida, [], errores);
        return new FilaEnCurso(dto, tipo is null ? null : normalizado, documento.Presente);
    }

    private static ErrorFilaDto ErrorIdentificador(string codigo, string mensaje) => new(PlantillaCarga.ColumnaIdentificador, codigo, mensaje);

    private static string? Celda(FilaLeida fila, int columna) => columna < fila.Celdas.Count ? fila.Celdas[columna] : null;

    /// <summary>Columna por nombre, sin distinguir mayúsculas ni espacios de los extremos; null si no está.</summary>
    private static int? Indice(IReadOnlyList<string?> encabezados, string nombre)
    {
        for (var i = 0; i < encabezados.Count; i++)
        {
            if (string.Equals(encabezados[i]?.Trim(), nombre, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return null;
    }

    // Limpieza de sanitizePlate antes del corte: mayúsculas y solo A-Z y 0-9.
    [GeneratedRegex(@"[^A-Z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex FueraDePlaca();

    /// <summary>Fila en análisis: <see cref="Valido"/> es el identificador normalizado si es placa o VIN (clave de duplicados).</summary>
    private sealed record FilaEnCurso(FilaResumenDto Dto, string? Valido, bool ConDocumento)
    {
        public List<int> Duplicadas { get; } = [];
    }
}
