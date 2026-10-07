using System.Collections;
using System.Reflection;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13304 (M-1) — la lista blanca del snapshot ICT (<see cref="PreflightVehicleSnapshotAllowList"/>) es la
/// unión de lo que emiten los mappers de vehículo. Test de deriva: llena por reflexión TODAS las propiedades
/// de la respuesta de cada proveedor, la pasa por su mapper y exige que cada clave emitida esté en la lista.
/// Si un mapper añade una clave y no se agrega a la lista, este test falla (en vez de descartarla en
/// silencio en producción).
/// <para>Uso de ejemplo: <c>snapshot.SoloClavesDeVehiculo()</c> deja solo FieldKey/Check.Key de vehículo.</para>
/// </summary>
public sealed class PreflightVehicleSnapshotAllowListTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 7);

    public static TheoryData<string> Proveedores => ["kyverum", "verifik", "intempo"];

    private static ConsultationResult Mapear(string proveedor) => proveedor switch
    {
        "kyverum" => KyverumRuntVehicleResultMapper.MapVehicle(Lleno<KyverumRuntVehicleResponse>(), Hoy),
        "verifik" => VerifikResultMapper.MapVehicle(Lleno<VerifikVehicleResponse>(), Hoy),
        "intempo" => IntempoVehicleResultMapper.Map(Lleno<IntempoVehicleResponse>(), Hoy),
        _ => throw new ArgumentOutOfRangeException(nameof(proveedor)),
    };

    [Theory]
    [MemberData(nameof(Proveedores))]
    public void ClavesEmitidasPorElMapper_EstanTodasEnLaListaBlanca(string proveedor)
    {
        var resultado = Mapear(proveedor);

        resultado.HydratedFields.Should().HaveCountGreaterThan(15, "el relleno por reflexión debe ejercitar el mapper");
        resultado.HydratedFields.Select(f => f.FieldKey)
            .Should().OnlyContain(k => PreflightVehicleSnapshotAllowList.FieldKeys.Contains(k));
        resultado.Checks.Select(c => c.Key)
            .Should().OnlyContain(k => PreflightVehicleSnapshotAllowList.CheckKeys.Contains(k));
    }

    [Theory]
    [MemberData(nameof(Proveedores))]
    public void SoloClavesDeVehiculo_NoPierdeNadaDeUnaConsultaReal(string proveedor)
    {
        var original = PreflightVehicleSnapshot.FromConsultation(Mapear(proveedor));

        var filtrado = original.SoloClavesDeVehiculo();

        filtrado.Should().BeEquivalentTo(original, o => o.WithStrictOrdering());
    }

    [Fact]
    public void SoloClavesDeVehiculo_DescartaAjenasYConservaValidasEnOrden()
    {
        var snapshot = new PreflightVehicleSnapshot(
            [
                new PreflightCheckDto("bloqueo_preflight", "Bloqueo", "fail", "system", null),
                new PreflightCheckDto("gravamenes", "Gravámenes", "warn", "kyverum_runt", null),
                new PreflightCheckDto("simit_comprador", "SIMIT", "ok", "kyverum_runt", null),
            ],
            [
                new HydratedField("transit_office_id", "00000000-0000-0000-0000-000000000001", null),
                new HydratedField("plate", "ABC123", null),
                new HydratedField("owner_document_number", "999", null),
                new HydratedField("runt_gravamenes", null, "[]"),
            ],
            ["kyverum_runt"]);

        var filtrado = snapshot.SoloClavesDeVehiculo();

        filtrado.Checks.Select(c => c.Key).Should().Equal("gravamenes");
        filtrado.HydratedFields.Select(f => f.FieldKey).Should().Equal("plate", "runt_gravamenes");
        filtrado.Providers.Should().Equal("kyverum_runt");
    }

    // ── relleno por reflexión: cada string "SI", cada lista con un elemento, cada objeto anidado ──

    private static T Lleno<T>() where T : new() => (T)Llenar(typeof(T), 0)!;

    private static object? Llenar(Type tipo, int nivel)
    {
        if (tipo == typeof(string))
            return "SI";
        if (nivel > 6 || tipo.IsAbstract || tipo.IsInterface && !EsLista(tipo))
            return null;

        if (EsLista(tipo))
        {
            var elemento = tipo.IsArray ? tipo.GetElementType()! : tipo.GetGenericArguments()[0];
            var lista = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elemento))!;
            var valor = Llenar(elemento, nivel + 1);
            if (valor is not null)
                lista.Add(valor);
            return lista;
        }

        if (!tipo.IsClass || tipo.GetConstructor(Type.EmptyTypes) is null)
            return null;

        var instancia = Activator.CreateInstance(tipo)!;
        foreach (var p in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanWrite || p.GetIndexParameters().Length > 0)
                continue;
            var valor = Llenar(p.PropertyType, nivel + 1);
            if (valor is not null && p.PropertyType.IsInstanceOfType(valor))
                p.SetValue(instancia, valor);
        }

        return instancia;
    }

    private static bool EsLista(Type tipo) =>
        tipo.IsArray
        || tipo.IsGenericType
           && tipo.GetGenericArguments().Length == 1
           && typeof(IEnumerable).IsAssignableFrom(tipo)
           && tipo.IsAssignableFrom(typeof(List<>).MakeGenericType(tipo.GetGenericArguments()[0]));
}
