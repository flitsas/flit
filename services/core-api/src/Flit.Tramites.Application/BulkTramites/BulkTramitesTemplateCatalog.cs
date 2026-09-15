namespace Flit.Tramites.Application.BulkTramites;

/// <summary>
/// Encabezados, guías y catálogos cerrados de las 3 plantillas de carga masiva (HU #12520). Es la
/// MISMA lista que debe validar el parser de HU #12522: la plantilla que se entrega y la que se
/// exige no pueden divergir (mismo principio que <c>StandaloneBatchTemplate</c> en Generación
/// Documental).
///
/// <para>Traspaso soporta hasta 4 propietarios por lado (comprador/vendedor) y Matrícula hasta 4
/// propietarios, porque ambos wizards admiten copropiedad con reparto de porcentaje (ADR-0053,
/// <c>PutActorsHandler</c>). La primera versión dejó la matrícula con un solo propietario por una
/// suposición que Samuel Cardenas corrigió en las pruebas: el paso de actores de matrícula ofrece
/// «Agregar propietario» igual que el traspaso.</para>
///
/// <para>Otros trámites es una plantilla única para el resto del catálogo canónico (cambio de
/// color, inscripción de prenda, etc.): trae un selector de tipo de trámite en vez de una
/// plantilla por tipo, con hasta 2 actores porque algunos tipos (p. ej. cambio de locatario)
/// necesitan más de uno.</para>
///
/// <para>Las columnas de actor NO piden el nombre: el procesamiento (HU #12523) consulta la persona
/// en el RUNT por tipo y número de documento y toma el nombre de ahí, igual que hace el paso de
/// actores del wizard. Pedirlo en el Excel era redundante y, peor, permitía guardar un actor sin
/// haberlo consultado. Sí piden celular, ciudad y dirección: son datos de contacto que el RUNT no
/// entrega y el PO los exige en el trámite.</para>
///
/// <para>Un actor con NIT es una empresa (HU #12538): su razón social sale de RUES y su firmante
/// del directorio de representantes legales de la compañía. La única columna extra es la cédula
/// del representante, y solo hace falta cuando hay varios registrados.</para>
/// </summary>
public static class BulkTramitesTemplateCatalog
{
    public const string Version = "v1";
    public const string SheetName = "Datos";
    public const string GuideSheetName = "Instrucciones";
    public const string ListsSheetName = "Listas";
    public const int MaxRows = 50;

    public const string FilaHeader = "fila";
    public const string TipoTramiteHeader = "tipo_tramite";

    /// <summary>
    /// Organismo de tránsito (secretaría). Solo en Matrícula: ahí es OBLIGATORIO —sin él no se
    /// consulta el VIN (HU #11199)— mientras que en traspaso y en el resto lo impone el RUNT.
    /// Lleva el NOMBRE del organismo, no su id: nadie escribe un GUID en un Excel.
    /// </summary>
    public const string OrganismoTransitoHeader = "organismo_transito";

    /// <summary>
    /// Sufijo de la columna opcional <c>{actor}_representante_documento</c> (HU #12538): cédula del
    /// representante legal elegido cuando el actor es una empresa con varios en el directorio.
    /// </summary>
    public const string RepresentanteDocumentoSuffix = "representante_documento";

    /// <summary>Catálogo cerrado de tipos de documento admitidos en las columnas de actor.</summary>
    public static readonly IReadOnlyList<string> TiposDocumento = ["CC", "CE", "NIT", "TI", "PPT", "PAS"];

    private static readonly BulkTramitesColumnSpec Fila = new(
        FilaHeader,
        "Número consecutivo de la fila dentro de este archivo. Úsalo para ubicar el resultado de "
            + "esta fila en el resumen del lote.");

    private static readonly BulkTramitesColumnSpec Placa = new(
        "placa",
        "Placa del vehículo, sin guiones ni espacios. Diligencia placa o VIN; con uno de los dos "
            + "es suficiente para consultar el vehículo.");

    private static readonly BulkTramitesColumnSpec Vin = new(
        "vin",
        "VIN (número de chasis) del vehículo. Diligencia placa o VIN; con uno de los dos es "
            + "suficiente para consultar el vehículo.");

    private static BulkTramitesColumnSpec ActorColumn(string prefijo, string campo, string guia) =>
        new($"{prefijo}_{campo}", guia);

