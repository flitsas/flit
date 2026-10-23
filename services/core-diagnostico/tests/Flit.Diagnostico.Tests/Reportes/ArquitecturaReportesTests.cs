using System.Reflection;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// HU #13847 (F07-H2, F07-C9, R-20) — prueba de arquitectura por reflexión: ningún tipo que el contenido del reporte expone a los
/// renderizadores (<see cref="ContenidoReporteVehiculo"/>, <see cref="ContenidoReporteLote"/> y todo lo que cuelga de ellos,
/// incluidos los tipos derivados de <see cref="ResultadoDetalleDto"/>) tiene una propiedad <c>Origen</c>, <c>UsuarioManual*</c>,
/// <c>Servicio*</c>, <c>Transaccion*</c>, <c>DocCifrado</c> ni <c>Payload</c>. Si alguien la agrega, la prueba falla y nombra el
/// tipo y la propiedad.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ArquitecturaReportesTests
/// </code>
/// </example>
public sealed class ArquitecturaReportesTests
{
    private static readonly string[] Prefijos = ["Origen", "UsuarioManual", "Servicio", "Transaccion", "DocCifrado", "Payload"];

    /// <summary>
    /// Única excepción, nombrada: <c>servicio</c> del RUNT del vehículo es la clase de servicio del vehículo (PARTICULAR, PÚBLICO),
    /// un dato del reporte, no el proveedor que resolvió la consulta (<c>servicio_que_resolvio</c>, que no se proyecta).
    /// </summary>
    private static readonly string[] Permitidas = [$"{nameof(ResultadoRuntDetalle)}.{nameof(ResultadoRuntDetalle.Servicio)}"];

    // AC5 (F07-C9) — los tipos del reporte no tienen origen, usuario manual, proveedor, transacción, documento cifrado ni payload;
    // la reflexión sí recorre las secciones, las personas y los resultados.
    [Fact]
    public void Tipos_del_reporte_no_tienen_origen_usuario_manual_ni_documento()
    {
        Type[] raices = [typeof(ContenidoReporteVehiculo), typeof(ContenidoReporteLote)];

        var recorridas = Propiedades(raices).Select(p => $"{p.DeclaringType!.Name}.{p.Name}").ToList();

        recorridas.Should().Contain(
            ["SeccionReporte.Resultado", "PersonaReporte.DocumentoEnmascarado", "VehiculoReporte.Secciones", "ResultadoSimitDetalle.Comparendos",
                "ResultadoRuntPersonaDetalle.Licencias", "EncabezadoReporte.UsuarioCreador"],
            "la reflexión recorre todo lo que llega al renderizador");
        Violaciones(raices).Should().BeEmpty();
    }

    // AC5 (negativo, F07-C9) — un tipo del reporte con una propiedad Origen (o UsuarioManualId, o DocCifrado) hace fallar la prueba
    // y el mensaje nombra el tipo y la propiedad, también si cuelga de una lista.
    [Fact]
    public void Un_tipo_con_origen_hace_fallar_la_prueba_y_la_nombra()
    {
        Violaciones([typeof(ReporteConOrigen)]).Should().BeEquivalentTo(
            ["ReporteConOrigen.Origen", "SeccionConUsuarioManual.UsuarioManualId", "SeccionConUsuarioManual.DocCifrado"]);

        var prueba = () => Violaciones([typeof(ReporteConOrigen)]).Should().BeEmpty();
        prueba.Should().Throw<Exception>().WithMessage("*ReporteConOrigen.Origen*");
    }

    // HU #13864 (F09-C9, R-20) — el resultado del impuesto llega al renderizador (está registrado entre los derivados de
    // ResultadoDetalleDto) y ni él ni sus vigencias tienen proveedor, transacción, origen ni departamento: sus claves JSON son
    // exactamente monto_total/vigencias y anio/valor/pagada.
    [Fact]
    public void ResultadoImpuestoDetalle_no_tiene_proveedor_transaccion_ni_origen()
    {
        var recorridas = Propiedades([typeof(ContenidoReporteVehiculo)]).Select(p => $"{p.DeclaringType!.Name}.{p.Name}").ToList();

        recorridas.Should().Contain(
            ["ResultadoImpuestoDetalle.MontoTotal", "ResultadoImpuestoDetalle.Vigencias", "VigenciaImpuestoDetalle.Pagada"],
            "el resultado del impuesto es un derivado registrado de ResultadoDetalleDto (JsonDerivedType)");
        Violaciones([typeof(ResultadoImpuestoDetalle)]).Should().BeEmpty();
        Json(typeof(ResultadoImpuestoDetalle)).Should().BeEquivalentTo(["monto_total", "vigencias"]);
        Json(typeof(VigenciaImpuestoDetalle)).Should().BeEquivalentTo(["anio", "valor", "pagada"]);
        Propiedades([typeof(ResultadoImpuestoDetalle)]).Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Proveedor", StringComparison.OrdinalIgnoreCase) || n.Contains("Departamento", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Json(Type tipo) =>
        tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name);

    /// <summary>«Tipo.Propiedad» de cada propiedad prohibida alcanzable desde <paramref name="raices"/>.</summary>
    internal static IReadOnlyList<string> Violaciones(IEnumerable<Type> raices) =>
        [.. Propiedades(raices)
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .Where(n => !Permitidas.Contains(n, StringComparer.Ordinal))
            .Where(n => Prefijos.Any(prefijo => n[(n.IndexOf('.', StringComparison.Ordinal) + 1)..].StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)))
            .Distinct()];

    /// <summary>Propiedades públicas de las raíces, de los tipos del ensamblado que cuelgan de ellas y de sus derivados JSON.</summary>
    private static IEnumerable<PropertyInfo> Propiedades(IEnumerable<Type> raices)
    {
        var vistos = new HashSet<Type>();
        var pendientes = new Queue<Type>(raices);
        while (pendientes.Count > 0)
        {
            var tipo = pendientes.Dequeue();
            if (!vistos.Add(tipo))
                continue;
            foreach (var derivado in tipo.GetCustomAttributes<JsonDerivedTypeAttribute>())
                pendientes.Enqueue(derivado.DerivedType);
            foreach (var propiedad in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (propiedad.Name == "EqualityContract")
                    continue;
                yield return propiedad;
                var t = Nullable.GetUnderlyingType(propiedad.PropertyType) ?? propiedad.PropertyType;
                foreach (var candidato in t.IsGenericType ? t.GetGenericArguments() : [t])
                {
                    if (candidato.Assembly == typeof(ContenidoReporteVehiculo).Assembly || candidato.Assembly == typeof(ArquitecturaReportesTests).Assembly)
                        pendientes.Enqueue(candidato);
                }
            }
        }
    }

    /// <summary>Tipo de prueba que viola la regla (solo existe en las pruebas).</summary>
    internal sealed record ReporteConOrigen(string Origen, IReadOnlyList<SeccionConUsuarioManual> Secciones);

    internal sealed record SeccionConUsuarioManual(Guid? UsuarioManualId, byte[] DocCifrado, string Fuente);
}
