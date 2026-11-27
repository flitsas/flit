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
/// <item>Hoja «Vehículos»: una fila por vehículo en el orden de la carga; columnas fijas (con el propietario actual declarado
/// enmascarado y «Cambio de propietario» Sí/No/vacío, HU #14582) y, por fuente del alcance en el orden de la
/// grilla, estado + datos (<see cref="ColumnasExcel"/>). Lo no terminado (pendiente, N/A o «Consulta no disponible») lleva solo su
/// estado; el vehículo sale Terminado, En proceso o Con error.</item>
/// <item>Hoja «Diagnóstico»: ID, nombre del lote, empresa, usuario creador, fecha de creación (hora Colombia) y tipo de análisis.</item>
/// <item>Hoja «Consultas»: solo si la grilla trae el estimado (<c>estimado_consultas=AL_FINALIZAR</c> en la copia y el lote
/// COMPLETADO o INCOMPLETO): fuente, estimadas, consumidas y «al menos» si <c>minimo=true</c>.</item>
/// </list>
/// Pinta SOLO lo que recibe (R-20): sin origen, usuario manual, proveedor, transacción ni tipo de error. HU #14752 (v4, D3; ADR-0076
/// enmienda 2026-11-27): en claro solo las columnas de <see cref="ColumnasExcel.EnClaro"/>, que salen de <see cref="VehiculoReporte.Excel"/>;
/// el resto de documentos sigue enmascarado. La huella usa <see cref="FirmaHuella"/>, sin esas celdas (SP-2). El texto que parece fórmula
/// queda literal (<see cref="TabularWorkbookWriter.PareceFormula"/>).
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
    /// <summary>
    /// Versión del diseño: entra en la huella (súbela al cambiar hojas, columnas o cómo se pinta una celda). 2 = monto de impuesto null no
    /// es cero. 3 = columnas del propietario actual declarado y del cambio de propietario (HU #14582). 4 = hoja v1 ampliada con PII de
    /// D3 (ola Excel lote, HU #14752).
    /// </summary>
    public const int VersionPlantilla = 4;

    public const string ContentType = TabularWorkbookWriter.ContentType;

    public const string HojaVehiculos = "Vehículos";

    public const string HojaDiagnostico = "Diagnóstico";

    public const string HojaConsultas = "Consultas";

    public const string Terminado = RotulosDiagnostico.VehiculoTerminado;

    public const string EnProceso = RotulosDiagnostico.VehiculoEnProceso;

    public const string ConError = RotulosDiagnostico.VehiculoConError;

    public const string AlMenos = "al menos";

    /// <summary>HU #14752 (D4): fila de la hoja «Diagnóstico» cuando algún RUNT completado no tiene la respuesta completa guardada.</summary>
    public const string Aviso = "Aviso";

    public const string TextoAviso = "Algunas consultas son anteriores al detalle completo de la fuente: sus columnas ampliadas pueden ir vacías";

    // HU #14752 (D8): valores fijados de las columnas nuevas.
    public const string ImpuestoVigente = "VIGENTE";

    public const string ImpuestoConDeuda = "CON DEUDA";

    public const string ImpuestoNoDisponible = "NO DISPONIBLE";

    public const string SinMultas = "Sin multas";

    public const string ConMultas = "Con multas";

    public const string SinComparendos = "Sin comparendos";

    private const string Separador = "; ";

    private const string Truncado = "…";

    private static readonly string[] FormatosFecha =
    [
        "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.fff", "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.fffzzz",
    ];

    private static readonly NumberFormatInfo Miles = new() { NumberGroupSeparator = ".", NumberGroupSizes = [3] };

    /// <summary><c>diagnostico-{8 primeros del id}.xlsx</c>.</summary>
    public static string NombreArchivo(Guid diagnosticoId) => $"diagnostico-{diagnosticoId.ToString()[..8]}.xlsx";

    /// <summary>Las hojas del libro, en el orden «Vehículos», «Diagnóstico» y (si aplica) «Consultas».</summary>
    public static IReadOnlyList<Hoja> Hojas(
        ContenidoReporteLote contenido, IReadOnlyDictionary<Guid, DatosTecnicosVehiculo> tecnicos, IReadOnlyList<EstimadoVsConsumidoDto>? estimado)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        ArgumentNullException.ThrowIfNull(tecnicos);
        var aviso = contenido.Vehiculos.Any(v => v.Excel?.RuntSinDetalle == true);
        var hojas = new List<Hoja> { Vehiculos(contenido, tecnicos), Diagnostico(contenido.Encabezado, aviso) };
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

    /// <summary>
    /// HU #14752 (SP-2): la <see cref="Firma"/> de una copia de las hojas en la que las celdas de <see cref="ColumnasExcel.EnClaro"/> se
    /// sustituyen: el número de documento por «****» + 4 (<see cref="EnmascaradorDocumento"/>); el nombre del locatario, las garantías a
    /// favor, las garantías mobiliarias y el detalle de comparendos del locatario por "" (celda vacía).
    /// Va al encabezado de la huella: ningún dato personal en claro, ni un SHA sin clave de él, entra a la evidencia.
    /// <para>
    /// Consecuencia aceptada (contrato-excel §3.5): un cambio solo del nombre (o de una lista) con el mismo documento no cambia esta firma;
    /// lo detectan la versión del tablero y las celdas de la huella, que suben cuando la consulta que trae ese dato se vuelve a guardar.
    /// </para>
    /// </summary>
    public static string FirmaHuella(IReadOnlyList<Hoja> hojas)
    {
        ArgumentNullException.ThrowIfNull(hojas);
        return Firma([.. hojas.Select(SinDatosEnClaro)]);
    }

    private static Hoja SinDatosEnClaro(Hoja hoja)
    {
        var sustituir = hoja.Columns.Select(c => DatoEnClaro(c.Header)).ToList();
        if (sustituir.All(d => d is null))
            return hoja;
        return hoja with
        {
            Rows = [.. hoja.Rows.Select(fila => (IReadOnlyList<Celda>)[.. fila.Select((celda, i) => Sustituir(celda, i < sustituir.Count ? sustituir[i] : null))])],
        };
    }

    private static Celda Sustituir(Celda celda, string? dato)
    {
        if (dato is null || celda is not Celda.Str texto)
            return celda;
        return ColumnasExcel.DocumentosEnClaro.Contains(dato) ? Celda.Of(EnmascaradorDocumento.Enmascarar(texto.Value)) : Celda.Empty;
    }

    /// <summary>El dato de <see cref="ColumnasExcel.EnClaro"/> de un encabezado «{fuente} — {dato}», o null.</summary>
    private static string? DatoEnClaro(string encabezado)
    {
        var separador = encabezado.LastIndexOf(" — ", StringComparison.Ordinal);
        var dato = separador < 0 ? encabezado : encabezado[(separador + 3)..];
        return ColumnasExcel.EnClaro.Contains(dato) ? dato : null;
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
                Celda.Of(v.Orden), Celda.Of(v.Identificador), Celda.Of(RotulosDiagnostico.DeTipoIdentificador(v.TipoIdentificador)), Celda.Of(v.Placa), Celda.Of(v.Vin),
                Celda.Of(v.PropietarioDeclarado), RotulosDiagnostico.DeCambioPropietario(v.CambioPropietario) is { } cambio ? Celda.Of(cambio) : Celda.Empty,
                Celda.Of(EstadoVehiculo(v.Secciones)), Celda.Of(v.VehiculoId.ToString("D")),
            };
            foreach (var seccion in v.Secciones)
            {
                fila.Add(Celda.Of(EstadoFuente(seccion)));
                fila.AddRange(Datos(seccion, tecnicos.GetValueOrDefault(v.VehiculoId), v.Excel));
            }

            fila.AddRange(Enumerable.Repeat(Celda.Empty, ColumnasExcel.SinFuente.Count));
            return (IReadOnlyList<Celda>)fila;
        }).ToList();
        return new Hoja(HojaVehiculos, columnas, filas);
    }

    private static Hoja Diagnostico(EncabezadoLoteReporte e, bool aviso)
    {
        List<IReadOnlyList<Celda>> filas =
        [
            Par("ID del diagnóstico", e.DiagnosticoId.ToString()),
            Par("Nombre del lote", e.NombreLote),
            Par("Empresa", e.Empresa),
            Par("Usuario creador", e.UsuarioCreador),
            Par("Fecha de creación", e.CreadoEn.ToOffset(VigenciaDelDia.OffsetColombia).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (hora Colombia)"),
            Par("Tipo de análisis", e.TipoAnalisis),
        ];
        if (aviso)
            filas.Add(Par(Aviso, TextoAviso));
        return new Hoja(HojaDiagnostico, [new Columna("Campo", 24), new Columna("Valor", 48)], filas);
    }

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
        EstadoSeccion.NoDisponible => seccion.Fuente == "IMPUESTO_DEPARTAMENTAL" ? "No confirmado" : "Consulta no disponible",
        _ => "Pendiente",
    };

    /// <summary>
    /// Las celdas de dato de la sección (tantas como columnas de dato tiene su fuente); vacías si no está COMPLETADO, salvo «Estado del
    /// impuesto» con la sección NO DISPONIBLE (HU #14752, D8).
    /// </summary>
    private static IEnumerable<Celda> Datos(SeccionReporte seccion, DatosTecnicosVehiculo? tecnicos, DatosExcelVehiculo? excel)
    {
        var cantidad = ColumnasExcel.DatosDe(seccion.Fuente).Count;
        if (cantidad == 0)
            return [];
        if (seccion.Estado == EstadoSeccion.NoDisponible && seccion.Fuente == "IMPUESTO_DEPARTAMENTAL")
            return [.. Enumerable.Repeat(Celda.Empty, cantidad - 1), Celda.Of(ImpuestoNoDisponible)];
        if (seccion.Estado != EstadoSeccion.Completado)
            return Enumerable.Repeat(Celda.Empty, cantidad);

        List<Celda> datos = seccion switch
        {
            { Resultado: ResultadoRuntDetalle r } =>
            [
                Celda.Of(r.Servicio), Celda.Of(r.Clase), Celda.Of(r.Organismo), Celda.Of(tecnicos?.Marca), Celda.Of(tecnicos?.Linea),
                tecnicos?.Modelo is { } modelo ? Celda.Of(modelo) : Celda.Empty, Celda.Of(r.Propietarios?.Count ?? 0), Celda.Of(r.Locatarios),
                .. RuntV4(excel),
            ],
            { Resultado: ResultadoSimitDetalle s } =>
            [
                Celda.Of(s.Semaforo), Celda.Of(s.CantidadComparendos), Valor([s]),
                s.CantidadResoluciones is { } resoluciones ? Celda.Of(resoluciones) : Celda.Empty,
                Celda.Of(s.CantidadComparendos > 0 ? ConMultas : SinMultas),
                Celda.Of(ListaTruncada((s.Comparendos ?? []).Select(c => c.Numero), s.ComparendosTruncados)),
                Celda.Of(ListaTruncada((s.Comparendos ?? []).Select(c => FH(c.Fecha)), s.ComparendosTruncados)),
                Celda.Of(ListaTruncada((s.Comparendos ?? []).Select(c => Pesos(c.Valor)), s.ComparendosTruncados)),
                Celda.Of(s.CantidadComparendos + (s.CantidadResoluciones ?? 0)),
            ],
            { Resultado: ResultadoAvaluoDetalle a } =>
            [
                !a.SinValorComercial && a.ValorSugerido is { } valor ? Celda.Of((double)valor) : Celda.Empty,
                a.SinValorComercial || a.FuentePrincipal is null ? Celda.Empty : Celda.Of(RotulosDiagnostico.DeFuenteAvaluo(a.FuentePrincipal)),
                Celda.Of(a.SinValorComercial ? "Sí" : "No"),
            ],
            { Resultado: ResultadoImpuestoDetalle i } =>
                [MontoImpuesto(i), Celda.Of((i.Vigencias ?? []).Count(v => !v.Pagada)), Celda.Of(i.TieneDeuda ? ImpuestoConDeuda : ImpuestoVigente)],
            { Personas: { } personas } => DePersonas(seccion.Fuente, personas, cantidad),
            _ => [],
        };
        return datos.Count == cantidad ? datos : [.. datos, .. Enumerable.Repeat(Celda.Empty, Math.Max(0, cantidad - datos.Count))];
    }

    /// <summary>El monto null o la deuda sin valor no se escriben como 0. Sin deuda tampoco.</summary>
    private static Celda MontoImpuesto(ResultadoImpuestoDetalle impuesto)
    {
        if (impuesto.TieneDeuda && impuesto.MontoTotal is null or 0)
            return Celda.Of(PdfVehiculo.DeudaSinValor);
        if (impuesto.MontoTotal is > 0)
            return Celda.Of((double)impuesto.MontoTotal.Value);
        return Celda.Of(PdfVehiculo.SinDeuda);
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

        if (fuente == "SIMIT_LOCATARIO")
            celdas.AddRange(DetalleLocatario(personas));
        return celdas;
    }

    /// <summary>
    /// HU #14752 (contrato-excel §2.3): detalle de comparendos de cada locatario OK, con su documento en claro (D3), y el total. Sin
    /// personas OK, las dos vacías. Un documento que no se pudo descifrar (null o «****») queda «[TIPO-]».
    /// </summary>
    private static IEnumerable<Celda> DetalleLocatario(IReadOnlyList<PersonaReporte> personas)
    {
        var ok = personas
            .Where(p => p.Estado == EstadoPersonaReporte.Ok)
            .Select(p => (Persona: p, Simit: p.Resultado as ResultadoSimitDetalle))
            .Where(p => p.Simit is not null)
            .ToList();
        if (ok.Count == 0)
            return [Celda.Empty, Celda.Empty];
        var partes = new List<string>();
        foreach (var (persona, simit) in ok)
        {
            var documento = persona.Documento is { } d && d != EnmascaradorDocumento.Mascara ? d : string.Empty;
            var prefijo = $"[{persona.TipoDocumento}-{documento}]";
            var comparendos = simit!.Comparendos ?? [];
            if (comparendos.Count == 0)
                partes.Add($"{prefijo} {SinComparendos}");
            else
                partes.AddRange(comparendos.Select(c => $"{prefijo} Número: {c.Numero} | Fecha: {FH(c.Fecha)} | Valor: {PesosCo(c.Valor)}"));
        }

        return [Celda.Of(string.Join(Separador, partes)), Celda.Of(ok.Sum(p => p.Simit!.CantidadComparendos))];
    }

    /// <summary>HU #14752 (contrato-excel §2.1): las 25 columnas nuevas del RUNT. 1-4 del vehículo; 5-25 de la respuesta (vacías sin ella).</summary>
    private static List<Celda> RuntV4(DatosExcelVehiculo? excel)
    {
        var celdas = new List<Celda>
        {
            Celda.Of(excel?.PropietarioTipo), Celda.Of(excel?.PropietarioNumero), Celda.Of(excel?.LocatarioTipo), Celda.Of(excel?.LocatarioNumero),
        };
        if (excel?.Runt is not { } runt)
        {
            celdas.AddRange(Enumerable.Repeat(Celda.Empty, 21));
            return celdas;
        }

        var d = runt.Detalle;
        var soat = MasReciente(d.Soat, s => s.FechaVencimiento);
        var rtm = MasReciente(d.Rtm, r => r.FechaVigencia);
        celdas.AddRange(
        [
            Celda.Of(excel.LocatarioNombre),
            Celda.Of(soat?.Estado), Celda.Of(F(soat?.FechaVencimiento)),
            Celda.Of(SN(rtm?.Estado)), Celda.Of(F(rtm?.FechaVigencia)),
            Celda.Of(Lista(runt.GarantiasMobiliarias.Select(g =>
                Unir(" - ", Unir(" ", g.Entidad, Documento(g.TipoDocumento, g.NumeroDocumento)), g.Estado, F(g.FechaRegistro))))),
            Celda.Of(Lista(runt.GarantiasAFavor.Select(g => Unir(" ", g.Acreedor, Documento(g.TipoDocumento, g.NumeroDocumento))))),
            Celda.Of(Lista((d.Limitaciones ?? []).Select(l => Unir(" - ", l.Tipo, l.Entidad, F(l.FechaExpedicion))))),
            Celda.Of(Lista(d.Accidentalidad.Select(a => $"{a.Etiqueta}: {a.Valor}"))),
            Celda.Of(SN(d.Blindaje?.Blindado)), Celda.Of(runt.NivelBlindajeNumero ?? d.Blindaje?.Nivel),
            Celda.Of(F(d.Vehiculo.FechaMatricula)), Celda.Of(d.General.Cilindraje), Celda.Of(d.Vehiculo.Clasificacion), Celda.Of(d.General.Color),
            Celda.Of(d.General.EstadoVehiculo), Celda.Of(d.General.NumeroChasis), Celda.Of(d.General.NumeroMotor), Celda.Of(SN(d.General.TieneGravamenes)),
            Celda.Of(d.General.TipoCarroceria), Celda.Of(d.General.TipoCombustible),
        ]);
        return celdas;
    }

    /// <summary>La entrada con la fecha (dd/MM/yyyy) mayor; la que no parsea cuenta como la mínima y en empate gana la primera.</summary>
    private static T? MasReciente<T>(IReadOnlyList<T> items, Func<T, string?> fecha)
        where T : class
    {
        T? mejor = null;
        var mayor = DateTime.MinValue;
        foreach (var item in items)
        {
            var valor = DateTime.TryParseExact(fecha(item), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var leida) ? leida : DateTime.MinValue;
            if (mejor is null || valor > mayor)
            {
                mejor = item;
                mayor = valor;
            }
        }

        return mejor;
    }

    /// <summary>«(TIPO NÚMERO)» con las partes que hay; null sin ninguna.</summary>
    private static string? Documento(string? tipo, string? numero) => Unir(" ", tipo, numero) is { } doc ? $"({doc})" : null;

    /// <summary>Las partes no vacías unidas con <paramref name="separador"/>; null si no queda ninguna.</summary>
    private static string? Unir(string separador, params string?[] partes)
    {
        var texto = string.Join(separador, partes.Where(p => !string.IsNullOrWhiteSpace(p)));
        return texto.Length == 0 ? null : texto;
    }

    private static string? Lista(IEnumerable<string?> elementos) => Unir(Separador, [.. elementos]);

    /// <summary>La lista y, si el SIMIT trajo más de los listados, «; …» al final (D8).</summary>
    private static string? ListaTruncada(IEnumerable<string?> elementos, bool truncada)
    {
        var lista = Lista(elementos);
        if (!truncada)
            return lista;
        return lista is null ? Truncado : lista + Separador + Truncado;
    }

    /// <summary>Fecha dd/MM/yyyy (texto); lo que no parsea, recortado tal cual.</summary>
    private static string? F(string? texto) =>
        Fecha(texto, out var fecha, out _) ? fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : Recortar(texto);

    /// <summary>Fecha dd/MM/yyyy HH:mm:ss si el origen trae hora, dd/MM/yyyy si no (no se inventa la hora); lo que no parsea, tal cual.</summary>
    private static string? FH(string? texto) => Fecha(texto, out var fecha, out var conHora)
        ? fecha.ToString(conHora ? "dd/MM/yyyy HH:mm:ss" : "dd/MM/yyyy", CultureInfo.InvariantCulture)
        : Recortar(texto);

    /// <summary>La fecha de los formatos de <see cref="FormatosFecha"/>, con la hora tal como viene (sin convertir de zona).</summary>
    private static bool Fecha(string? texto, out DateTime fecha, out bool conHora)
    {
        fecha = default;
        conHora = false;
        if (Recortar(texto) is not { } limpio
            || !DateTimeOffset.TryParseExact(limpio, FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var leida))
            return false;
        fecha = leida.DateTime;
        conHora = limpio.Contains('T', StringComparison.Ordinal);
        return true;
    }

    /// <summary>SI / NO de los valores conocidos; otro valor, recortado tal cual; vacío, null.</summary>
    private static string? SN(string? texto) => Recortar(texto) is not { } limpio ? null : limpio.ToUpperInvariant() switch
    {
        "SI" or "SÍ" or "S" or "TRUE" or "VIGENTE" => "SI",
        "NO" or "N" or "FALSE" or "NO VIGENTE" => "NO",
        _ => limpio,
    };

    /// <summary>Pesos del SIMIT («604100.00») como entero sin separadores («604100»); null si no parsea.</summary>
    private static string? Pesos(string? valor) => Entero(valor) is { } pesos ? pesos.ToString("0", CultureInfo.InvariantCulture) : null;

    /// <summary>«$ » y punto de miles («$ 604.100»), sin CultureInfo("es-CO") (globalización invariante); vacío si no parsea.</summary>
    private static string PesosCo(string? valor) => Entero(valor) is { } pesos ? "$ " + pesos.ToString("#,0", Miles) : string.Empty;

    private static decimal? Entero(string? valor) =>
        decimal.TryParse(Recortar(valor), NumberStyles.Number, CultureInfo.InvariantCulture, out var pesos) ? decimal.Truncate(pesos) : null;

    private static string? Recortar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

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
/// el estado del lote y la <see cref="ExcelLote.FirmaHuella"/> de lo que muestra, sin datos en claro) y registro de <see cref="ServicioEvidencias"/> (tipo XLSX,
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
            // HU #14752 (SP-2): la firma sin las celdas en claro de D3.
            ["excel/contenido"] = ExcelLote.FirmaHuella(hojas),
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
