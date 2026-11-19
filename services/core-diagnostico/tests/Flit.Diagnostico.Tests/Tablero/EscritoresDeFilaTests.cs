using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #14563 (B1, AC8, ADR-0086 D-1) — prueba de arquitectura de los escritores de fila. Lee el IL de core-diagnostico: todo método
/// que inserta un <see cref="DictamenVehiculo"/> (<c>newobj</c>) o asigna <see cref="VehiculoDiagnostico.TramiteId"/> llama a
/// <see cref="EscritorCeldas.SubirVersionFilaAsync"/> o a <see cref="EscritorCeldas.MarcarFila"/> (la emisión automática reutiliza la
/// versión de la celda). Los mutadores puros de la lista cerrada (arman la fila en memoria, sin contexto) quedan cubiertos si
/// TODOS sus llamadores llaman al helper. Un método se cuenta con sus lambdas, funciones locales y máquinas de estado.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EscritoresDeFilaTests
/// </code>
/// </example>
public sealed partial class EscritoresDeFilaTests
{
    /// <summary>Lista cerrada (anexo B1): mutadores puros que arman la fila sin contexto; la versión la sube quien los llama.</summary>
    private static readonly string[] MutadoresPuros =
    [
        "CierreItemDerivacion.Cerrar",
        "EmisorDictamen.Materializar",
        "EmisorDictamen.FilaAlCerrar",
        "EmisorDictamen.FilaDeOverride",
        // HU #14563 B7: arma la fila del ajuste con alertamientos; el PUT (Reemplazar) sube la versión antes de llamarla.
        "EmisorDictamen.FilaDeAjuste",
        // HU #14563 code review menor 13: la construcción de la fila salió de la lambda del PUT; Reemplazar sube la versión antes.
        "OverrideDictamenEndpoints.NuevaFila",
    ];

    /// <summary>
    /// Escritores que aún no llaman al helper y tienen dueño. Vacía desde B7: el PUT del backoffice (<c>Reemplazar</c>) llama a
    /// <see cref="EscritorCeldas.SubirVersionFilaAsync"/> antes de <c>FilaDeOverride</c> o <c>FilaDeAjuste</c>.
    /// </summary>
    private static readonly string[] PendientesB7 = [];

    [Fact]
    public void HU14563_AC8_Todo_escritor_de_fila_sube_la_version_de_fila()
    {
        var violaciones = Revisar(typeof(EscritorCeldas).Assembly.GetTypes());

        violaciones.Should().BeEquivalentTo(PendientesB7, "todo escritor de dictamen o de trámite llama a SubirVersionFilaAsync o a MarcarFila");
    }

    [Fact]
    public void HU14563_AC8_Un_escritor_nuevo_sin_el_helper_hace_fallar_la_prueba()
    {
        var violaciones = Revisar([.. typeof(EscritorCeldas).Assembly.GetTypes(), typeof(EscritoresNuevos)]);

        violaciones.Should().Contain(["EscritoresNuevos.FijarTramite", "EscritoresNuevos.CerrarSinVersion"]);
        violaciones.Should().NotContain("EscritoresNuevos.CerrarConVersion");
    }

    /// <summary>Unidades que escriben la fila sin subir su versión (directas o por un mutador puro).</summary>
    private static List<string> Revisar(IReadOnlyCollection<Type> tipos)
    {
        var setTramite = typeof(VehiculoDiagnostico).GetProperty(nameof(VehiculoDiagnostico.TramiteId))!.SetMethod!;
        var helpers = new HashSet<MethodBase>(typeof(EscritorCeldas).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name is nameof(EscritorCeldas.SubirVersionFilaAsync) or nameof(EscritorCeldas.MarcarFila) or nameof(EscritorCeldas.MarcarFilaAsync)));

