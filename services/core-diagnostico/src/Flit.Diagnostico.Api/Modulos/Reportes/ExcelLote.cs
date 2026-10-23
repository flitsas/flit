using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Infraestructura.Excel;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;
using Celda = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;
using Columna = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Column;
using Hoja = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Sheet;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>Datos técnicos del bien que el RUNT deja en <c>datos_consolidados.vehiculo</c> (HU #13795) y salen en las columnas de RUNT.</summary>
internal sealed record DatosTecnicosVehiculo(string? Marca, string? Linea, int? Modelo);

/// <summary>
/// Renderizador del Excel del lote (HU #13849, F07-H4, F07-C5/C6/C9/C10): función pura del contenido de F07-H2
/// (<see cref="ContenidoReporteLote"/>), de los datos técnicos del RUNT y del estimado frente a lo consumido de la grilla (#13796).
/// <list type="bullet">
/// <item>Hoja «Vehículos»: una fila por vehículo en el orden de la carga; columnas fijas y, por fuente del alcance en el orden de la
/// grilla, estado + datos (<see cref="ColumnasExcel"/>). Lo no terminado (pendiente, N/A o «Consulta no disponible») lleva solo su
/// estado; el vehículo sale Terminado, En proceso o Con error.</item>
/// <item>Hoja «Diagnóstico»: ID, nombre del lote, empresa, usuario creador, fecha de creación (hora Colombia) y tipo de análisis.</item>
/// <item>Hoja «Consultas»: solo si la grilla trae el estimado (<c>estimado_consultas=AL_FINALIZAR</c> en la copia y el lote
/// COMPLETADO o INCOMPLETO): fuente, estimadas, consumidas y «al menos» si <c>minimo=true</c>.</item>
/// </list>
/// Pinta SOLO lo que recibe (R-20): sin origen, usuario manual, proveedor, transacción ni tipo de error; los documentos ya vienen
/// enmascarados. El texto que parece fórmula queda literal (<see cref="TabularWorkbookWriter.PareceFormula"/>).
/// </summary>
/// <example>
/// <code>
/// var hojas = ExcelLote.Hojas(contenido, tecnicos, estimado);
/// var bytes = TabularWorkbookWriter.Write(hojas);
/// var nombre = ExcelLote.NombreArchivo(contenido.Encabezado.DiagnosticoId); // diagnostico-0192ab34.xlsx
/// </code>
/// </example>
internal static class ExcelLote
{
    /// <summary>Versión del diseño: entra en la huella (súbela al cambiar hojas, columnas o cómo se pinta una celda).</summary>
    public const int VersionPlantilla = 1;

    public const string ContentType = TabularWorkbookWriter.ContentType;

    public const string HojaVehiculos = "Vehículos";

    public const string HojaDiagnostico = "Diagnóstico";

    public const string HojaConsultas = "Consultas";

    public const string Terminado = "Terminado";

    public const string EnProceso = "En proceso";

    public const string ConError = "Con error";

    public const string AlMenos = "al menos";

    /// <summary><c>diagnostico-{8 primeros del id}.xlsx</c>.</summary>
    public static string NombreArchivo(Guid diagnosticoId) => $"diagnostico-{diagnosticoId.ToString()[..8]}.xlsx";

    /// <summary>Las hojas del libro, en el orden «Vehículos», «Diagnóstico» y (si aplica) «Consultas».</summary>
    public static IReadOnlyList<Hoja> Hojas(
        ContenidoReporteLote contenido, IReadOnlyDictionary<Guid, DatosTecnicosVehiculo> tecnicos, IReadOnlyList<EstimadoVsConsumidoDto>? estimado)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        ArgumentNullException.ThrowIfNull(tecnicos);
        var hojas = new List<Hoja> { Vehiculos(contenido, tecnicos), Diagnostico(contenido.Encabezado) };
        if (estimado is not null)
            hojas.Add(Consultas(estimado));
        return hojas;
    }

    /// <summary>
    /// SHA-256 (hex) de todo lo que el libro muestra (nombres de hoja, encabezados y cada celda con su tipo): va al encabezado de la
    /// huella para que cambiar columnas, la hoja «Consultas», los datos técnicos o el estado de un vehículo invalide el archivo.
    /// </summary>
    public static string Firma(IReadOnlyList<Hoja> hojas)
    {
        ArgumentNullException.ThrowIfNull(hojas);
        var canonico = hojas.Select(h => new
        {
            hoja = h.Name,
            columnas = h.Columns.Select(c => c.Header),
            filas = h.Rows.Select(f => f.Select(Texto)),
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonico))));
    }

    private static string Texto(Celda celda) => celda switch
    {
        Celda.Str s => "s:" + s.Value,
        Celda.Num n => "n:" + n.Value.ToString("R", CultureInfo.InvariantCulture),
        Celda.Fecha f => "f:" + f.Value.ToString("O", CultureInfo.InvariantCulture),
        Celda.FechaHora fh => $"fh:{fh.Value.ToString("O", CultureInfo.InvariantCulture)}T{fh.Hour}:{fh.Minute}",
        _ => string.Empty,
    };

    private static Hoja Vehiculos(ContenidoReporteLote contenido, IReadOnlyDictionary<Guid, DatosTecnicosVehiculo> tecnicos)
    {
        var fuentes = contenido.Vehiculos.Count > 0 ? contenido.Vehiculos[0].Secciones.Select(s => s.Fuente).ToList() : [];
        var columnas = ColumnasExcel.Encabezados(fuentes)
            .Select((encabezado, i) => new Columna(encabezado, i < ColumnasExcel.Fijas.Count ? 16 : 20))
            .ToList();
        var filas = contenido.Vehiculos.Select(v =>
        {
            var fila = new List<Celda>
            {
                Celda.Of(v.Orden), Celda.Of(v.Identificador), Celda.Of(v.TipoIdentificador), Celda.Of(v.Placa), Celda.Of(v.Vin),
                Celda.Of(EstadoVehiculo(v.Secciones)),
            };
            foreach (var seccion in v.Secciones)
            {
                fila.Add(Celda.Of(EstadoFuente(seccion)));
                fila.AddRange(Datos(seccion, tecnicos.GetValueOrDefault(v.VehiculoId)));
            }

            return (IReadOnlyList<Celda>)fila;
        }).ToList();
        return new Hoja(HojaVehiculos, columnas, filas);
    }

    private static Hoja Diagnostico(EncabezadoLoteReporte e) => new(
        HojaDiagnostico,
        [new Columna("Campo", 24), new Columna("Valor", 48)],
        [
            Par("ID del diagnóstico", e.DiagnosticoId.ToString()),
            Par("Nombre del lote", e.NombreLote),
            Par("Empresa", e.Empresa),
            Par("Usuario creador", e.UsuarioCreador),
            Par("Fecha de creación", e.CreadoEn.ToOffset(VigenciaDelDia.OffsetColombia).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (hora Colombia)"),
            Par("Tipo de análisis", e.TipoAnalisis),
        ]);

    private static Hoja Consultas(IReadOnlyList<EstimadoVsConsumidoDto> estimado) => new(
        HojaConsultas,
        [new Columna("Fuente", 24), new Columna("Consultas estimadas"), new Columna("Consultas consumidas"), new Columna("Observación")],
        [.. estimado.Select(e => (IReadOnlyList<Celda>)
        [
            Celda.Of(ColumnasExcel.Titulo(e.Fuente)), Celda.Of(e.Estimadas), Celda.Of(e.Consumidas), e.Minimo ? Celda.Of(AlMenos) : Celda.Empty,
        ])]);

    private static IReadOnlyList<Celda> Par(string campo, string valor) => [Celda.Of(campo), Celda.Of(valor)];

    /// <summary>Con error si alguna fuente quedó «consulta no disponible»; Terminado si todas están completas o no aplican.</summary>
    private static string EstadoVehiculo(IReadOnlyList<SeccionReporte> secciones)
    {
        if (secciones.Any(s => s.Estado == EstadoSeccion.NoDisponible))
            return ConError;
        return secciones.Count > 0 && secciones.All(s => s.Estado is EstadoSeccion.Completado or EstadoSeccion.NoAplica) ? Terminado : EnProceso;
    }

    private static string EstadoFuente(SeccionReporte seccion) => seccion.Estado switch
    {
        EstadoSeccion.Completado => "Completado",
        EstadoSeccion.NoAplica => string.IsNullOrWhiteSpace(seccion.Leyenda) ? "No aplica" : "No aplica: " + seccion.Leyenda,
        EstadoSeccion.NoDisponible => "Consulta no disponible",
        _ => "Pendiente",
    };

    /// <summary>Las celdas de dato de la sección (tantas como columnas de dato tiene su fuente); vacías si no está COMPLETADO.</summary>
    private static IEnumerable<Celda> Datos(SeccionReporte seccion, DatosTecnicosVehiculo? tecnicos)
    {
        var cantidad = ColumnasExcel.DatosDe(seccion.Fuente).Count;
        if (cantidad == 0)
            return [];
        if (seccion.Estado != EstadoSeccion.Completado)
            return Enumerable.Repeat(Celda.Empty, cantidad);

        List<Celda> datos = seccion switch
        {
            { Resultado: ResultadoRuntDetalle r } =>
            [
                Celda.Of(r.Servicio), Celda.Of(r.Clase), Celda.Of(r.Organismo), Celda.Of(tecnicos?.Marca), Celda.Of(tecnicos?.Linea),
                tecnicos?.Modelo is { } modelo ? Celda.Of(modelo) : Celda.Empty, Celda.Of(r.Propietarios?.Count ?? 0), Celda.Of(r.Locatarios),
            ],
            { Resultado: ResultadoSimitDetalle s } => [Celda.Of(s.Semaforo), Celda.Of(s.CantidadComparendos), Valor([s])],
            { Resultado: ResultadoAvaluoDetalle a } =>
            [
                !a.SinValorComercial && a.ValorSugerido is { } valor ? Celda.Of((double)valor) : Celda.Empty,
                a.SinValorComercial || a.FuentePrincipal is null ? Celda.Empty : Celda.Of(PdfVehiculo.FuentesAvaluo.GetValueOrDefault(a.FuentePrincipal, a.FuentePrincipal)),
                Celda.Of(a.SinValorComercial ? "Sí" : "No"),
            ],
            { Personas: { } personas } => DePersonas(seccion.Fuente, personas, cantidad),
            _ => [],
        };
        return datos.Count == cantidad ? datos : [.. datos, .. Enumerable.Repeat(Celda.Empty, Math.Max(0, cantidad - datos.Count))];
    }

    private static List<Celda> DePersonas(string fuente, IReadOnlyList<PersonaReporte> personas, int cantidad)
    {
        var documentos = string.Join("; ", personas.Select(p =>
            $"{p.TipoDocumento} {p.DocumentoEnmascarado}" + (p.RotuloDeclarado is { } rotulo ? $" ({rotulo})" : string.Empty)));
        var celdas = new List<Celda>
        {
            Celda.Of(personas.Count(p => p.Estado == EstadoPersonaReporte.Ok)),
            Celda.Of(personas.Count(p => p.Estado == EstadoPersonaReporte.NoDisponible)),
            Celda.Of(documentos),
        };
        if (cantidad > celdas.Count && fuente.StartsWith("SIMIT", StringComparison.Ordinal))
        {
            var simit = personas.Where(p => p.Estado == EstadoPersonaReporte.Ok).Select(p => p.Resultado).OfType<ResultadoSimitDetalle>().ToList();
            celdas.Add(Celda.Of(simit.Sum(s => s.CantidadComparendos)));
            celdas.Add(Valor(simit));
        }

        return celdas;
    }

    /// <summary>
    /// Suma de los valores de los comparendos listados; si el SIMIT trajo más de los que se listan (tope de #13798), «al menos …».
    /// </summary>
    private static Celda Valor(IReadOnlyList<ResultadoSimitDetalle> resultados)
    {
        var suma = resultados.SelectMany(r => r.Comparendos ?? [])
            .Sum(c => decimal.TryParse(c.Valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var pesos) ? pesos : 0m);
        return resultados.Any(r => r.ComparendosTruncados)
            ? Celda.Of($"{AlMenos} {suma.ToString(CultureInfo.InvariantCulture)}")
            : Celda.Of((double)suma);
    }
}

