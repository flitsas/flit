using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Tests;

/// <summary>
/// Tipos de trámite para fixtures de prueba (ADR-0050).
/// <para>Desde ADR-0050 la clasificación de un expediente se deriva de su tipo, no de la columna
/// <c>modalidad_entrada</c>. Los tests que construyen una <see cref="ProcedureInstance"/> a mano
/// deben cargar la navegación <c>ProcedureType</c>, o leer <c>Family</c>/<c>TypeCode</c> lanza
/// — deliberadamente, para no clasificar un expediente por accidente.</para>
/// </summary>
internal static class ProcedureTypeFixture
{
    // Instancias ÚNICAS y compartidas, no una nueva por acceso: los tests con DbContext real
    // adjuntan la misma entidad desde varias instancias de trámite, y dos objetos distintos con la
    // misma clave hacen que EF falle con un conflicto de identidad.
    private static readonly ProcedureType MatriculaInstance = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-0000000000a1"),
        Code = "MATRICULA_NUEVA",
        Name = "Matrícula inicial",
        Family = "MATRICULAS",
        GateProfile = """{"entryMode":"VIN","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"requiresPlateRequest":true}""",
        Steps = MatriculaSteps(),
    };

    private static readonly ProcedureType TraspasoInstance = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-0000000000a2"),
        Code = "TRASPASO_STANDARD",
        Name = "Traspaso",
        Family = "TRASPASO",
        GateProfile = """{"entryMode":"PLATE","requiresSeller":true,"requiresBuyer":true,"requiresCommercialValue":true,"requiresBiometrics":true,"biometricActors":["OWNER","BUYER"],"requiresSignature":true}""",
        Steps = TraspasoSteps(),
    };

    public static ProcedureType Matricula => MatriculaInstance;

    public static ProcedureType Traspaso => TraspasoInstance;

    /// <summary>
    /// Cancelación de matrícula: familia MATRICULAS —que acumula— con los complementarios apagados
    /// por TIPO (DDL 93). Acumular presupone un vehículo que sigue inscrito, y este trámite lo saca
    /// del registro.
    /// </summary>
    public static ProcedureType Cancelacion => CancelacionInstance;

    private static readonly ProcedureType CancelacionInstance = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-0000000000a9"),
        Code = "CANCELACION_MATRICULA",
        Name = "Cancelación de matrícula",
        Family = "MATRICULAS",
        GateProfile = """{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true,"allowsComplementaryTransformations":false,"allowsComplementaryPrenda":false}""",
        Steps = MatriculaSteps(),
    };

    // ── Familia OTROS (ADR-0050 / DDL 87) ────────────────────────────────────────────────────────
    // Perfiles con los complementarios apagados, como los deja `87-otros-sin-complementarios.sql`.
    // El recorrido es el NOVEDAD/PRENDA del DDL 82: el titular se captura en el paso «propietario»
    // y su sección se codifica COMPRADOR (el ActorType con el que se persiste).
    //
    // Ninguno declara `hasPrendaGate`: la decisión de prenda ya no sale de una marca del tipo sino
    // de si el trámite ES el gravamen o el RUNT reportó uno (ProcedureTypeLayers.ExigeDecisionDePrenda).

    private static readonly ProcedureType BlindajeInstance = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-0000000000a3"),
        Code = "BLINDAJE",
        Name = "Blindaje",
        Family = "OTROS",
        GateProfile = """{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"allowsComplementaryTransformations":false,"allowsComplementaryPrenda":false}""",
        Steps = NovedadSteps(),
    };

    private static readonly ProcedureType CambioColorInstance = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-0000000000a4"),
        Code = "CAMBIO_COLOR",
        Name = "Cambio de color",
        Family = "OTROS",
        GateProfile = """{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"allowsComplementaryTransformations":false,"allowsComplementaryPrenda":false}""",
        Steps = NovedadSteps(),
    };

    private static readonly ProcedureType CambioCarroceriaInstance = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-0000000000a7"),
        Code = "CAMBIO_CARROCERIA",
        Name = "Cambio de carrocería",
        Family = "OTROS",
        GateProfile = """{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"allowsComplementaryTransformations":false,"allowsComplementaryPrenda":false}""",
        Steps = NovedadSteps(),
    };

    private static readonly ProcedureType LevantamientoPrendaInstance = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-0000000000a5"),
        Code = "LEVANTAMIENTO_PRENDA",
        Name = "Levantamiento de prenda",
        Family = "OTROS",
        GateProfile = """{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"allowsComplementaryTransformations":false,"allowsComplementaryPrenda":false}""",
        Steps = PrendaSteps(),
    };

    /// <summary>
    /// Matrícula por leasing: propietario (entidad financiera) y arrendatario son partes DISTINTAS.
    /// El locatario no entra en <c>biometricActors</c> — se identifica y se notifica, pero quien
    /// valida identidad y firma es el propietario.
    /// </summary>
    private static readonly ProcedureType MatriculaLeasingInstance = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-0000000000a6"),
        Code = "MATRICULA_LEASING",
        Name = "Matrícula Leasing",
        Family = "MATRICULAS",
        GateProfile = """{"entryMode":"VIN","requiresBuyer":true,"requiresLessee":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"requiresPlateRequest":true}""",
        Steps = LeasingSteps(),
    };

    public static ProcedureType MatriculaLeasing => MatriculaLeasingInstance;

    /// <summary>Tipo de OTROS cuyo blindaje ES el trámite (capa base); no gestiona gravamen.</summary>
    public static ProcedureType Blindaje => BlindajeInstance;

    /// <summary>Tipo de OTROS cuyo cambio de color ES el trámite (capa base, no complemento).</summary>
    public static ProcedureType CambioColor => CambioColorInstance;

    /// <summary>Tipo de OTROS cuyo cambio de carrocería ES el trámite; exige carrocería de partida.</summary>
    public static ProcedureType CambioCarroceria => CambioCarroceriaInstance;

    /// <summary>Tipo de OTROS prendario: la decisión de gravamen ES el trámite.</summary>
    public static ProcedureType LevantamientoPrenda => LevantamientoPrendaInstance;

    /// <summary>Tipo de OTROS prendario que CONSTITUYE el gravamen (no lo presupone).</summary>
    public static ProcedureType PrendaInscripcion => PrendaInscripcionInstance;

    private static readonly ProcedureType PrendaInscripcionInstance = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-0000000000a8"),
        Code = "PRENDA_INSCRIPCION",
        Name = "Inscribir prenda",
        Family = "OTROS",
        GateProfile = """{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"allowsComplementaryTransformations":false,"allowsComplementaryPrenda":false}""",
        Steps = PrendaSteps(),
    };

    /// <summary>
    /// Tipo equivalente a la modalidad que el test venía usando. Preserva la semántica de los
    /// fixtures previos a ADR-0050, incluidos los helpers que reciben la modalidad por parámetro.
    /// </summary>
    public static ProcedureType For(string? modalidad) =>
        modalidad is not null && modalidad.Contains("traspaso", StringComparison.OrdinalIgnoreCase)
            ? Traspaso
            : Matricula;

    /// <summary>
    /// Pasos y secciones equivalentes al seed <c>81-parametrizacion-tipos-operativos.sql</c>. Los
    /// tests del wizard ejercitan el motor dinámico con la MISMA conformación que se despliega, así
    /// que un cambio en el seed que no se refleje aquí sale como test roto.
    /// </summary>
    private static List<ProcedureStep> MatriculaSteps() =>
    [
        Step("consulta_vin", "Consulta VIN", 1, ("VEHICULO", "vehicle_query")),
        Step("comprador", "Comprador", 2, ("COMPRADOR", "actor_form")),
        Step("documentos", "Documentos", 3, ("CHECKLIST", "document_checklist")),
        Step("identidad", "Identidad", 4, ("BIOMETRIA", "biometric")),
        Step("fur", "Resumen del trámite", 5, ("FUR", "signature_fur")),
    ];

    /// <summary>Recorrido NOVEDAD del DDL 82: consulta → propietario → documentos → identidad → FUR.</summary>
    private static List<ProcedureStep> NovedadSteps() =>
    [
        Step("consulta", "Consulta del vehículo", 1, ("VEHICULO", "vehicle_query")),
        Step("propietario", "Propietario", 2, ("COMPRADOR", "actor_form")),
        Step("documentos", "Documentos", 3, ("CHECKLIST", "document_checklist")),
        Step("identidad", "Identidad", 4, ("BIOMETRIA", "biometric")),
        Step("fur", "Resumen del trámite", 5, ("FUR", "signature_fur")),
    ];

    /// <summary>Recorrido PRENDA: el de NOVEDAD más el paso propio de decisión de gravamen.</summary>
    private static List<ProcedureStep> PrendaSteps() =>
    [
        Step("consulta", "Consulta del vehículo", 1, ("VEHICULO", "vehicle_query")),
        Step("propietario", "Propietario", 2, ("COMPRADOR", "actor_form")),
        Step("documentos", "Documentos", 3, ("CHECKLIST", "document_checklist")),
        Step("prenda", "Decisión de prenda", 4, ("PRENDA", "prenda_decision")),
        Step("identidad", "Identidad", 5, ("BIOMETRIA", "biometric")),
        Step("fur", "Resumen del trámite", 6, ("FUR", "signature_fur")),
    ];

    /// <summary>Recorrido del leasing (DDL 88): el arrendatario va justo detrás del propietario.</summary>
    private static List<ProcedureStep> LeasingSteps() =>
    [
        Step("consulta_vin", "Consulta VIN", 1, ("VEHICULO", "vehicle_query")),
        Step("comprador", "Comprador", 2, ("COMPRADOR", "actor_form")),
        Step("locatario", "Locatario", 3, ("LOCATARIO", "actor_form")),
        Step("documentos", "Documentos", 4, ("CHECKLIST", "document_checklist")),
        Step("identidad", "Identidad", 5, ("BIOMETRIA", "biometric")),
        Step("fur", "Resumen del trámite", 6, ("FUR", "signature_fur")),
    ];

    private static List<ProcedureStep> TraspasoSteps() =>
    [
        Step("consulta", "Consulta del vehículo", 1, ("VEHICULO", "vehicle_query")),
        Step("vendedor", "Vendedor", 2, ("VENDEDOR", "actor_form")),
        Step("comprador", "Comprador", 3, ("COMPRADOR", "actor_form")),
        // El paso de documentos absorbió los datos comerciales: dos secciones.
        Step("documentos", "Documentos", 4, ("CHECKLIST", "document_checklist"), ("COMERCIAL", "commercial")),
        Step("identidad", "Identidad", 5, ("BIOMETRIA", "biometric")),
        Step("fur", "Resumen del trámite", 6, ("FUR", "signature_fur")),
    ];

    private static ProcedureStep Step(
        string code, string title, short order, params (string Code, string Type)[] sections) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = title,
            SortOrder = order,
            IsActive = true,
            Sections = [.. sections.Select((s, i) => new ProcedureSection
            {
                Id = Guid.NewGuid(),
                Code = s.Code,
                Title = s.Code,
                SortOrder = (short)(i + 1),
                SectionType = s.Type,
            })],
        };
}