        var propios = new HashSet<Type>(tipos);
        var escriben = new HashSet<string>(StringComparer.Ordinal);
        var llamanHelper = new HashSet<string>(StringComparer.Ordinal);
        var llamadores = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var metodo in tipos.SelectMany(Metodos))
        {
            var unidad = Unidad(metodo);
            foreach (var referido in Referidos(metodo))
            {
                if (referido == setTramite || referido is ConstructorInfo { DeclaringType: var d } && d == typeof(DictamenVehiculo))
                    escriben.Add(unidad);
                if (referido is MethodInfo mi && helpers.Contains(mi.IsGenericMethod ? mi.GetGenericMethodDefinition() : mi))
                    llamanHelper.Add(unidad);
                if (referido.DeclaringType is { } declarante && propios.Contains(declarante))
                {
                    var destino = Unidad(referido);
                    if (destino != unidad)
                    {
                        if (!llamadores.TryGetValue(destino, out var set))
                            llamadores[destino] = set = new HashSet<string>(StringComparer.Ordinal);
                        set.Add(unidad);
                    }
                }
            }
        }

        var violaciones = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var escritor in escriben)
            Comprobar(escritor, []);
        return [.. violaciones];

        void Comprobar(string unidad, HashSet<string> visitadas)
        {
            if (llamanHelper.Contains(unidad) || !visitadas.Add(unidad))
                return;
            if (!MutadoresPuros.Contains(unidad))
            {
                violaciones.Add(unidad);
                return;
            }

            if (!llamadores.TryGetValue(unidad, out var quienes) || quienes.Count == 0)
            {
                violaciones.Add(unidad);
                return;
            }

            foreach (var quien in quienes)
                Comprobar(quien, visitadas);
        }
    }

    private static IEnumerable<MethodBase> Metodos(Type tipo) =>
        tipo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Cast<MethodBase>()
            .Concat(tipo.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Concat(tipo.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(Metodos));

    /// <summary>
    /// «Tipo.Método» del código fuente: las lambdas (<c>&lt;M&gt;b__…</c>), funciones locales (<c>&lt;M&gt;g__…</c>) y máquinas de estado
    /// (<c>&lt;M&gt;d__…</c>) cuentan como el método que las contiene, en el primer tipo no generado por el compilador.
    /// </summary>
    private static string Unidad(MethodBase metodo)
    {
        var tipo = metodo.DeclaringType!;
        string? nombre = metodo.Name.StartsWith('<') ? Interno(metodo.Name) : null;
        while (tipo.Name.StartsWith('<') && tipo.DeclaringType is not null)
        {
            if (nombre is null && !tipo.Name.StartsWith("<>", StringComparison.Ordinal))
                nombre = Interno(tipo.Name);
            tipo = tipo.DeclaringType;
        }

        return $"{tipo.Name}.{nombre ?? metodo.Name}";
    }

    private static string Interno(string nombre) => NombreGenerado().Match(nombre) is { Success: true } m ? m.Groups[1].Value : nombre;

    [GeneratedRegex("^<+([^<>]+)>")]
    private static partial Regex NombreGenerado();

    private static readonly Dictionary<short, OpCode> Codigos = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    /// <summary>Métodos y constructores que el IL referencia con <c>call</c>, <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c> o <c>ldvirtftn</c>.</summary>
    private static IEnumerable<MethodBase> Referidos(MethodBase metodo)
    {
        byte[]? il;
        try
        {
            il = metodo.GetMethodBody()?.GetILAsByteArray();
        }
        catch (InvalidOperationException)
        {
            yield break;
        }

        if (il is null)
            yield break;
        var argsTipo = metodo.DeclaringType!.IsGenericType ? metodo.DeclaringType.GetGenericArguments() : null;
        var argsMetodo = metodo.IsGenericMethod ? metodo.GetGenericArguments() : null;
        var i = 0;
        while (i < il.Length)
        {
            short valor = il[i] == 0xFE ? (short)(0xFE00 | il[++i]) : il[i];
            i++;
            var codigo = Codigos[valor];
            switch (codigo.OperandType)
            {
                case OperandType.InlineMethod:
                    var token = BitConverter.ToInt32(il, i);
                    MethodBase? referido = null;
                    try
                    {
                        referido = metodo.Module.ResolveMethod(token, argsTipo, argsMetodo);
                    }
                    catch (ArgumentException)
                    {
                    }

                    if (referido is not null)
                        yield return referido;
                    i += 4;
                    break;
                case OperandType.InlineSwitch:
                    i += 4 + (4 * BitConverter.ToInt32(il, i));
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    i += 1;
                    break;
                case OperandType.InlineVar:
                    i += 2;
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    i += 8;
                    break;
                default:
                    i += 4;
                    break;
            }
        }
    }

    /// <summary>Escritores de fila de mentira para comprobar que la prueba sí detecta uno nuevo.</summary>
    private static class EscritoresNuevos
    {
        public static void FijarTramite(VehiculoDiagnostico vehiculo) => vehiculo.TramiteId = Guid.NewGuid();

        public static void CerrarSinVersion(OperacionDerivacion operacion, ItemDerivacion item, VehiculoDiagnostico vehiculo) =>
            CierreItemDerivacion.Cerrar(operacion, item, vehiculo, new ResultadoBorrador.NoDisponible(Transitorio: false), DateTimeOffset.UtcNow);

        public static void CerrarConVersion(OperacionDerivacion operacion, ItemDerivacion item, VehiculoDiagnostico vehiculo, long version)
        {
            EscritorCeldas.MarcarFila(vehiculo, version);
            CierreItemDerivacion.Cerrar(operacion, item, vehiculo, new ResultadoBorrador.NoDisponible(Transitorio: false), DateTimeOffset.UtcNow);
        }
    }
}