/// <summary>
/// Descarga del Excel del lote para la compañía (HU #13849): visibilidad de <see cref="AccesoLote"/> (404 si el lote no se ve), luego
/// 409 <see cref="CodigoLoteSinLanzar"/> si el lote sigue CREADO; contenido de <see cref="LectorContenidoReporte"/> con la empresa del
/// claim <c>company_name</c>, datos técnicos del RUNT, estimado frente a consumido de la grilla (<see cref="ConsultaGrilla.EstimadoAsync"/>,
/// sin duplicar el cálculo de #13796), huella del lote (<see cref="HuellaEvidencia"/> con <see cref="ExcelLote.VersionPlantilla"/>,
/// el estado del lote y la <see cref="ExcelLote.Firma"/> de lo que muestra) y registro de <see cref="ServicioEvidencias"/> (tipo XLSX,
/// sin vehículo): misma huella → los bytes guardados; si cambió algo, genera uno nuevo e invalida el anterior.
/// </summary>
internal sealed class DescargaExcelLote(DiagnosticoDb db, UsuarioActual usuario, LectorContenidoReporte lector, ServicioEvidencias evidencias)
{
    public const string CodigoLoteSinLanzar = "LOTE_SIN_LANZAR";

    public async Task<ArchivoGenerado> ObtenerAsync(Guid diagnosticoId, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, diagnosticoId, seguir: false, ct).ConfigureAwait(false);
        if (cabecera.Estado == EstadoDiagnostico.Creado)
            throw ReglaException.Conflicto(CodigoLoteSinLanzar, "El diagnóstico todavía no se ha lanzado: su Excel sale cuando empiecen las consultas.");

