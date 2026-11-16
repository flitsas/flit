using System.Globalization;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Reportes.Marca;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Renderizador del PDF corporativo de UN vehículo (HU #13848, F07-H3, F07-C1..C4/C9/C10) con el diseño v2 (ajuste 2026-10-27):
/// membrete FLIT (copia de la marca de core-api, <see cref="FlitLetterhead"/>) en todas las páginas; en la primera, el bloque de
/// identificación (Diagnóstico ID, placa —o VIN— y fecha de generación en hora Colombia) y la tabla «DATOS DEL DIAGNÓSTICO» (lote,
/// tipo de análisis, fecha de creación, empresa, creador y VIN); luego una sección por fuente del contenido de F07-H2 en el orden de
/// la grilla, con título centrado en mayúsculas y las tablas de <see cref="TablaFlit"/>; al final el recuadro NOTA; y en el pie
/// «Certificado de Diagnóstico» y «Página X de Y». Las secciones RUNT ampliadas (detalle del RUNT) salen de <see cref="SeccionesRuntAmpliadas"/>.
/// Pinta SOLO lo que trae <see cref="ContenidoReporteVehiculo"/> (R-20): sin origen, usuario manual, proveedor, transacción ni tipo
/// de error; una fuente en ERROR sale «consulta no disponible» y los documentos de persona ya vienen enmascarados. Función pura de
/// su entrada: F07-H5 (ZIP) la reutiliza con el contenido de <c>LectorContenidoReporte</c>. QuestPDF con licencia Community
/// declarada en el constructor estático (patrón de <c>FlitCoverPageGenerator</c> de core-api).
/// </summary>
/// <example>
/// <code>
/// var bytes = PdfVehiculo.Generar(contenido, reloj.GetUtcNow());
/// var nombre = PdfVehiculo.NombreArchivo(contenido.Encabezado); // diagnostico-0192ab34-TST301.pdf
/// </code>
/// </example>
internal static partial class PdfVehiculo
{
    /// <summary>
    /// Versión del diseño: entra en la huella (súbela al cambiar lo que se pinta o cómo). 6 = impuesto con deuda sin valor, sin deuda y
    /// «No confirmado». 7 = rótulos de negocio (punto 5, 2026-11-11): estados de las fuentes en tipo oración y «Análisis en preparación».
    /// </summary>
    public const int VersionPlantilla = 7;

    public const string ContentType = "application/pdf";

    /// <summary>Título de los metadatos del PDF (desde el diseño v2 no se pinta en la página).</summary>
    public const string Titulo = "Reporte de diagnóstico vehicular";

    /// <summary>Texto del pie de todas las páginas, junto a «Página X de Y».</summary>
    public const string Certificado = "Certificado de Diagnóstico";

    private const string NoRegistra = "No hay información";

    /// <summary>Impuesto sin vigencias registradas (HU #13864).</summary>
    public const string SinVigencias = "No hay vigencias registradas";

    /// <summary>Deuda confirmada sin monto informado. Nunca «$ 0».</summary>
    public const string DeudaSinValor = "Deuda sin valor informado";

    /// <summary>Consulta completada sin deuda.</summary>
    public const string SinDeuda = "Sin deuda";

    /// <summary>Impuesto en ERROR: no confirma deuda ni imprime un monto.</summary>
    public const string NoConfirmado = "No confirmado.";

    /// <summary>Título de la primera página.</summary>
    public const string TituloDocumento = "Diagnóstico vehicular";

    /// <summary>
    /// Títulos de sección por código de fuente (los de la grilla), sin el prefijo de la fuente («RUNT —», «SIMIT —»): el diseño v2
    /// los pinta en mayúsculas (<see cref="TituloVisible"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Titulos => RotulosDiagnostico.TitulosSeccion;

    /// <summary>Una línea, bajo el título, de qué contiene cada sección.</summary>
    private static readonly Dictionary<string, string> Descripciones = new(StringComparer.Ordinal)
    {
        ["Información del vehículo"] = "Placa, licencia de tránsito, clase, servicio y organismo de tránsito.",
        ["Información general"] = "Marca, línea, color, motor, chasis, estado, prenda y gravamen.",
        ["Datos técnicos"] = "Ejes, capacidad de carga, pasajeros, peso y dimensiones.",
        ["Datos SOAT"] = "Póliza, vigencia y aseguradora.",
        ["Datos revisión tecno-mecánica"] = "Certificado, vigencia y centro de diagnóstico.",
        ["Solicitudes"] = "Trámites registrados ante el organismo de tránsito.",
        ["Garantías a favor"] = "Prendas o gravámenes inscritos sobre el vehículo.",
        ["Accidentalidad"] = "Siniestros informados para el vehículo.",
        ["Póliza de responsabilidad civil"] = "Póliza de responsabilidad civil y su vigencia.",
        ["Limitaciones a la propiedad"] = "Embargos y otras limitaciones inscritas sobre el vehículo.",
        ["Garantías mobiliarias"] = "Garantías mobiliarias inscritas y su estado.",
        ["Infracciones del RUNT"] = "Comparendos que el RUNT entrega junto con el vehículo.",
        ["Blindaje"] = "Si el vehículo está blindado y el certificado, cuando la fuente lo informa.",
        ["Tarjeta de operación"] = "Modalidad de la tarjeta de operación, cuando aplica.",
        ["Propietarios"] = "Documento, inscripción como conductor y licencias de cada propietario.",
        ["Locatarios"] = "Documento, inscripción como conductor y licencias de cada locatario.",
        ["Comparendos del vehículo"] = "Multas y comparendos asociados a la placa.",
        ["Comparendos de los propietarios"] = "Multas y comparendos de cada propietario.",
        ["Comparendos de los locatarios"] = "Multas y comparendos de cada locatario.",
        ["Avalúo comercial"] = "Valor comercial de referencia del vehículo.",
        ["Impuesto vehicular departamental"] = "Vigencias del impuesto y si figuran pagadas.",
    };

    /// <summary>Nombre de cada fuente del avalúo (también para el Excel del lote, HU #13849); vive en <see cref="RotulosDiagnostico"/>.</summary>
    internal static IReadOnlyDictionary<string, string> FuentesAvaluo => RotulosDiagnostico.FuentesAvaluo;

    /// <summary>Alto (pt) desde el borde inferior de la hoja hasta el bloque de texto del pie, sobre la banda del membrete.</summary>
    private const float PieDesdeAbajo = 30;

    private static readonly NumberFormatInfo Pesos = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NumberGroupSizes = [3] };

    static PdfVehiculo()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary><c>diagnostico-{8 primeros del id}-{placa o VIN}.pdf</c> (solo letras y dígitos en la identificación).</summary>
    public static string NombreArchivo(EncabezadoReporte encabezado)
    {
        ArgumentNullException.ThrowIfNull(encabezado);
        var identificacion = NoAlfanumerico().Replace(encabezado.Placa ?? encabezado.Vin ?? encabezado.Identificador, string.Empty);
        return $"diagnostico-{encabezado.DiagnosticoId.ToString()[..8]}-{(identificacion.Length > 0 ? identificacion : "vehiculo")}.pdf";
    }

    /// <summary>El PDF del contenido, con la fecha de generación <paramref name="generadoEn"/> en la identificación (hora Colombia).</summary>
    public static byte[] Generar(ContenidoReporteVehiculo contenido, DateTimeOffset generadoEn) => Documento(contenido, generadoEn).GeneratePdf();

    /// <summary>El documento QuestPDF sin generar (las pruebas lo exportan además como imágenes para revisar el diseño).</summary>
    internal static IDocument Documento(ContenidoReporteVehiculo contenido, DateTimeOffset generadoEn)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        FuentesFlit.Registrar();
        var generado = generadoEn.ToOffset(VigenciaDelDia.OffsetColombia);
        return Document.Create(documento => documento.Page(page =>
            {
                FlitLetterhead.ApplyTo(page);
                page.Header().Element(FlitLetterhead.Header);
                page.Footer().Element(c => PieConNota(c, contenido.Encabezado));
                page.Content().PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre).PaddingVertical(0.4f, Unit.Centimetre).Column(col =>
                {
                    col.Spacing(14);
                    col.Item().AlignCenter().Text(TituloDocumento).Bold().FontSize(16).FontColor(FlitDocumentTheme.PrimaryBlue);
                    col.Item().Row(fila =>
                    {
                        fila.RelativeItem().AlignLeft().AlignMiddle().Element(e => e.Width(130).Height(48).Svg(BrandingAssets.LogoSvg));
                        fila.RelativeItem().AlignRight().Element(c => DatosDelDiagnostico(c, contenido.Encabezado, generado));
                    });
                    if (contenido.Dictamen is { } dictamen)
                        col.Item().Element(c => SeccionConTitulo(c, "Dictamen", null, cuerpo => CuerpoDictamen(cuerpo, dictamen)));
                    for (var i = 0; i < contenido.Secciones.Count; i++)
                    {
                        var seccion = contenido.Secciones[i];
                        var sobretitulo = i == 0 ? "Datos del vehículo" : null;
                        var detalle = seccion.Fuente == "RUNT" ? contenido.DetalleRunt : null;
                        col.Item().Element(c => Seccion(c, seccion, sobretitulo, detalle));
                        if (seccion.Fuente == "RUNT")
                            SeccionesRuntAmpliadas(col, contenido, seccion);
                    }
                });
            }))
            .WithMetadata(new DocumentMetadata
            {
                Title = Titulo,
                Author = "FLIT",
                Creator = "FLIT Diagnóstico",
                Producer = "FLIT Diagnóstico",
                CreationDate = generado,
                ModifiedDate = generado,
            });
    }

    /// <summary>
    /// Secciones RUNT ampliadas del diseño v2 (ajuste 2026-10-27), justo después de «Información del vehículo» y antes de personas y
    /// SIMIT: Información general, Datos teóricos, Datos SOAT, Datos revisión tecno-mecánica, Solicitudes (una sola vez), Garantías a
    /// favor, Accidentalidad y Póliza de responsabilidad civil, con lo que trae <see cref="ContenidoReporteVehiculo.DetalleRunt"/> y
    /// nada más (R-20). Solo con la sección RUNT COMPLETADO (si no, la sección RUNT ya dice su estado). Lista vacía o RUNT sin
    /// detalle (consultas anteriores al 2026-10-27) ⇒ la sección con su encabezado y una fila «No registra».
    /// </summary>
    private static void SeccionesRuntAmpliadas(ColumnDescriptor col, ContenidoReporteVehiculo contenido, SeccionReporte runt)
    {
        if (runt.Estado != EstadoSeccion.Completado)
            return;
        var d = contenido.DetalleRunt;

        col.Item().Element(c => SeccionConTitulo(c, "Información general", null, cuerpo =>
        {
            if (d?.General is not { } g)
            {
                cuerpo.Item().Element(x => TablaFlit.SinDatos(x, NoRegistra));
                return;
            }

            cuerpo.Item().Element(x => Tabla(x,
            [
                ("Marca", R(g.Marca)), ("Línea", R(g.Linea)), ("Cilindraje", R(g.Cilindraje)),
                ("Color", R(g.Color)), ("Tipo de combustible", R(g.TipoCombustible)), ("Tipo de carrocería", R(g.TipoCarroceria)),
                ("Puertas", R(g.Puertas)), ("Clásico antiguo", R(g.ClasicoAntiguo)), ("Repotenciado", R(g.Repotenciado)),
                ("VIN", R(g.Vin)), ("N.º motor", R(g.NumeroMotor)), ("N.º chasis", R(g.NumeroChasis)),
                ("Regrabado VIN", R(g.RegrabadoVin)), ("Regrabado motor", R(g.RegrabadoMotor)), ("Regrabado chasis", R(g.RegrabadoChasis)),
                ("Regrabado serie", R(g.RegrabadoSerie)), ("Estado del vehículo", R(g.EstadoVehiculo)),
                ("Prendas", R(g.Prendas)), ("Tiene gravámenes", R(g.TieneGravamenes)),
            ]));
        }));

        var t = d?.DatosTecnicos;
        GrillaRunt(col, "Datos técnicos", ["N.º ejes", "Capacidad de carga", "Pasajeros sentados", "Peso bruto vehicular", "Alto", "Ancho", "Largo", "Rodaje"],
            t is null ? [] : [[R(t.NumeroEjes), R(t.CapacidadCarga), R(t.PasajerosSentados), R(t.PesoBrutoVehicular), R(t.Alto), R(t.Ancho), R(t.Largo), R(t.Rodaje)]]);
        GrillaRunt(col, "Datos SOAT", ["Estado", "N.º póliza", "Vigencia", "Expedición", "Vencimiento", "Entidad"],
            [.. (d?.Soat ?? []).Select(s => new[] { Estado(s.Estado), R(s.NumeroPoliza), R(s.FechaVigencia), R(s.FechaExpedicion), R(s.FechaVencimiento), R(s.Entidad) })], [4, 4, 4, 4, 5, 6]);
        GrillaRunt(col, "Datos revisión tecno-mecánica", ["Estado", "N.º certificado", "Vigencia", "Expedición", "CDA"],
            [.. (d?.Rtm ?? []).Select(r => new[] { Estado(r.Estado), R(r.NumeroCertificado), R(r.FechaVigencia), R(r.FechaExpedicion), R(r.Cda) })], [1, 1, 1, 1, 2]);
        GrillaRunt(col, "Solicitudes", ["Estado", "Entidad", "N.º solicitud", "Fecha solicitud", "Trámites realizados"],
            [.. (d?.Solicitudes ?? []).Select(s => new[] { R(s.Estado), R(s.Entidad), R(s.NumeroSolicitud), R(s.Fecha), R(s.TramitesRealizados) })]);
        GrillaRunt(col, "Garantías a favor", ["Acreedor", "Confecámaras", "Fecha inscripción", "Tipo y N.º de documento acreedor"],
            [.. (d?.GarantiasAFavor ?? []).Select(g => new[] { R(g.Acreedor), R(g.Confecamaras), R(g.FechaInscripcion), DocumentoAcreedor(g) })], [3, 2, 2, 3]);

        var accidentalidad = d?.Accidentalidad ?? [];
        col.Item().Element(c => SeccionConTitulo(c, "Accidentalidad", null, cuerpo =>
        {
            if (accidentalidad.Count == 0)
                cuerpo.Item().Element(x => TablaFlit.SinDatos(x, NoRegistra));
            else
                cuerpo.Item().Element(x => Tabla(x, [.. accidentalidad.Select(a => (a.Etiqueta, a.Valor))]));
        }));

        GrillaRunt(col, "Póliza de responsabilidad civil", ["Estado", "N.º póliza", "Tipo póliza", "Expedición", "Vencimiento", "Entidad"],
            [.. (d?.PolizasResponsabilidadCivil ?? []).Select(p => new[] { Estado(p.Estado), R(p.NumeroPoliza), R(p.Tipo), R(p.FechaExpedicion), R(p.FechaVencimiento), R(p.Entidad) })], [4, 4, 4, 4, 5, 6]);
        GrillaRunt(col, "Limitaciones a la propiedad", ["Tipo", "N.º documento", "Entidad", "Demandante", "Expedición", "Radicación", "Departamento", "Municipio"],
            [.. (d?.Limitaciones ?? []).Select(l => new[] { R(l.Tipo), R(l.NumeroDocumento), R(l.Entidad), Demandante(l), R(l.FechaExpedicion), R(l.FechaRadicacion), R(l.Departamento), R(l.Municipio) })]);
        GrillaRunt(col, "Garantías mobiliarias", ["Entidad", "Estado", "Fecha de registro", "Tipo y N.º de documento"],
            [.. (d?.GarantiasMobiliarias ?? []).Select(g => new[] { R(g.Entidad), R(g.Estado), R(g.FechaRegistro), Documento(g.TipoDocumento, g.NumeroDocumento) })], [3, 2, 2, 3]);
        GrillaRunt(col, "Infracciones del RUNT", ["Número", "Fecha", "Código", "Estado", "Valor", "Secretaría"],
            [.. (d?.Infracciones ?? []).Select(i => new[] { R(i.Numero), R(i.Fecha), R(i.Codigo), R(i.Estado), R(i.Valor), R(i.Secretaria) })]);
        col.Item().Element(c => SeccionConTitulo(c, "Blindaje", null, cuerpo =>
        {
            if (d?.Blindaje is not { } b)
            {
                cuerpo.Item().Element(x => TablaFlit.SinDatos(x, NoRegistra));
                return;
            }

            cuerpo.Item().Element(x => Tabla(x,
            [
                ("Blindado", R(b.Blindado)), ("Nivel", R(b.Nivel)), ("Fecha", R(b.Fecha)),
                ("Resolución", R(b.Resolucion)), ("Tipo", R(b.Tipo)), ("Autorización", R(b.Autorizacion)),
            ]));
        }));
        GrillaRunt(col, "Tarjeta de operación", ["Modalidad"],
            [.. (d?.TarjetasOperacion ?? []).Select(t => new[] { R(t.Modalidad) })]);
    }

    private static string Demandante(DetalleRuntLimitacion limitacion) => Documento(limitacion.TipoDocumentoDemandante, limitacion.NumeroDocumentoDemandante);

    private static string Documento(string? tipo, string? numero)
    {
        var texto = string.Join(' ', new[] { tipo, numero }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return texto.Length == 0 ? NoRegistra : texto;
    }

    /// <summary>
    /// Sección RUNT ampliada con una grilla; sin filas, una sola fila «No registra» a todo el ancho. <paramref name="anchos"/>: ancho
    /// relativo por columna (más ancho para los nombres de entidad, CDA y documentos, que si no se parten a mitad de palabra).
    /// </summary>
    private static void GrillaRunt(
        ColumnDescriptor col, string titulo, IReadOnlyList<string> columnas, IReadOnlyList<string[]> filas, IReadOnlyList<int>? anchos = null) =>
        col.Item().Element(c => SeccionConTitulo(c, titulo, null, cuerpo => cuerpo.Item().Element(x => TablaFlit.Grilla(x, columnas, filas, NoRegistra, anchos))));

    /// <summary>
    /// «Información del vehículo» con el detalle del RUNT (diseño v2): Placa · Licencia de tránsito · Tipo de servicio / Clasificación ·
    /// Clase vehículo · Modelo / Fecha matrícula · Organismo de tránsito (a dos columnas) / Titulares · Locatarios registrados.
    /// </summary>
    private static void InformacionVehiculo(IContainer contenedor, DetalleRuntVehiculo v, ResultadoRuntDetalle runt) => TablaFlit.Campos(contenedor,
    [
        ("Placa", R(v.Placa ?? runt.Placa), 1), ("Licencia de tránsito", R(v.LicenciaTransito), 1), ("Tipo de servicio", R(v.TipoServicio ?? runt.Servicio), 1),
        ("Clasificación", R(v.Clasificacion), 1), ("Clase vehículo", R(v.Clase ?? runt.Clase), 1), ("Modelo", R(v.Modelo), 1),
        ("Fecha matrícula", R(v.FechaMatricula), 1), ("Organismo de tránsito", R(v.OrganismoTransito ?? runt.Organismo), 2),
        ("Titulares", Titulares(runt), 1), ("Locatarios registrados", runt.Locatarios.ToString(CultureInfo.InvariantCulture), 1),
    ]);

    private static string Titulares(ResultadoRuntDetalle runt) => runt.Propietarios is { Count: > 0 } titulares
        ? string.Join(", ", titulares.Select(t => $"{t.TipoDocumento ?? NoRegistra} ({t.TipoPropiedad ?? NoRegistra})"))
        : NoRegistra;

    /// <summary>
    /// Estado que entrega la fuente (SOAT, RTM, póliza, licencia, comparendo, acuerdo…) en tipo oración por
    /// <see cref="RotulosDiagnostico.DeEstadoFuente"/>: VIGENTE → «Vigente», NO VIGENTE → «No vigente», PENDIENTE → «Pendiente»; uno
    /// desconocido sale tal cual; null → «No hay información».
    /// </summary>
    internal static string Estado(string? estado) => string.IsNullOrWhiteSpace(estado) ? NoRegistra : RotulosDiagnostico.DeEstadoFuente(estado);

    /// <summary>«{Tipo} {Número}» del acreedor (el número de persona natural ya viene enmascarado); sin ninguno, «No registra».</summary>
    private static string DocumentoAcreedor(DetalleRuntGarantia g)
    {
        var texto = string.Join(' ', new[] { g.TipoDocumento, g.NumeroDocumento }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return texto.Length == 0 ? NoRegistra : texto;
    }

    /// <summary>El valor o «No hay información»; un estado conocido de la fuente (VIGENTE, PENDIENTE…) sale con su rótulo (punto 5).</summary>
    private static string R(string? valor) => Estado(valor);

    /// <summary>
    /// Pie: la banda del membrete y, encima a la derecha, una sola línea con «Certificado de Diagnóstico» en azul y «Página X de Y» en
    /// gris. Va por encima de la banda degradada del arte (en dos líneas la segunda caía sobre la banda y el gris no se leía).
    /// </summary>
    /// <summary>Nota en todas las páginas y, debajo, la banda del membrete con el certificado y el número de página.</summary>
    private static void PieConNota(IContainer contenedor, EncabezadoReporte encabezado) => contenedor.Column(col =>
    {
        col.Item().PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre).PaddingTop(4).Element(c => TablaFlit.Nota(c, TextoNota(encabezado)));
        col.Item().Element(Pie);
    });

    private static void Pie(IContainer contenedor) => contenedor.Layers(capas =>
    {
        capas.PrimaryLayer().Element(FlitLetterhead.Footer);
        capas.Layer().AlignBottom().PaddingBottom(PieDesdeAbajo).PaddingRight(FlitDocumentTheme.MarginCm, Unit.Centimetre).AlignRight().Text(t =>
        {
            t.Span(Certificado).FontSize(8).FontColor(FlitDocumentTheme.PrimaryBlue);
            t.Span("   Página ").FontSize(7).FontColor(FlitDocumentTheme.Gris);
            t.CurrentPageNumber().FontSize(7).FontColor(FlitDocumentTheme.Gris);
            t.Span(" de ").FontSize(7).FontColor(FlitDocumentTheme.Gris);
            t.TotalPages().FontSize(7).FontColor(FlitDocumentTheme.Gris);
        });
    });

    /// <summary>
    /// Cuerpo de la sección Dictamen, después de los pares del diagnóstico: la palabra del veredicto, el aviso si el estado es
    /// exactamente <c>PENDIENTE</c>, y los párrafos de <see cref="DictamenEnHuella.Texto"/> en orden. Si un párrafo es igual al
    /// anterior, no se repite. No pinta código, efecto, fuente ni placa.
    /// </summary>
    private static void CuerpoDictamen(ColumnDescriptor cuerpo, DictamenEnHuella dictamen)
    {
        cuerpo.Item().Text(VeredictoEnPalabra(dictamen.Veredicto));
        if (dictamen.Estado == "PENDIENTE")
            cuerpo.Item().Text(RotulosDiagnostico.AnalisisEnPreparacion);

        string? anterior = null;
        foreach (var parrafo in dictamen.Texto.Split('\n'))
        {
            if (parrafo == anterior)
                continue;
            anterior = parrafo;
            cuerpo.Item().Text(parrafo);
        }
    }

    /// <summary>El veredicto se lee en palabra. El color no lo sustituye.</summary>
    private static string VeredictoEnPalabra(string veredicto) => RotulosDiagnostico.DeVeredicto(veredicto);

    /// <summary>Tabla de la cabecera, al 50 % del ancho: cada dato en una fila, etiqueta a la izquierda y valor a la derecha.</summary>
    private static void DatosDelDiagnostico(IContainer contenedor, EncabezadoReporte e, DateTimeOffset generado) =>
        TablaFlit.Pares(contenedor,
        [
            ("Diagnóstico ID", e.DiagnosticoId.ToString()),
            ("Placa", e.Placa ?? NoRegistra),
            ("Fecha de generación", generado.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)),
            ("Nombre del lote", e.NombreLote),
            ("Tipo de análisis", e.TipoAnalisis),
            ("Fecha y hora de creación", Fecha(e.CreadoEn)),
            ("Empresa", e.Empresa),
            ("Creado por", e.UsuarioCreador),
            ("VIN", e.Vin ?? NoRegistra),
        ]);

    /// <summary>Texto del recuadro NOTA, repetido al pie de cada página.</summary>
    internal static string TextoNota(EncabezadoReporte e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var vehiculo = e.Placa is { } placa ? $"la placa {placa}" : $"el VIN {e.Vin ?? e.Identificador}";
        return $"Este informe se elaboró a solicitud de {e.UsuarioCreador}, de {e.Empresa}, para {vehiculo}. "
            + "Los datos provienen de organismos públicos y corresponden a lo que respondieron en la fecha de generación. "
            + "Una consulta sin resultado no significa que el dato no exista: significa que en esa fecha el organismo no lo entregó.";
    }

    /// <summary>
    /// El título de sección como se pinta: en mayúsculas. Invariante a propósito: el servicio corre con globalización invariante (no
    /// hay culturas como es-CO) y desde .NET 8 ese modo igual pasa a mayúscula las vocales tildadas y la ñ.
    /// </summary>
    internal static string TituloVisible(string titulo) => titulo.ToUpperInvariant();

    /// <summary>
    /// Bloque de sección: sobretítulo opcional centrado en azul, título centrado en mayúsculas Poppins Bold azul oscuro, «Consultado
    /// el …» pequeño gris si hay fecha del dato, y el cuerpo. Pide espacio al inicio para no dejar el título huérfano al pie.
    /// </summary>
    private static void SeccionConTitulo(IContainer contenedor, string titulo, DateTimeOffset? dato, Action<ColumnDescriptor> cuerpo, string? sobretitulo = null) =>
        contenedor.EnsureSpace(90).Column(col =>
        {
            col.Spacing(5);
            if (sobretitulo is not null)
                col.Item().AlignCenter().Text(TituloVisible(sobretitulo)).Bold().FontSize(sobretitulo == "Datos del vehículo" ? 15 : 11)
                    .FontColor(FlitDocumentTheme.PrimaryBlue);
            col.Item().AlignCenter().Text(TituloVisible(titulo)).Bold().FontSize(11).FontColor(FlitDocumentTheme.DarkNavy);
            if (Descripciones.TryGetValue(titulo, out var descripcion))
                col.Item().AlignCenter().Text(descripcion).FontSize(8).FontColor(FlitDocumentTheme.Gris).Italic();
            if (dato is { } d)
                col.Item().AlignCenter().Text($"Consultado el {Fecha(d)} (hora Colombia)").FontSize(7.5f).FontColor(FlitDocumentTheme.Gris);
            cuerpo(col);
        });

    /// <summary>Texto gris centrado de los estados sin dato (No aplica, consulta no disponible, pendiente, sin resultados).</summary>
    private static void Aviso(IContainer contenedor, string texto) =>
        contenedor.AlignCenter().Text(texto).FontSize(TablaFlit.TamanoTexto).FontColor(FlitDocumentTheme.Gris);

    private static void Seccion(IContainer contenedor, SeccionReporte seccion, string? sobretitulo, DetalleRuntReporte? detalle) => SeccionConTitulo(contenedor,
        RotulosDiagnostico.DeTituloSeccion(seccion.Fuente), seccion.DatoObtenidoEn, col =>
        {
            switch (seccion.Estado)
            {
                case EstadoSeccion.Completado when seccion.Resultado is ResultadoRuntDetalle runt && detalle is not null:
                    col.Item().Element(c => InformacionVehiculo(c, detalle.Vehiculo, runt));
                    break;
                case EstadoSeccion.Completado when seccion.Resultado is { } resultado:
                    col.Item().Element(c => Resultado(c, resultado));
                    break;
                case EstadoSeccion.NoAplica:
                    col.Item().Element(c => Aviso(c, "No aplica. " + (seccion.Leyenda ?? string.Empty)));
                    break;
                case EstadoSeccion.NoDisponible:
                    col.Item().Element(c => Aviso(c, seccion.Fuente == "IMPUESTO_DEPARTAMENTAL" ? NoConfirmado : "No pudimos obtener esta información."));
                    break;
                case EstadoSeccion.Pendiente:
                    col.Item().Element(c => Aviso(c, "Esta consulta todavía está en curso."));
                    break;
            }

            if (seccion.Personas is { Count: > 0 } personas)
            {
                foreach (var persona in personas)
                    col.Item().PaddingTop(4).Element(c => Persona(c, persona));
            }
            else if (seccion.Personas is not null && seccion.Estado == EstadoSeccion.Completado)
            {
                col.Item().Element(c => Aviso(c, "No hay personas registradas para esta consulta."));
            }
        }, sobretitulo);

    /// <summary>Una persona de una sección de persona: subtítulo «Propietario n» / «Locatario n» en azul, su tabla y su resultado.</summary>
    private static void Persona(IContainer contenedor, PersonaReporte persona) => contenedor.Column(col =>
    {
        col.Spacing(5);
        var rol = persona.Rol switch { "PROPIETARIO" => "Propietario", "LOCATARIO" => "Locatario", _ => persona.Rol };
        col.Item().AlignCenter().Text($"{rol} {persona.Orden.ToString(CultureInfo.InvariantCulture)}").FontFamily(FlitDocumentTheme.FontMedium)
            .FontSize(10).FontColor(FlitDocumentTheme.PrimaryBlue);
        var numero = string.IsNullOrWhiteSpace(persona.Documento) ? persona.DocumentoEnmascarado : persona.Documento;
        var filas = new List<(string, string)> { ("Documento", $"{persona.TipoDocumento} {numero}") };
        if (persona.RotuloDeclarado is { } rotulo)
            filas.Add(("Declaración", rotulo));
        if (persona.ConsultadoEn is { } consultado)
            filas.Add(("Consultado el", Fecha(consultado)));
        col.Item().Element(c => Tabla(c, filas));
        switch (persona.Estado)
        {
            case EstadoPersonaReporte.Ok when persona.Resultado is { } resultado:
                col.Item().Element(c => Resultado(c, resultado));
                break;
            case EstadoPersonaReporte.NoDisponible:
                col.Item().Element(c => Aviso(c, "No pudimos obtener esta información."));
                break;
            default:
                col.Item().Element(c => Aviso(c, "No hay un resultado para esta persona."));
                break;
        }
    });

    private static void Resultado(IContainer contenedor, ResultadoDetalleDto resultado)
    {
        switch (resultado)
        {
            case ResultadoRuntDetalle runt:
                Tabla(contenedor,
                [
                    ("Placa", runt.Placa ?? NoRegistra),
                    ("VIN", runt.Vin ?? NoRegistra),
                    ("Servicio", runt.Servicio ?? NoRegistra),
                    ("Clase", runt.Clase ?? NoRegistra),
                    ("Organismo de tránsito", runt.Organismo ?? NoRegistra),
                    ("Titulares", Titulares(runt)),
                    ("Locatarios registrados", runt.Locatarios.ToString(CultureInfo.InvariantCulture)),
                ]);
                break;
            case ResultadoSimitDetalle simit:
                contenedor.Element(c => Simit(c, simit));
                break;
            case ResultadoAvaluoDetalle avaluo:
                contenedor.Element(c => Avaluo(c, avaluo));
                break;
            case ResultadoImpuestoDetalle impuesto:
                contenedor.Element(c => Impuesto(c, impuesto));
                break;
            case ResultadoRuntPersonaDetalle persona:
                contenedor.Column(col =>
                {
                    col.Spacing(5);
                    col.Item().Element(c => Tabla(c,
                    [
                        ("Inscrito en el RUNT", persona.Inscrito ? "Sí" : "No"),
                        ("Estado como conductor", Estado(persona.EstadoConductor)),
                    ]));
                    if (persona.Licencias is { Count: > 0 } licencias)
                    {
                        col.Item().Element(c => Grilla(c, ["Número", "Categoría", "Estado", "Vencimiento", "Expedición", "Organismo", "Restricciones", "Suspensión"],
                            [.. licencias.Select(l => new[]
                            {
                                l.Numero ?? NoRegistra, l.Categoria ?? NoRegistra, Estado(l.Estado), l.VigenteHasta ?? NoRegistra,
                                l.Expedida ?? NoRegistra, l.Organismo ?? NoRegistra, l.Restricciones ?? NoRegistra,
                                Suspension(l),
                            })]));
                        if (persona.FechaInscripcion is not null || persona.TieneMultas is not null || persona.PazYSalvo is not null)
                            col.Item().Element(c => Tabla(c,
                            [
                                ("Fecha de inscripción", persona.FechaInscripcion ?? NoRegistra),
                                ("Tiene multas", persona.TieneMultas ?? NoRegistra),
                                ("Paz y salvo", persona.PazYSalvo ?? NoRegistra),
                            ]));
                        if (persona.Certificados is { Count: > 0 } certificados)
                            col.Item().Element(c => Grilla(c, ["Categoría", "Estado", "Expedición", "Vencimiento", "Tipo", "Número", "Entidad"],
                                [.. certificados.Select(x => new[] { x.Categoria ?? NoRegistra, Estado(x.Estado), x.Expedida ?? NoRegistra, x.Vence ?? NoRegistra, x.Tipo ?? NoRegistra, x.Numero ?? NoRegistra, x.Entidad ?? NoRegistra })]));
                        if (persona.Solicitudes is { Count: > 0 } solicitudes)
                            col.Item().Element(c => Grilla(c, ["Trámite", "Estado solicitud", "Estado trámite", "Fecha", "Entidad", "Registro"],
                                [.. solicitudes.Select(s => new[] { s.Tramite ?? NoRegistra, Estado(s.EstadoSolicitud), Estado(s.EstadoTramite), s.Fecha ?? NoRegistra, s.Entidad ?? NoRegistra, s.Registro ?? NoRegistra })]));
                    }
                    else
                    {
                        col.Item().Element(c => Aviso(c, "No hay licencias de conducción registradas."));
                    }
                });
                break;
            case ResultadoRuesDetalle rues:
                Tabla(contenedor,
                [
                    ("Encontrada en el RUES", rues.Encontrada ? "Sí" : "No"),
                    ("Estado de la matrícula", Estado(rues.EstadoMatricula)),
                    ("Última renovación", rues.UltimaRenovacion ?? NoRegistra),
                ]);
                break;
        }
    }

    private static void Simit(IContainer contenedor, ResultadoSimitDetalle simit) => contenedor.Column(col =>
    {
        col.Spacing(5);
        col.Item().Element(c => Tabla(c,
        [
            ("Semáforo", simit.Semaforo ?? NoRegistra),
            ("Comparendos", simit.CantidadComparendos.ToString(CultureInfo.InvariantCulture)),
            ("Total general", simit.TotalGeneral ?? NoRegistra),
            ("Total por pagar", simit.TotalPorPagar ?? NoRegistra),
            ("Paz y salvo", simit.PazYSalvo is null ? NoRegistra : simit.PazYSalvo.Value ? "Sí" : "No"),
            ("Suspendida", simit.Suspendida is null ? NoRegistra : simit.Suspendida.Value ? "Sí" : "No"),
            ("Cancelada", simit.Cancelada is null ? NoRegistra : simit.Cancelada.Value ? "Sí" : "No"),
        ]));
        var comparendos = simit.Comparendos ?? [];
        if (comparendos.Count == 0)
            col.Item().Element(c => Aviso(c, "No hay comparendos registrados."));
        else
        {
            col.Item().Element(c => Grilla(c, ["Número", "Fecha", "Valor", "Estado", "Organismo"],
                [.. comparendos.Select(k => new[]
                {
                    k.Numero ?? NoRegistra, k.Fecha ?? NoRegistra, ValorTexto(k.Valor), Estado(k.Estado), k.Organismo ?? NoRegistra,
                })]));
            var restantes = simit.CantidadComparendos - comparendos.Count;
            if (simit.ComparendosTruncados || restantes > 0)
                col.Item().Element(c => Aviso(c, $"y {Math.Max(restantes, 0).ToString(CultureInfo.InvariantCulture)} más"));
        }

        if (simit.Multas is { Count: > 0 } multas)
            col.Item().Element(c => Grilla(c, ["Número", "Fecha", "Valor", "Valor a pagar", "Estado de cartera", "Organismo", "Códigos"],
                [.. multas.Select(m => new[] { m.Numero ?? NoRegistra, m.Fecha ?? NoRegistra, m.Valor ?? NoRegistra, m.ValorPagar ?? NoRegistra, Estado(m.EstadoCartera), m.Organismo ?? NoRegistra, m.Codigos ?? NoRegistra })]));
        if (simit.Acuerdos is { Count: > 0 } acuerdos)
            col.Item().Element(c => Grilla(c, ["Estado", "Cuotas abonadas", "Cuotas pendientes", "Pendiente", "Fecha", "Organismo"],
                [.. acuerdos.Select(a => new[] { Estado(a.Estado), a.CuotasAbonadas ?? NoRegistra, a.CuotasPendientes ?? NoRegistra, a.Pendiente ?? NoRegistra, a.Fecha ?? NoRegistra, a.Organismo ?? NoRegistra })]));
    });

    private static string Suspension(LicenciaDetalle licencia)
    {
        if (licencia.SuspensionDesde is null && licencia.SuspensionHasta is null)
            return NoRegistra;
        return $"{licencia.SuspensionDesde ?? "—"} – {licencia.SuspensionHasta ?? "—"}";
    }

    private static void Avaluo(IContainer contenedor, ResultadoAvaluoDetalle avaluo) => contenedor.Column(col =>
    {
        col.Spacing(5);
        if (avaluo.SinValorComercial)
        {
            col.Item().Element(c => Aviso(c, "No hay un valor comercial de referencia."));
        }
        else
        {
            col.Item().Element(c => Tabla(c,
            [
                ("Valor sugerido", avaluo.ValorSugerido is { } valor ? Moneda(valor) : NoRegistra),
                ("Fuente principal", NombreFuenteAvaluo(avaluo.FuentePrincipal)),
            ]));
        }

        if (avaluo.Fuentes is { Count: > 0 } fuentes)
        {
            col.Item().Element(c => Grilla(c, ["Fuente", "Estado", "Valor", "Muestras", "Fecha"],
                [.. fuentes.Select(f => new[]
                {
                    NombreFuenteAvaluo(f.Fuente), Estado(f.Estado), f.Valor is { } v ? Moneda(v) : NoRegistra,
                    f.Muestras?.ToString(CultureInfo.InvariantCulture) ?? NoRegistra, f.Fecha ?? NoRegistra,
                })]));
        }
    });

    /// <summary>
    /// Impuesto departamental: total adeudado, o «Deuda sin valor informado» / «Sin deuda» cuando no hay un monto que afirmar.
    /// Fechas y valores opcionales solo si vinieron. Sin vigencias, «No hay vigencias registradas».
    /// </summary>
    private static void Impuesto(IContainer contenedor, ResultadoImpuestoDetalle impuesto) => contenedor.Column(col =>
    {
        col.Spacing(5);
        col.Item().Element(c => Tabla(c, FilasImpuesto(impuesto)));
        if (impuesto.Vigencias is { Count: > 0 } vigencias)
        {
            col.Item().Element(c => Grilla(c, ["Vigencia", "Valor", "Estado"],
                [.. vigencias.Select(v => new[] { v.Anio.ToString(CultureInfo.InvariantCulture), Moneda(v.Valor), v.Pagada ? "Pagada" : "Pendiente" })]));
        }
        else
        {
            col.Item().Element(c => Aviso(c, SinVigencias));
        }
    });

    private static string TextoTotal(ResultadoImpuestoDetalle impuesto) =>
        impuesto.TieneDeuda && impuesto.MontoTotal is null or 0 ? DeudaSinValor
        : impuesto.MontoTotal is > 0 ? Moneda(impuesto.MontoTotal.Value)
        : SinDeuda;

    private static List<(string Etiqueta, string Valor)> FilasImpuesto(ResultadoImpuestoDetalle impuesto)
    {
        var filas = new List<(string Etiqueta, string Valor)> { ("Total adeudado", TextoTotal(impuesto)) };
        if (impuesto.InteresesMora is > 0)
            filas.Add(("Intereses de mora", Moneda(impuesto.InteresesMora.Value)));
        if (impuesto.Sanciones is > 0)
            filas.Add(("Sanciones", Moneda(impuesto.Sanciones.Value)));
        if (impuesto.AvaluoComercial is > 0)
            filas.Add(("Avalúo comercial", Moneda(impuesto.AvaluoComercial.Value)));
        if (impuesto.UltimoPago is { } pago)
        {
            if (pago.Valor is > 0)
                filas.Add(("Último pago", Moneda(pago.Valor.Value)));
            if (!string.IsNullOrWhiteSpace(pago.Fecha))
                filas.Add(("Fecha del último pago", pago.Fecha));
            if (pago.Anio is > 0)
                filas.Add(("Año del último pago", pago.Anio.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(impuesto.PeriodoCertificacion))
            filas.Add(("Periodo de certificación", impuesto.PeriodoCertificacion));
        if (!string.IsNullOrWhiteSpace(impuesto.FechaConsulta))
            filas.Add(("Fecha de consulta", impuesto.FechaConsulta));
        return filas;
    }

    /// <summary>Tabla etiqueta-valor: bloques de 3 columnas, fila de etiquetas y debajo la de valores (<see cref="TablaFlit.Campos"/>).</summary>
    private static void Tabla(IContainer contenedor, IReadOnlyList<(string Etiqueta, string Valor)> filas) => TablaFlit.Campos(contenedor, filas);

    /// <summary>Tabla con encabezado de columnas y una fila por elemento (<see cref="TablaFlit.Grilla"/>).</summary>
    private static void Grilla(IContainer contenedor, IReadOnlyList<string> columnas, IReadOnlyList<string[]> filas) =>
        TablaFlit.Grilla(contenedor, columnas, filas);

    /// <summary>Fecha y hora (sin segundos) en hora Colombia: dd/MM/yyyy HH:mm.</summary>
    private static string Fecha(DateTimeOffset instante) =>
        instante.ToOffset(VigenciaDelDia.OffsetColombia).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Moneda(long valor) => "$ " + valor.ToString("#,0", Pesos);

    private static string ValorTexto(string? valor) =>
        decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var pesos)
            ? "$ " + pesos.ToString("#,0.##", Pesos)
            : valor ?? NoRegistra;

    private static string NombreFuenteAvaluo(string? fuente) =>
        fuente is null ? NoRegistra : RotulosDiagnostico.DeFuenteAvaluo(fuente);

    [GeneratedRegex("[^A-Za-z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex NoAlfanumerico();
}

/// <summary>
/// Descarga del PDF de un vehículo para la compañía (HU #13848): visibilidad de <see cref="AccesoLote"/> (404 si el lote no se ve o
/// el vehículo no es de ese lote), regla única de emisión <see cref="EmisionPdf"/> (409 <c>VEHICULO_NO_TERMINADO</c>), contenido de
/// <see cref="LectorContenidoReporte"/> con la empresa del claim <c>company_name</c>, huella del vehículo
/// (<see cref="HuellaEvidencia"/> con <see cref="PdfVehiculo.VersionPlantilla"/>) y registro de <see cref="ServicioEvidencias"/>: con
/// la misma huella entrega los bytes guardados sin regenerar; si cambió una celda, genera uno nuevo e invalida el anterior.
/// </summary>
internal sealed class DescargaPdfVehiculo(
    DiagnosticoDb db, UsuarioActual usuario, LectorContenidoReporte lector, ServicioEvidencias evidencias, TimeProvider reloj)
{
    public async Task<ArchivoGenerado> ObtenerAsync(Guid diagnosticoId, Guid vehiculoId, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, diagnosticoId, seguir: false, ct).ConfigureAwait(false);
        var estados = await db.Set<CeldaConsulta>().AsNoTracking()
            .Where(c => c.DiagnosticoId == cabecera.Id && c.VehiculoId == vehiculoId)
            .Select(c => c.Estado)
            .ToListAsync(ct).ConfigureAwait(false);
        if (!EmisionPdf.Puede(cabecera, estados))
        {
            // Un vehículo de otro lote (u otra compañía) no tiene celdas en este: 404, no 409.
            if (!await db.Set<VehiculoDiagnostico>().AnyAsync(v => v.Id == vehiculoId && v.DiagnosticoId == cabecera.Id, ct).ConfigureAwait(false))
                throw ReglaException.NoEncontrado("El vehículo");
            EmisionPdf.Exigir(cabecera, estados);
        }

        var contenido = await lector.VehiculoAsync(cabecera, vehiculoId, usuario.EmpresaNombre, ct).ConfigureAwait(false);
        var clave = ClaveEvidencia.Pdf(cabecera.Id, vehiculoId);
        var entrada = await HuellaEvidencia.LeerEntradaAsync(db, clave, PdfVehiculo.VersionPlantilla, contenido.ParaHuella(), ct).ConfigureAwait(false);
        var nombre = PdfVehiculo.NombreArchivo(contenido.Encabezado);
        var entregada = await evidencias.ObtenerOGenerarAsync(
            clave, HuellaEvidencia.Calcular(entrada), usuario.Id,
            _ => Task.FromResult(new ArchivoGenerado(PdfVehiculo.Generar(contenido, reloj.GetUtcNow()), nombre, PdfVehiculo.ContentType)),
            ct).ConfigureAwait(false);
        return new ArchivoGenerado(entregada.Contenido, nombre, PdfVehiculo.ContentType);
    }
}
