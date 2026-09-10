using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Las reglas de adjuntos que se declaran por CÓDIGO de documento solo pueden nombrar códigos que
/// el catálogo siembra de verdad (<c>tramites.document_types</c>).
/// <para>
/// Motivo: <c>AttachmentRules.TiposMultiples</c> llegó a incluir <c>documentosTramite</c>, un
/// código que no siembra ningún DDL y que solo existía en una base de pruebas local, creado a mano
/// desde la UI del módulo documental por el usuario demo. No protegía ninguna casilla y, peor, hacía
/// creer a quien leyera la regla que esa casilla existe. Estas pruebas hacen que un código inventado
/// rompa la build en vez de viajar al despliegue de los demás ambientes.
/// </para>
/// <para>
/// La contraparte —que todo documento del catálogo esté sembrado— la cubre
/// <see cref="DocumentCatalogParityTests"/>.
/// </para>
/// </summary>
public sealed partial class AttachmentRulesCatalogParityTests
{
    /// <summary>
    /// Códigos sembrados en cualquiera de los DDL embebidos. Se leen todos los scripts y no solo el
    /// seed de paridad, porque el catálogo creció por partes (documentos generados, prenda, blindaje,
    /// escrituras del representante…) y una regla puede nombrar legítimamente cualquiera de ellos.
    /// </summary>
    private static readonly Lazy<HashSet<string>> SeededCodes = new(LoadSeededCodes);

    private static HashSet<string> LoadSeededCodes()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.Contains(".Sql.Ddl.", StringComparison.Ordinal)
                                 && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
                continue;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var sql = reader.ReadToEnd();

            // Solo las sentencias que insertan en el catálogo: otras tablas usan la misma forma
            // ('codigo', ...) para causales de rechazo, campos del FUR, etc.
            foreach (Match insert in DocumentTypesInsert().Matches(sql))
            {
                foreach (Match row in RowCode().Matches(insert.Value))
                {
                    codes.Add(row.Groups[1].Value);
                }
            }
        }

        return codes;
    }

    // INSERT INTO tramites.document_types ... hasta el ';' que cierra la sentencia.
    [GeneratedRegex(
        @"INSERT\s+INTO\s+tramites\.document_types\b.*?;",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DocumentTypesInsert();

    // Primer literal de cada fila de VALUES: ('codigo', 'Nombre', ...).
    [GeneratedRegex(@"\(\s*'([A-Za-z0-9_-]+)'\s*,", RegexOptions.None)]
    private static partial Regex RowCode();

    /// <summary>Red de seguridad: si el barrido de DDL dejara de encontrar el catálogo, el resto
    /// de las pruebas pasaría en vacío y no protegería nada.</summary>
    [Fact]
    public void SeededCodes_AreDiscovered()
    {
        SeededCodes.Value.Should().NotBeEmpty("el barrido de los DDL debe encontrar el catálogo");
        SeededCodes.Value.Should().Contain("paz_salvo").And.Contain("anexos_generales");
    }

    /// <summary>
    /// Las bolsas que admiten varios adjuntos (HU #12046) deben existir en el catálogo: una casilla
    /// que no está sembrada no puede acumular nada.
    /// </summary>
    [Fact]
    public void TiposMultiples_OnlyNamesSeededCatalogCodes()
    {
        var desconocidos = AttachmentRules.TiposMultiples
            .Where(code => !SeededCodes.Value.Contains(code))
            .ToList();

        desconocidos.Should().BeEmpty(
            "una regla de adjuntos no puede nombrar un código que el catálogo no siembra; "
            + "si el tipo es nuevo, hay que sembrarlo en un DDL antes de declararlo aquí");
    }

    /// <summary>
    /// La evidencia de SOAT (HU #10611) se declara igual, por código: mismo riesgo, misma guarda.
    /// </summary>
    [Fact]
    public void SoatEvidenceTipos_OnlyNamesSeededCatalogCodes()
    {
        var desconocidos = AttachmentRules.SoatEvidenceTipos
            .Where(code => !SeededCodes.Value.Contains(code))
            .ToList();

        desconocidos.Should().BeEmpty(
            "los tipos de evidencia de SOAT deben existir en el catálogo de documentos");
    }

    /// <summary>
    /// El código retirado no puede volver por la puerta de atrás: ni en las reglas ni sembrado en un
    /// DDL. Es camelCase, forma que el generador de códigos actual no produce (slug en MAYÚSCULAS),
    /// así que si reaparece es porque alguien volvió a copiarlo de una base de pruebas.
    /// </summary>
    [Fact]
    public void PhantomDocumentosTramiteCode_IsGoneForGood()
    {
        AttachmentRules.TiposMultiples.Should().NotContain("documentosTramite");
        SeededCodes.Value.Should().NotContain("documentosTramite");
    }
}
