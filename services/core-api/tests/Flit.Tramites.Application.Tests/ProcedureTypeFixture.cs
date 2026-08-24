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