    private static IEnumerable<BulkTramitesColumnSpec> ActorColumns(string prefijo, string rol, bool conPorcentaje)
    {
        yield return ActorColumn(prefijo, "tipo_documento", $"Tipo de documento del {rol}.")
            with
            { Opciones = TiposDocumento };
        yield return ActorColumn(
            prefijo,
            "numero_documento",
            $"Número de documento del {rol}, sin puntos ni guiones. El nombre NO se pide: se toma "
                + "de la consulta al RUNT con este documento, igual que en el paso de actores.");
        yield return ActorColumn(
            prefijo,
            RepresentanteDocumentoSuffix,
            $"Solo si el {rol} es una empresa (NIT): cédula del representante legal que debe "
                + "firmar, cuando la empresa tiene varios registrados en el directorio. Si se deja "
                + "vacío, se toma el representante principal. La empresa y su representante deben "
                + "estar registrados en el directorio de representantes legales; si no, el trámite "
                + "queda por retomar.");
        yield return ActorColumn(prefijo, "email", $"Correo electrónico del {rol}, para el envío de la validación de identidad. Obligatorio.");
        yield return ActorColumn(prefijo, "celular", $"Celular del {rol}. Obligatorio.");
        yield return ActorColumn(prefijo, "ciudad", $"Ciudad de residencia del {rol}. Obligatoria.");
        yield return ActorColumn(prefijo, "direccion", $"Dirección de residencia del {rol}. Obligatoria.");

        if (conPorcentaje)
        {
            yield return ActorColumn(
                prefijo,
                "porcentaje",
                $"Porcentaje de propiedad del {rol}. Solo obligatorio cuando hay más de un "
                    + $"{rol} en la fila; si hay uno solo, se deja vacío. Con 2 o más, deben sumar "
                    + "100 entre todos los del mismo lado y ninguno puede quedar en 0.");
        }
    }

    /// <summary>
    /// Columnas de la plantilla de Matrícula: organismo de tránsito + vehículo + hasta 4
    /// propietarios con su porcentaje cuando hay más de uno. El organismo va PRIMERO entre los
    /// datos porque sin él la fila no se puede procesar: es el dato que el wizard exige antes de
    /// consultar el VIN.
    /// </summary>
    public static IReadOnlyList<BulkTramitesColumnSpec> MatriculaColumns(
        IReadOnlyList<string>? organismosHabilitados = null)
    {
        var columnas = new List<BulkTramitesColumnSpec>
        {
            Fila,
            new(
                OrganismoTransitoHeader,
                "Organismo de tránsito (secretaría) donde se matricula. Obligatorio: elige uno de "
                    + "los que tu empresa tiene habilitados. Escríbelo tal cual aparece en la lista.",
                organismosHabilitados),
            Placa,
            Vin,
        };

        for (var i = 1; i <= 4; i++)
        {
            columnas.AddRange(ActorColumns($"propietario_{i}", "propietario", conPorcentaje: true));
        }

        return columnas;
    }

    /// <summary>
    /// Columnas de la plantilla de Traspaso: vehículo + hasta 4 compradores y 4 vendedores, cada
    /// uno con su porcentaje de propiedad cuando hay más de uno por lado.
    /// </summary>
    public static IReadOnlyList<BulkTramitesColumnSpec> TraspasoColumns()
    {
        var columnas = new List<BulkTramitesColumnSpec> { Fila, Placa, Vin };

        for (var i = 1; i <= 4; i++)
        {
            columnas.AddRange(ActorColumns($"comprador_{i}", "comprador", conPorcentaje: true));
        }

        for (var i = 1; i <= 4; i++)
        {
            columnas.AddRange(ActorColumns($"vendedor_{i}", "vendedor", conPorcentaje: true));
        }

        return columnas;
    }

    /// <summary>
    /// Columnas de la plantilla de Otros trámites: selector de tipo (catálogo canónico recibido
    /// por parámetro, resuelto en BD al momento de generar la plantilla) + vehículo + hasta 2
    /// actores, porque algunos tipos de este grupo necesitan más de uno (p. ej. cambio de locatario).
    /// </summary>
    public static IReadOnlyList<BulkTramitesColumnSpec> OtrosColumns(IReadOnlyList<string> tiposTramiteVigentes)
    {
        var columnas = new List<BulkTramitesColumnSpec>
        {
            Fila,
            new(
                TipoTramiteHeader,
                "Tipo de trámite a crear en esta fila. Elige uno de los códigos del catálogo vigente.",
                tiposTramiteVigentes),
            Placa,
            Vin,
        };

        for (var i = 1; i <= 2; i++)
        {
            var rol = i == 1 ? "actor principal (propietario/comprador/vendedor según el tipo)" : "segundo actor (si el tipo lo requiere)";
            columnas.Add(new($"actor_{i}_rol", $"Rol del {rol} dentro del trámite."));
            columnas.AddRange(ActorColumns($"actor_{i}", rol, conPorcentaje: false));
        }

        return columnas;
    }

    /// <summary>
    /// Columnas de la plantilla. Los catálogos dinámicos solo alimentan los DESPLEGABLES: los
    /// encabezados —que son el contrato con el parser— no dependen de ellos, así que el parser
    /// puede pedir las columnas sin resolver nada contra la base.
    /// </summary>
    public static IReadOnlyList<BulkTramitesColumnSpec> ColumnsFor(
        BulkTramitesTemplateType tipo, BulkTramitesDynamicCatalogs? catalogos = null) => tipo switch
    {
        BulkTramitesTemplateType.Matricula => MatriculaColumns(catalogos?.OrganismosTransito),
        BulkTramitesTemplateType.Traspaso => TraspasoColumns(),
        BulkTramitesTemplateType.Otros => OtrosColumns(catalogos?.TiposTramite ?? []),
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de plantilla no soportado."),
    };

    public static string DisplayName(BulkTramitesTemplateType tipo) => tipo switch
    {
        BulkTramitesTemplateType.Matricula => "Matrícula",
        BulkTramitesTemplateType.Traspaso => "Traspaso",
        BulkTramitesTemplateType.Otros => "Otros trámites",
        _ => tipo.ToString(),
    };
}