        var contenido = await lector.LoteAsync(cabecera, usuario.EmpresaNombre, ct).ConfigureAwait(false);
        var tecnicos = await TecnicosAsync(cabecera.Id, cabecera.TenantId, ct).ConfigureAwait(false);
        var estimado = await ConsultaGrilla.EstimadoAsync(new LecturaDeLaCompania(db), cabecera, ct).ConfigureAwait(false);
        var hojas = ExcelLote.Hojas(contenido, tecnicos, estimado);

        var encabezado = new Dictionary<string, string?>(contenido.ParaHuella(), StringComparer.Ordinal)
        {
            ["lote/estado"] = CodigosDeEnum.Codigo(cabecera.Estado),
            ["excel/contenido"] = ExcelLote.Firma(hojas),
        };
        var clave = ClaveEvidencia.Xlsx(cabecera.Id);
        var entrada = await HuellaEvidencia.LeerEntradaAsync(db, clave, ExcelLote.VersionPlantilla, encabezado, ct).ConfigureAwait(false);
        var nombre = ExcelLote.NombreArchivo(cabecera.Id);
        var entregada = await evidencias.ObtenerOGenerarAsync(
            clave, HuellaEvidencia.Calcular(entrada), usuario.Id,
            _ => Task.FromResult(new ArchivoGenerado(TabularWorkbookWriter.Write(hojas), nombre, ExcelLote.ContentType)),
            ct).ConfigureAwait(false);
        return new ArchivoGenerado(entregada.Contenido, nombre, ExcelLote.ContentType);
    }

    /// <summary>Marca, línea y modelo de <c>datos_consolidados.vehiculo</c> de cada vehículo del lote (los que el RUNT ya dejó).</summary>
    private async Task<IReadOnlyDictionary<Guid, DatosTecnicosVehiculo>> TecnicosAsync(Guid diagnosticoId, Guid tenant, CancellationToken ct)
    {
        var filas = await db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.DiagnosticoId == diagnosticoId && v.TenantId == tenant)
            .Select(v => new { v.Id, v.DatosConsolidados })
            .ToListAsync(ct).ConfigureAwait(false);
        var tecnicos = new Dictionary<Guid, DatosTecnicosVehiculo>();
        foreach (var fila in filas)
        {
            if (Leer(fila.DatosConsolidados) is { } datos)
                tecnicos[fila.Id] = datos;
        }

        return tecnicos;
    }

    private static DatosTecnicosVehiculo? Leer(string? consolidados)
    {
        if (string.IsNullOrWhiteSpace(consolidados))
            return null;
        try
        {
            using var documento = JsonDocument.Parse(consolidados);
            if (documento.RootElement.ValueKind != JsonValueKind.Object
                || !documento.RootElement.TryGetProperty("vehiculo", out var vehiculo) || vehiculo.ValueKind != JsonValueKind.Object)
                return null;
            return new DatosTecnicosVehiculo(Texto(vehiculo, "marca"), Texto(vehiculo, "linea"),
                vehiculo.TryGetProperty("modelo", out var modelo) && modelo.ValueKind == JsonValueKind.Number && modelo.TryGetInt32(out var anio) ? anio : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Texto(JsonElement objeto, string clave) =>
        objeto.TryGetProperty(clave, out var valor) && valor.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(valor.GetString())
            ? valor.GetString()!.Trim()
            : null;
}
