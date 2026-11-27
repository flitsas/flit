using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #14743 (Épica #14503) — el listado del gestor trae el dígito de preferencia de placa, la marca de
/// declarado, la clase y la marca de Ruta Corta, derivados del grafo que el listado ya carga.
/// </summary>
public sealed class PreferenciaPlacaListadoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private static ProcedureInstance Instancia(ProcedureType tipo, params (string Clave, string Valor, string Fuente)[] campos)
    {
        var instancia = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            ProcedureType = tipo,
            TenantId = Guid.NewGuid(),
            ReferenceNumber = "17",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var (clave, valor, fuente) in campos)
            instancia.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(), FieldKey = clave, ValueText = valor, Source = fuente,
            });
        return instancia;
    }

    private async Task<IReadOnlyList<InstanceSummaryDto>> ListarAsync(params ProcedureInstance[] instancias)
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), ct)
            .Returns(((IReadOnlyList<ProcedureInstance>)instancias, instancias.Length));
        var (items, _) = await new ListProcedureInstancesFilteredHandler(_repo)
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = Guid.NewGuid() }, ct);
        return items;
    }

    [Fact] // AC1 — ruta larga con dígito
    public async Task RutaLargaConDigito_TraeDigitoDeclaradoYClase()
    {
        var fila = (await ListarAsync(Instancia(ProcedureTypeFixture.Matricula,
            ("plate_preferred_last_digit", "7", "user"),
            ("plate_preferred_last_digit_declared", "true", "user"),
            ("vehicle_class", "AUTOMOVIL", "consultation")))).Single();

        fila.PlateDigitoPreferido.Should().Be("7");
        fila.PlateDigitoDeclarado.Should().BeTrue();
        fila.Clase.Should().Be("AUTOMOVIL");
        fila.PlacaRutaCorta.Should().BeFalse();
    }

    [Fact] // AC2 — sin preferencia declarada: dígito vacío + declarado
    public async Task SinPreferenciaDeclarada_DigitoNuloYDeclaradoTrue()
    {
        var fila = (await ListarAsync(Instancia(ProcedureTypeFixture.Matricula,
            ("plate_preferred_last_digit", "", "user"),
            ("plate_preferred_last_digit_declared", "true", "user")))).Single();

        fila.PlateDigitoPreferido.Should().BeNull();
        fila.PlateDigitoDeclarado.Should().BeTrue();
    }

    [Fact] // AC3 — ruta corta aunque luego tenga placa
    public async Task RutaCorta_PlacaDeConsultaMarcaRutaCorta_PlacaDelOtNo()
    {
        var filas = await ListarAsync(
            Instancia(ProcedureTypeFixture.Matricula, ("plate", "ABC123", "consultation")),
            Instancia(ProcedureTypeFixture.Matricula, ("plate", "XYZ987", "ot")),
            Instancia(ProcedureTypeFixture.Matricula));

        filas[0].PlacaRutaCorta.Should().BeTrue();
        filas[1].PlacaRutaCorta.Should().BeFalse();
        filas[2].PlacaRutaCorta.Should().BeFalse();
    }

    [Fact] // AC4 — otros trámites: todo nulo
    public async Task OtrosTramites_TodoNulo()
    {
        var fila = (await ListarAsync(Instancia(ProcedureTypeFixture.Traspaso,
            ("plate", "ABC123", "consultation"),
            ("plate_preferred_last_digit", "7", "user"),
            ("plate_preferred_last_digit_declared", "true", "user"),
            ("vehicle_class", "AUTOMOVIL", "consultation")))).Single();

        fila.PlateDigitoPreferido.Should().BeNull();
        fila.PlateDigitoDeclarado.Should().BeNull();
        fila.Clase.Should().BeNull();
        fila.PlacaRutaCorta.Should().BeNull();
    }

    [Fact] // AC5 — una sola consulta de listado para N filas, sin lecturas adicionales por fila
    public async Task UnaSolaConsultaParaTodasLasFilas()
    {
        var ct = TestContext.Current.CancellationToken;
        var instancias = Enumerable.Range(0, 25)
            .Select(_ => Instancia(ProcedureTypeFixture.Matricula, ("plate_preferred_last_digit", "3", "user")))
            .ToArray();

        var filas = await ListarAsync(instancias);

        filas.Should().HaveCount(25).And.OnlyContain(f => f.PlateDigitoPreferido == "3");
        await _repo.Received(1).ListWithSummaryGraphFilteredAsync(
            Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ProcedureInstanceListFilter>(),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), ct);
    }
}
