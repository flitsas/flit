using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Guardián de ORDEN de escritura del tipo de servicio (casilla 18 del FUR) al crear un trámite
/// desde la consulta del paso 1.
///
/// <para><b>El bug que fija.</b> En matrícula inicial el operador elige el tipo de servicio en el
/// paso 1 y se persiste en <c>vehicle_service</c> — el MISMO field_value que el preflight hidrata
/// desde el proveedor de consulta. Cuando la elección se escribía ANTES del preflight, la hidratación
/// la pisaba: los proveedores devuelven <c>TipoServicio</c> igual aunque el vehículo no exista todavía
/// en el RUNT (hoy con "Particular" fijo en sus datos de demo), así que el FUR salía con la casilla
/// "PARTICULAR" marcada habiendo elegido "PÚBLICO". La casilla 19 (empresa vinculadora) sí conservaba
/// el dato, porque el preflight no toca esas llaves — de ahí lo desconcertante del síntoma.</para>
///
/// <para><b>Por qué un guardián de código fuente y no un test de comportamiento.</b> El camino feliz
/// completo de <c>CreateProcedureInstanceFromConsultaHandler</c> no es montable hoy en un test: encadena
/// <c>CreateProcedureInstanceHandler</c> y <c>RunPreflightHandler</c>, clases selladas con dependencias
/// propias que ningún test previo mockea (ver la nota de alcance en
/// <see cref="CreateProcedureInstanceFromConsultaHandlerTests"/>). El invariante que importa es de
/// ORDEN, y el orden se lee en el fuente. Mismo criterio —y misma limitación honesta— que
/// <see cref="FieldValueContractGuardTests"/>: no demuestra la ejecución, pero convierte una
/// regresión silenciosa en una decisión explícita y revisable.</para>
/// </summary>
public sealed class TipoServicioOrdenDeEscrituraGuardTests
{
    private const string RutaFuente =
        "services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/CreateFromConsultaCommand.cs";

    [Fact]
    public void ElTipoDeServicio_SeEscribeDespuesDelPreflight_ParaQueNoLoPiseLaHidratacion()
    {
        var fuente = LeerFuente();

        var indicePreflight = fuente.IndexOf("preflightHandler.HandleAsync", StringComparison.Ordinal);
        // Lo que importa es cuándo se EJECUTA el patch, no cuándo se arma la lista: la lista se
        // construye arriba, junto al resto de campos, y eso es irrelevante para el pisado.
        var indicePatch = fuente.IndexOf(
            "new PatchFieldValuesRequest(tipoServicioFieldValues)", StringComparison.Ordinal);

        indicePreflight.Should().BeGreaterThan(
            -1, "el handler debe seguir invocando el preflight; si cambió de nombre, actualiza este guardián");
        indicePatch.Should().BeGreaterThan(
            -1,
            "la elección del operador debe seguir persistiéndose en su propio patch "
            + "(`tipoServicioFieldValues`); si se fusionó con el patch general, el pisado vuelve");

        indicePatch.Should().BeGreaterThan(
            indicePreflight,
            "el patch del tipo de servicio debe ejecutarse DESPUÉS de `preflightHandler.HandleAsync`. "
            + "El preflight hidrata `vehicle_service` desde el proveedor de consulta y pisaría el "
            + "tipo elegido en el paso 1: el FUR saldría marcando PARTICULAR habiendo elegido PÚBLICO. "
            + "Si necesitas moverlo, el orden es el arreglo — no lo deshagas sin resolver el pisado.");
    }

    /// <summary>
    /// Las tres llaves de las casillas 18/19 viajan juntas: separarlas volvería a abrir la puerta a que
    /// una se escriba antes del preflight y otra después, que es justo la desincronización que produjo
    /// el síntoma original (casilla 19 correcta, casilla 18 pisada).
    /// </summary>
    [Fact]
    public void LasTresLlavesDeLasCasillas18y19_SeEscribenEnElMismoPunto()
    {
        var fuente = LeerFuente();

        foreach (var llave in new[] { "vehicle_service", "empresa_vinculadora_nit", "empresa_vinculadora_razon_social" })
        {
            var indice = IndiceDeEscritura(fuente, llave);
            indice.Should().BeGreaterThan(-1, $"la llave {llave} debe seguir persistiéndose");

            // Todas tienen que acumularse en la MISMA lista, la que se patchea tras el preflight. Si
            // una cayera en `items` (el patch general, anterior al preflight) volvería el síntoma
            // original: casilla 19 correcta y casilla 18 pisada por la hidratación.
            var listaDestino = fuente.LastIndexOf(
                "tipoServicioFieldValues.Add(", indice, StringComparison.Ordinal);
            var listaGeneral = fuente.LastIndexOf("items.Add(", indice, StringComparison.Ordinal);
            listaDestino.Should().BeGreaterThan(
                listaGeneral,
                $"{llave} debe añadirse a `tipoServicioFieldValues`, no al patch general `items`");
        }
    }

    /// <summary>
    /// Posición de la ESCRITURA real de la llave, no de cualquier mención. Se ancla al
    /// <c>new FieldValueInput(null, "&lt;llave&gt;"</c>: los comentarios del handler nombran estas llaves
    /// varias veces —y antes del preflight—, así que un <c>IndexOf</c> del literal a secas mediría
    /// prosa en vez de código.
    /// </summary>
    private static int IndiceDeEscritura(string fuente, string llave) =>
        fuente.IndexOf($"new FieldValueInput(null, \"{llave}\"", StringComparison.Ordinal);

    private static string LeerFuente()
    {
        var raiz = BuscarRaizDelRepo();
        var ruta = Path.Combine(raiz, RutaFuente.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(ruta).Should().BeTrue($"no se encontró el fuente en {ruta}");
        return File.ReadAllText(ruta);
    }

    private static string BuscarRaizDelRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "services", "core-api", "src")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("No se encontró la raíz del repositorio desde el directorio de pruebas.");
    }
}
