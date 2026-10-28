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
    /// <summary>Versión del diseño: entra en la huella (súbela al cambiar lo que se pinta o cómo). 2 = diseño v2 (2026-10-27).</summary>
    public const int VersionPlantilla = 2;

    public const string ContentType = "application/pdf";

    /// <summary>Título de los metadatos del PDF (desde el diseño v2 no se pinta en la página).</summary>
    public const string Titulo = "Reporte de diagnóstico vehicular";

    /// <summary>Texto del pie de todas las páginas, junto a «Página X de Y».</summary>
    public const string Certificado = "Certificado de Diagnóstico";

    private const string NoRegistra = "No registra";

    /// <summary>Impuesto sin vigencias registradas (HU #13864).</summary>
    public const string SinVigencias = "No registra vigencias";

    /// <summary>
    /// Títulos de sección por código de fuente (los de la grilla), sin el prefijo de la fuente («RUNT —», «SIMIT —»): el diseño v2
    /// los pinta en mayúsculas (<see cref="TituloVisible"/>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Titulos = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RUNT"] = "Información del vehículo",
        ["RUNT_PROPIETARIO"] = "Propietarios",
        ["RUNT_LOCATARIO"] = "Locatarios",
        ["SIMIT_VEHICULO"] = "Comparendos del vehículo",
        ["SIMIT_PROPIETARIO"] = "Comparendos de los propietarios",
        ["SIMIT_LOCATARIO"] = "Comparendos de los locatarios",
        ["AVALUO"] = "Avalúo comercial",
        ["IMPUESTO_DEPARTAMENTAL"] = "Impuesto vehicular departamental",
    };

    /// <summary>Nombre de cada fuente del avalúo (también para el Excel del lote, HU #13849).</summary>
    internal static readonly IReadOnlyDictionary<string, string> FuentesAvaluo = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fasecolda"] = "Fasecolda",
        ["mercado_libre"] = "Mercado Libre",
        ["base_gravable"] = "Base gravable",
    };

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
                page.Footer().Element(Pie);
                page.Content().PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre).PaddingVertical(0.4f, Unit.Centimetre).Column(col =>
                {
                    col.Spacing(14);
                    col.Item().Element(c => Identificacion(c, contenido.Encabezado, generado));
                    col.Item().Element(c => DatosDelDiagnostico(c, contenido.Encabezado));
                    for (var i = 0; i < contenido.Secciones.Count; i++)
                    {
                        var seccion = contenido.Secciones[i];
                        var sobretitulo = i == 0 ? "Datos del vehículo" : null;
                        var detalle = seccion.Fuente == "RUNT" ? contenido.DetalleRunt : null;
                        col.Item().Element(c => Seccion(c, seccion, sobretitulo, detalle));
                        if (seccion.Fuente == "RUNT")
                            SeccionesRuntAmpliadas(col, contenido, seccion);
                    }

                    col.Item().PaddingTop(4).Element(c => TablaFlit.Nota(c, TextoNota(contenido.Encabezado)));
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
            ]));
        }));

        var t = d?.DatosTecnicos;
        GrillaRunt(col, "Datos teóricos", ["N.º ejes", "Capacidad de carga", "Pasajeros sentados", "Peso bruto vehicular"],
            t is null ? [] : [[R(t.NumeroEjes), R(t.CapacidadCarga), R(t.PasajerosSentados), R(t.PesoBrutoVehicular)]]);
        GrillaRunt(col, "Datos SOAT", ["Estado", "N.º póliza", "Vigencia", "Expedición", "Vencimiento", "Entidad"],
            [.. (d?.Soat ?? []).Select(s => new[] { Estado(s.Estado), R(s.NumeroPoliza), R(s.FechaVigencia), R(s.FechaExpedicion), R(s.FechaVencimiento), R(s.Entidad) })]);
        GrillaRunt(col, "Datos revisión tecno-mecánica", ["Estado", "N.º certificado", "Vigencia", "Expedición", "CDA"],
            [.. (d?.Rtm ?? []).Select(r => new[] { Estado(r.Estado), R(r.NumeroCertificado), R(r.FechaVigencia), R(r.FechaExpedicion), R(r.Cda) })]);
        GrillaRunt(col, "Solicitudes", ["Estado", "Entidad", "N.º solicitud", "Fecha solicitud", "Trámites realizados"],
            [.. (d?.Solicitudes ?? []).Select(s => new[] { R(s.Estado), R(s.Entidad), R(s.NumeroSolicitud), R(s.Fecha), R(s.TramitesRealizados) })]);
        GrillaRunt(col, "Garantías a favor", ["Acreedor", "Confecámaras", "Fecha inscripción", "Tipo y N.º de documento acreedor"],
            [.. (d?.GarantiasAFavor ?? []).Select(g => new[] { R(g.Acreedor), R(g.Confecamaras), R(g.FechaInscripcion), DocumentoAcreedor(g) })]);

        var accidentalidad = d?.Accidentalidad ?? [];
        col.Item().Element(c => SeccionConTitulo(c, "Accidentalidad", null, cuerpo =>
        {
            if (accidentalidad.Count == 0)
                cuerpo.Item().Element(x => TablaFlit.SinDatos(x, NoRegistra));
            else
                cuerpo.Item().Element(x => Tabla(x, [.. accidentalidad.Select(a => (a.Etiqueta, a.Valor))]));
        }));

        GrillaRunt(col, "Póliza de responsabilidad civil", ["Estado", "N.º póliza", "Tipo póliza", "Expedición", "Vencimiento", "Entidad"],
            [.. (d?.PolizasResponsabilidadCivil ?? []).Select(p => new[] { Estado(p.Estado), R(p.NumeroPoliza), R(p.Tipo), R(p.FechaExpedicion), R(p.FechaVencimiento), R(p.Entidad) })]);
    }

    /// <summary>Sección RUNT ampliada con una grilla; sin filas, una sola fila «No registra» a todo el ancho.</summary>
    private static void GrillaRunt(ColumnDescriptor col, string titulo, IReadOnlyList<string> columnas, IReadOnlyList<string[]> filas) =>
        col.Item().Element(c => SeccionConTitulo(c, titulo, null, cuerpo => cuerpo.Item().Element(x => TablaFlit.Grilla(x, columnas, filas, NoRegistra))));

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

    /// <summary>Estado de SOAT/RTM/póliza en tipo oración: VIGENTE → «Vigente», NO VIGENTE → «No vigente», NO REQUIERE → «No requiere»; null → «No registra».</summary>
    internal static string Estado(string? estado) => string.IsNullOrWhiteSpace(estado)
        ? NoRegistra
        : estado.Trim().ToUpperInvariant() switch
        {
            "VIGENTE" => "Vigente",
            "NO VIGENTE" => "No vigente",
            "NO REQUIERE" => "No requiere",
            _ => estado.Trim(),
        };

    /// <summary>«{Tipo} {Número}» del acreedor (el número de persona natural ya viene enmascarado); sin ninguno, «No registra».</summary>
    private static string DocumentoAcreedor(DetalleRuntGarantia g)
    {
        var texto = string.Join(' ', new[] { g.TipoDocumento, g.NumeroDocumento }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return texto.Length == 0 ? NoRegistra : texto;
    }

    /// <summary>El valor o «No registra».</summary>
    private static string R(string? valor) => string.IsNullOrWhiteSpace(valor) ? NoRegistra : valor;

    /// <summary>
    /// Pie: la banda del membrete y, encima a la derecha, una sola línea con «Certificado de Diagnóstico» en azul y «Página X de Y» en
    /// gris. Va por encima de la banda degradada del arte (en dos líneas la segunda caía sobre la banda y el gris no se leía).
    /// </summary>
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

    /// <summary>Identificación (solo página 1): Diagnóstico ID, Placa (o VIN si no hay placa) y fecha de generación con segundos.</summary>
    private static void Identificacion(IContainer contenedor, EncabezadoReporte e, DateTimeOffset generado) => contenedor.Column(col =>
    {
        col.Spacing(1);
        col.Item().Element(c => Linea(c, "Diagnóstico ID", e.DiagnosticoId.ToString()));
        col.Item().Element(c => Linea(c, e.Placa is null ? "VIN" : "Placa", e.Placa ?? e.Vin ?? e.Identificador));
        col.Item().Element(c => Linea(c, "Fecha de generación", generado.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)));
    });

    /// <summary>Una línea «Etiqueta: valor» de la identificación: etiqueta azul oscuro regular, valor azul Poppins Bold, 11 pt.</summary>
    private static void Linea(IContainer contenedor, string etiqueta, string valor) => contenedor.Text(t =>
    {
        t.DefaultTextStyle(s => s.FontSize(11));
        t.Span(etiqueta + ": ").FontColor(FlitDocumentTheme.DarkNavy);
        t.Span(valor).Bold().FontColor(FlitDocumentTheme.PrimaryBlue);
    });

    /// <summary>Lo del encabezado que no está en la identificación (AC de la HU #13848): lote, tipo, creación, empresa, creador y VIN.</summary>
    private static void DatosDelDiagnostico(IContainer contenedor, EncabezadoReporte e) => SeccionConTitulo(contenedor, "Datos del diagnóstico", null,
        col => col.Item().Element(c => Tabla(c,
        [
            ("Nombre del lote", e.NombreLote),
            ("Tipo de análisis", e.TipoAnalisis),
            ("Fecha y hora de creación", Fecha(e.CreadoEn)),
            ("Empresa", e.Empresa),
            ("Creado por", e.UsuarioCreador),
            ("VIN", e.Vin ?? NoRegistra),
        ])));

    /// <summary>Texto del recuadro NOTA con la placa (o el VIN), el creador y la empresa del encabezado.</summary>
    internal static string TextoNota(EncabezadoReporte e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var vehiculo = e.Placa is { } placa ? $"a la placa {placa}" : $"al VIN {e.Vin ?? e.Identificador}";
        return $"La información que se encuentra en este documento asociada {vehiculo} se genera por solicitud de {e.UsuarioCreador} en "
            + $"nombre de la compañía {e.Empresa}. Toda la información suministrada y relacionada en el presente documento se realiza sobre "
            + "fuentes públicas y estatales; el resultado de estas consultas depende netamente de la respuesta otorgada por los organismos "
            + "consultados en la fecha de generación.";
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
                col.Item().AlignCenter().Text(TituloVisible(sobretitulo)).Bold().FontSize(11).FontColor(FlitDocumentTheme.PrimaryBlue);
            col.Item().AlignCenter().Text(TituloVisible(titulo)).Bold().FontSize(11).FontColor(FlitDocumentTheme.DarkNavy);
            if (dato is { } d)
                col.Item().AlignCenter().Text($"Consultado el {Fecha(d)} (hora Colombia)").FontSize(7.5f).FontColor(FlitDocumentTheme.Gris);
            cuerpo(col);
        });

    /// <summary>Texto gris centrado de los estados sin dato (No aplica, consulta no disponible, pendiente, sin resultados).</summary>
    private static void Aviso(IContainer contenedor, string texto) =>
        contenedor.AlignCenter().Text(texto).FontSize(TablaFlit.TamanoTexto).FontColor(FlitDocumentTheme.Gris);

    private static void Seccion(IContainer contenedor, SeccionReporte seccion, string? sobretitulo, DetalleRuntReporte? detalle) => SeccionConTitulo(contenedor,
        Titulos.GetValueOrDefault(seccion.Fuente, seccion.Fuente), seccion.DatoObtenidoEn, col =>
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
                    col.Item().Element(c => Aviso(c, SeccionReporte.ConsultaNoDisponible));
                    break;
                case EstadoSeccion.Pendiente:
                    col.Item().Element(c => Aviso(c, "Pendiente: la consulta aún no termina."));
                    break;
            }

            if (seccion.Personas is { Count: > 0 } personas)
            {
                foreach (var persona in personas)
                    col.Item().PaddingTop(4).Element(c => Persona(c, persona));
            }
            else if (seccion.Personas is not null && seccion.Estado == EstadoSeccion.Completado)
            {
                col.Item().Element(c => Aviso(c, "Sin personas para consultar."));
            }
        }, sobretitulo);

    /// <summary>Una persona de una sección de persona: subtítulo «Propietario n» / «Locatario n» en azul, su tabla y su resultado.</summary>
    private static void Persona(IContainer contenedor, PersonaReporte persona) => contenedor.Column(col =>
    {
        col.Spacing(5);
        var rol = persona.Rol switch { "PROPIETARIO" => "Propietario", "LOCATARIO" => "Locatario", _ => persona.Rol };
        col.Item().AlignCenter().Text($"{rol} {persona.Orden.ToString(CultureInfo.InvariantCulture)}").FontFamily(FlitDocumentTheme.FontMedium)
            .FontSize(10).FontColor(FlitDocumentTheme.PrimaryBlue);
        var filas = new List<(string, string)> { ("Documento", $"{persona.TipoDocumento} {persona.DocumentoEnmascarado}") };
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
                col.Item().Element(c => Aviso(c, SeccionReporte.ConsultaNoDisponible));
                break;
            default:
                col.Item().Element(c => Aviso(c, "Sin resultado."));
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
                        ("Estado como conductor", persona.EstadoConductor ?? NoRegistra),
                    ]));
                    if (persona.Licencias is { Count: > 0 } licencias)
                    {
                        col.Item().Element(c => Grilla(c, ["Categoría", "Estado", "Vigente hasta"],
                            [.. licencias.Select(l => new[] { l.Categoria ?? NoRegistra, l.Estado ?? NoRegistra, l.VigenteHasta ?? NoRegistra })]));
                    }
                    else
                    {
                        col.Item().Element(c => Aviso(c, "Sin licencias de conducción."));
                    }
                });
                break;
            case ResultadoRuesDetalle rues:
                Tabla(contenedor,
                [
                    ("Encontrada en el RUES", rues.Encontrada ? "Sí" : "No"),
                    ("Estado de la matrícula", rues.EstadoMatricula ?? NoRegistra),
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
        ]));
        var comparendos = simit.Comparendos ?? [];
        if (comparendos.Count == 0)
        {
            col.Item().Element(c => Aviso(c, "Sin comparendos."));
            return;
        }

        col.Item().Element(c => Grilla(c, ["Número", "Fecha", "Valor", "Estado", "Organismo"],
            [.. comparendos.Select(k => new[]
            {
                k.Numero ?? NoRegistra, k.Fecha ?? NoRegistra, ValorTexto(k.Valor), k.Estado ?? NoRegistra, k.Organismo ?? NoRegistra,
            })]));
        var restantes = simit.CantidadComparendos - comparendos.Count;
        if (simit.ComparendosTruncados || restantes > 0)
            col.Item().Element(c => Aviso(c, $"y {Math.Max(restantes, 0).ToString(CultureInfo.InvariantCulture)} más"));
    });

    private static void Avaluo(IContainer contenedor, ResultadoAvaluoDetalle avaluo) => contenedor.Column(col =>
    {
        col.Spacing(5);
        if (avaluo.SinValorComercial)
        {
            col.Item().Element(c => Aviso(c, "Sin valor comercial"));
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
                    NombreFuenteAvaluo(f.Fuente), f.Estado ?? NoRegistra, f.Valor is { } v ? Moneda(v) : NoRegistra,
                    f.Muestras?.ToString(CultureInfo.InvariantCulture) ?? NoRegistra, f.Fecha ?? NoRegistra,
                })]));
        }
    });

    /// <summary>
    /// Impuesto departamental (HU #13864): «Total adeudado» con el formato de moneda del avalúo y la grilla Vigencia / Valor / Estado
    /// («Pagada» o «Pendiente») por año; sin vigencias, «No registra vigencias».
    /// </summary>
    private static void Impuesto(IContainer contenedor, ResultadoImpuestoDetalle impuesto) => contenedor.Column(col =>
    {
        col.Spacing(5);
        col.Item().Element(c => Tabla(c, [("Total adeudado", Moneda(impuesto.MontoTotal))]));
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
        fuente is null ? NoRegistra : FuentesAvaluo.GetValueOrDefault(fuente, fuente);

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
