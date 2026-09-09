using System.Globalization;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Infrastructure.Documents.Branding;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Standalone;

/// <summary>
/// Bloques reutilizables del Documento de Transferencia de Dominio (Feature #12201, I2):
/// encabezado, tabla de identificación del vehículo, cláusula genérica, bloque de firmas y
/// advertencia normativa. Los tres escenarios del anexo se arman con estas piezas.
///
/// <para><b>El bloque de firmas recibe una LISTA de firmantes, no un par con uno opcional.</b> Es
/// la regla estructural del anexo §9.0.3 y §9.2: <i>el escenario decide cuántos bloques de firma
/// existen; el modo de firma solo decide qué va dentro de un bloque que ya existe</i>. Con esta
/// firma de método, el escenario B de HU-06 pasa una lista de un elemento y el bloque del
/// adquirente/locatario <b>no puede instanciarse</b>: no hay parámetro que dejar nulo, no hay
/// columna que ocultar y no hay cascada por defecto que produzca un hueco en blanco —el defecto que
/// sí tiene el componente de firma del expediente
/// (<c>FurCompraventaDocumentGenerator.BloqueFirma</c>: «sin firma validada el hueco queda en
/// blanco»), y que §10 regla #4 prohíbe expresamente.</para>
///
/// <para><b>Ningún bloque consulta el baúl de firmas ni pinta sello alguno.</b> El modo vigente es
/// <c>MANUSCRITA</c> (§9.0): línea en blanco, nombre y documento. Esta clase no tiene acceso a
/// imágenes de firma ni a sellos de validación de identidad.</para>
/// </summary>
internal static class TransferDocumentBlocks
{
    private static readonly string[] Meses =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    ];

    /// <summary>Rótulo del escenario en el encabezado común (anexo §7).</summary>
    private static string EscenarioEtiqueta(string scenario) => scenario switch
    {
        "A" => "A — Traspaso ordinario (art. 5.3.2.1)",
        "B" => "B — Transferencia unilateral leasing (art. 5.3.2.2)",
        "C" => "C — Transferencia a tercero (sin exenciones del art. 5.3.2.2)",
        _ => scenario,
    };

    /// <summary>Encabezado común a los tres escenarios (anexo §7).</summary>
    public static void Encabezado(ColumnDescriptor col, TransferDocumentModel model)
    {
        col.Item().AlignCenter().Text(t =>
            t.Span("DOCUMENTO DE TRANSFERENCIA DE DOMINIO DE VEHÍCULO AUTOMOTOR")
                .Bold()
                .FontColor(FlitDocumentTheme.DarkNavy));

        col.Item().PaddingTop(6).Text($"Ciudad: {model.CiudadFirma}");
        col.Item().Text($"Fecha: {FechaEnLetras(model.FechaFirma)}");
        col.Item().Text($"Placa: {model.Vehiculo.Placa}");
        col.Item().Text($"Escenario: {EscenarioEtiqueta(model.Scenario)}");
    }

    /// <summary>Cláusula con título en negrita y uno o varios párrafos.</summary>
    public static void Clausula(ColumnDescriptor col, string titulo, params string[] parrafos)
    {
        col.Item().PaddingTop(10).Text(t => t.Span(titulo).Bold().FontColor(FlitDocumentTheme.DarkNavy));

        foreach (var parrafo in parrafos.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            col.Item().PaddingTop(4).Text(parrafo).Justify();
        }
    }

    /// <summary>
    /// Tabla de identificación del vehículo: los 13 campos del anexo §5.1, sin excepción. Un campo
    /// sin dato se imprime con una raya y no se omite: el checklist §13.1 exige que la tabla incluya
    /// los 13, y una fila ausente se lee como si el vehículo no tuviera ese atributo.
    /// </summary>
    public static void TablaVehiculo(ColumnDescriptor col, TransferDocumentVehicle vehiculo)
    {
        col.Item().PaddingTop(6).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                columns.RelativeColumn(3);
            });

            Fila(table, "Placa", vehiculo.Placa);
            Fila(table, "Marca", vehiculo.Marca);
            Fila(table, "Línea", vehiculo.Linea);
            Fila(table, "Año modelo", vehiculo.ModeloAnio);
            Fila(table, "Clase", vehiculo.ClaseVehiculo);
            Fila(table, "Carrocería", vehiculo.TipoCarroceria);
            Fila(table, "Color(es)", vehiculo.Color);
            Fila(table, "Motor No.", vehiculo.NoMotor);
            Fila(table, "Chasis / VIN No.", vehiculo.NoChasis);
            Fila(table, "Serie No.", vehiculo.NoSerie);
            Fila(table, "Servicio", vehiculo.Servicio);
            Fila(table, "Licencia de Tránsito No.", vehiculo.NoLicenciaTransito);
            Fila(table, "Organismo de Tránsito", vehiculo.OrganismoTransito);
        });
    }

    /// <summary>
    /// Bloque de firmas. <b>Una columna por firmante de la lista, ni una más.</b>
    ///
    /// <para>La lista vacía es un error de programación, no un documento sin firmas: un instrumento
    /// privado sin ningún espacio de firma no sirve para nada y el fallo debe verse en el generador,
    /// no en la mesa del Organismo de Tránsito.</para>
    ///
    /// <para>Modo <c>MANUSCRITA</c> (§9.0): línea en blanco para firmar de puño y letra, nombre,
    /// identificación y —si la parte es persona jurídica— representante legal. Sin leyenda de firma
    /// electrónica, sin sello de baúl y sin sello de validación de identidad.</para>
    /// </summary>
    public static void Firmas(
        ColumnDescriptor col,
        string ciudad,
        DateOnly fecha,
        IReadOnlyList<TransferDocumentParty> firmantes)
    {
        ArgumentNullException.ThrowIfNull(firmantes);

        if (firmantes.Count == 0)
        {
            throw new ArgumentException(
                "El documento debe tener al menos un firmante: el escenario decide cuántos bloques existen.",
                nameof(firmantes));
        }

        col.Item().PaddingTop(18).Text(
            $"En {ciudad}, a los {fecha.Day} días del mes de {Meses[fecha.Month - 1]} de "
            + fecha.Year.ToString(CultureInfo.InvariantCulture) + ".");

        col.Item().PaddingTop(24).Row(row =>
        {
            row.Spacing(18);

            foreach (var firmante in firmantes)
            {
                row.RelativeItem().Column(bloque =>
                {
                    bloque.Item().Text(t => t.Span(firmante.Etiqueta).Bold().FontColor(FlitDocumentTheme.DarkNavy));
                    bloque.Item().PaddingTop(22).Text("_______________________________");
                    bloque.Item().Text(firmante.NombreRazonSocial);
                    bloque.Item().Text(firmante.Identificacion);

                    if (!string.IsNullOrWhiteSpace(firmante.RepresentanteLegal))
                    {
                        bloque.Item().Text($"Representante Legal: {firmante.RepresentanteLegal}");
                    }

                    if (!string.IsNullOrWhiteSpace(firmante.CcRepresentanteLegal))
                    {
                        bloque.Item().Text($"C.C. RL: {firmante.CcRepresentanteLegal}");
                    }
                });
            }
        });
    }

    /// <summary>
    /// Advertencia del anexo §1, exigida por el checklist §13.5. Va en el propio documento para que
    /// sobreviva a la descarga: un aviso que solo vive en la pantalla desaparece en cuanto el PDF
    /// cambia de manos.
    /// </summary>
    public static void AdvertenciaNormativa(ColumnDescriptor col)
    {
        col.Item().PaddingTop(18).Text(t => t
            .Span(
                "Advertencia: este es un instrumento privado parametrizado. FLIT no es parte del negocio "
                + "jurídico subyacente y no garantiza su suficiencia como prueba de dominio ni la "
                + "aprobación del trámite por el Organismo de Tránsito, que realiza validaciones propias "
                + "(RUNT, SOAT, SIMIT, RTM y medidas judiciales). La Resolución 20233040017145 de 2023 no "
                + "prescribe un formato interno para el título de dominio del art. 5.3.2.1 numeral 1.º; "
                + "las cláusulas de este documento son neutrales e indicativas.")
            .FontSize(8)
            .Italic());
    }

    /// <summary>«9 de septiembre de 2026» — sin <c>CultureInfo</c>: la API compila con
    /// <c>InvariantGlobalization</c> y el nombre del mes en español no existe ahí.</summary>
    public static string FechaEnLetras(DateOnly fecha) =>
        $"{fecha.Day} de {Meses[fecha.Month - 1]} de {fecha.Year.ToString(CultureInfo.InvariantCulture)}";

    private static void Fila(TableDescriptor table, string campo, string? valor)
    {
        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4)
            .Text(t => t.Span(campo).SemiBold());
        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4)
            .Text(string.IsNullOrWhiteSpace(valor) ? "—" : valor);
    }
}
