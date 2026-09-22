using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12774 — el DDL que siembra los tipos documentales del certificado de Cámara de Comercio por
/// rol. Se verifica sobre el recurso embebido y no contra una base viva: lo que se protege aquí son
/// decisiones que viven en el texto del script (un código por rol, PDF único, fuera de la matriz
/// documental, reaplicable), no el estado de un ambiente.
/// </summary>
public sealed class CamaraComercioPorRolDdlTests
{
    private const string ResourceName =
        "Flit.Infrastructure.Persistence.Sql.Ddl.118-HU12774-camara-comercio-por-rol.sql";

    private static string LoadDdl()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        stream.Should().NotBeNull($"el DDL embebido {ResourceName} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// El DDL sin sus comentarios <c>--</c>. Las pruebas que exigen la AUSENCIA de algo tienen que
    /// mirar las sentencias y no la documentación: la cabecera de este script explica precisamente
    /// por qué el tipo no entra en la matriz documental ni se marca como generado, así que buscar
    /// esas palabras en el texto completo daría un fallo por decir la verdad.
    /// </summary>
    private static string LoadStatements() =>
        string.Join(
            '\n',
            LoadDdl()
                .Split('\n')
                .Select(line =>
                {
                    var comment = line.IndexOf("--", StringComparison.Ordinal);
                    return comment < 0 ? line : line[..comment];
                }));

    /// <summary>AC1 — los tres tipos del catálogo, uno por rol real del modelo.</summary>
    [Theory]
    [InlineData("camara_comercio_vendedor")]
    [InlineData("camara_comercio_comprador")]
    [InlineData("camara_comercio_locatario")]
    public void Ddl_SiembraElTipoDelRol(string code)
    {
        LoadDdl().Should().Contain($"('{code}',",
            "cada rol que puede ser persona jurídica necesita su propio código");
    }

    /// <summary>
    /// AC1 — el campo acepta únicamente PDF. Se enforcea de verdad: <c>AttachmentValidator</c> usa
    /// los <c>mime_types_allowed</c> del catálogo cuando el tipo existe en él, y solo cae al set
    /// global cuando no hay regla.
    /// </summary>
    [Fact]
    public void Ddl_RestringeElFormatoAPdf()
    {
        var sql = LoadDdl();

        sql.Should().Contain("""'["application/pdf"]'""",
            "el criterio funcional dice PDF y solo PDF");
        sql.Should().NotContain("image/jpeg",
            "aceptar imágenes dejaría pasar una foto del certificado, que el OCR no puede validar igual");
    }

    /// <summary>
    /// AC4 — camino A: el documento se pide en el paso del actor, no por la matriz documental. Si
    /// alguien lo siembra en <c>procedure_document_requirements</c> aparecería en el checklist de
    /// Requisitos y en la lista ordenable del OT, que es justo lo que esta HU decidió no hacer.
    /// </summary>
    [Fact]
    public void Ddl_NoEntraALaMatrizDocumental()
    {
        LoadStatements().Should().NotContain("procedure_document_requirements",
            "el gate vive en el paso del actor, igual que en escritura_representante");
    }

    /// <summary>
    /// El documento SE CARGA, no se genera: dejar <c>is_system_generated</c> en su default false es
    /// lo que impide que la limpieza de huérfanos del expediente lo retire (esa barre los tipos de
    /// sistema sin mirar el <c>source</c>).
    /// </summary>
    [Fact]
    public void Ddl_NoMarcaLosTiposComoGeneradosPorElSistema()
    {
        LoadStatements().Should().NotContain("is_system_generated",
            "el default false es el correcto y tocarlo expondría el adjunto a la limpieza de huérfanos");
    }

    /// <summary>AC6 — reaplicable sin efecto.</summary>
    [Fact]
    public void Ddl_EsIdempotente()
    {
        var sql = LoadDdl();

        sql.Should().Contain("ON CONFLICT (code) DO NOTHING",
            "el DDL se reaplica en cada arranque y no puede fallar ni duplicar");
        sql.Should().Contain("upload_instructions IS NULL",
            "la instrucción inicial no puede pisar lo que el admin haya escrito desde el módulo documental");
    }

    /// <summary>
    /// No se reutiliza el código <c>camara_comercio</c> del catálogo de paridad: arrastra los datos
    /// migrados de V1, donde las tres llaves del legado colapsan en él.
    /// </summary>
    [Fact]
    public void Ddl_NoTocaElCodigoHistoricoDeCamaraComercio()
    {
        LoadStatements().Should().NotContain("('camara_comercio',",
            "el código sin sufijo es el de V1 y mezclarlo reintroduciría la colisión que la HU evita");
    }

    /// <summary>Los códigos del DDL y los del helper no pueden divergir.</summary>
    [Fact]
    public void Ddl_YHelper_DeclaranLosMismosCodigos()
    {
        var sql = LoadDdl();

        foreach (var code in CamaraComercioAttachmentTipo.Todos)
        {
            sql.Should().Contain($"('{code}',",
                "un código que el helper usa pero el catálogo no siembra no protege ninguna casilla");
        }
    }
}
