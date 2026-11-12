using System.Net;
using System.Net.Http.Json;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Derivacion.Bus;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 3 (diseño §5.3 puntos 1, 2 y 4, ADR-0084) — el crédito por compañía: <see cref="CreditoDerivacion.Libres"/>
/// (lo que se puede liberar con lo que ya hay en vuelo), el orden de liberación de <see cref="DespachadorCredito.Siguientes"/> (la
/// operación más antigua primero, luego el orden de la selección, solo de la compañía dada y solo PENDIENTE) y
/// <see cref="DespachadorCredito.Marcar"/>. La misma consulta corre en memoria aquí y contra Postgres en la última prueba, que
/// encola dos selecciones a la vez en la misma compañía y comprueba que el candado por compañía no deja pasar del crédito.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DespachadorCreditoTests
/// </code>
/// </example>
public sealed class DespachadorCreditoTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(2, 0, 2)]
    [InlineData(2, 1, 1)]
    [InlineData(2, 2, 0)]
    [InlineData(2, 3, 0)]
    [InlineData(1, 0, 1)]
    public void Libres_es_el_credito_menos_lo_que_hay_en_vuelo_y_nunca_negativo(int credito, int enVuelo, int libres) =>
        CreditoDerivacion.Libres(credito, enVuelo).Should().Be(libres);

    [Fact]
    public void Siguientes_toma_la_operacion_mas_antigua_primero_luego_el_orden_y_solo_pendientes_de_la_compania()
    {
        var empresa = Guid.NewGuid();
        var otra = Guid.NewGuid();
        var vieja = Operacion(empresa, T0);
        var nueva = Operacion(empresa, T0.AddMinutes(1));
        var ajena = Operacion(otra, T0.AddMinutes(-5));
        var items = new List<ItemDerivacion>
        {
            Item(nueva, 1, EstadoItemDerivacion.Pendiente),
            Item(vieja, 3, EstadoItemDerivacion.Pendiente),
            Item(vieja, 1, EstadoItemDerivacion.Enviado),
            Item(vieja, 2, EstadoItemDerivacion.Pendiente),
            Item(vieja, 4, EstadoItemDerivacion.Creado),
            Item(ajena, 1, EstadoItemDerivacion.Pendiente),
        };

        var orden = DespachadorCredito.Siguientes(items.AsQueryable(), new[] { vieja, nueva, ajena }.AsQueryable(), empresa).ToList();

        orden.Select(i => (i.OperacionId, i.Orden)).Should().Equal((vieja.Id, 2), (vieja.Id, 3), (nueva.Id, 1));
    }

    [Fact]
    public void Marcar_deja_el_item_en_vuelo_desde_ahora_sin_espera()
    {
        var item = Item(Operacion(Guid.NewGuid(), T0), 1, EstadoItemDerivacion.Pendiente);
        item.DisponibleDesde = T0.AddMinutes(-1);

        DespachadorCredito.Marcar(item, T0.AddSeconds(5));

        item.Estado.Should().Be(EstadoItemDerivacion.Enviado);
        item.EnviadoEn.Should().Be(T0.AddSeconds(5));
        item.ActualizadoEn.Should().Be(T0.AddSeconds(5));
        item.DisponibleDesde.Should().BeNull();
    }

    [Fact]
    public async Task Encolados_concurrentes_de_la_misma_compania_nunca_superan_el_credito()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso, extra: new() { [$"{OpcionesDerivacion.Seccion}:CreditoPorCompania"] = "2" });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var uno = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [.. Enumerable.Range(0, 10).Select(i => ($"TST6{i:00}", true, (string?)"VIABLE"))]);
        var dos = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [.. Enumerable.Range(10, 10).Select(i => ($"TST6{i:00}", true, (string?)"VIABLE"))]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));

        var respuestas = await Task.WhenAll(
            cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(uno.Id), new { vehiculo_ids = uno.Vehiculos.Select(v => v.Id) }, ct),
            cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(dos.Id), new { vehiculo_ids = dos.Vehiculos.Select(v => v.Id) }, ct));

        respuestas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Accepted);
        await using var db = empresa.Db();
        (await db.Set<ItemDerivacion>().CountAsync(i => i.Estado == EstadoItemDerivacion.Enviado, ct)).Should().Be(2, "el candado por compañía no deja pasar del crédito");
        (await db.Set<ItemDerivacion>().CountAsync(i => i.Estado == EstadoItemDerivacion.Pendiente, ct)).Should().Be(18);
        (await DerivacionDePrueba.MensajesAsync(empresa, ct)).Should().HaveCount(2, "un mensaje por ítem liberado");
        falso.Llamadas.Should().BeEmpty("encolar no llama a Trámites");
    }

    internal static OperacionDerivacion Operacion(Guid empresa, DateTimeOffset creada) =>
        OperacionDerivacion.Masiva(empresa, Guid.NewGuid(), "TRASPASO_STANDARD", Guid.NewGuid(), total: 10, creada);

    internal static ItemDerivacion Item(OperacionDerivacion operacion, int orden, EstadoItemDerivacion estado)
    {
        var item = ItemDerivacion.Pendiente(operacion, Guid.NewGuid(), orden, T0);
        item.Estado = estado;
        return item;
    }

    /// <summary>Uso: el evento solo lleva ids (diseño §8).</summary>
    internal static DerivacionSolicitada Evento(ItemDerivacion item) => new(item.OperacionId, item.Id, item.VehiculoId);
}
