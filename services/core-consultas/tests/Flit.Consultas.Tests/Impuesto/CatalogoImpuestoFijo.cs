using Flit.Consultas.Api.Impuesto;

namespace Flit.Consultas.Tests.Impuesto;

internal sealed class CatalogoImpuestoFijo : ICatalogoDepartamentosImpuesto
{
    private readonly string[] _codigos;

    public CatalogoImpuestoFijo(params string[] codigos) => _codigos = codigos;

    public Task<IReadOnlyList<DepartamentoHabilitado>> ActivosAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DepartamentoHabilitado>>(_codigos.Select(c => new DepartamentoHabilitado(c, c, true)).ToArray());

    public Task<bool> ActivoAsync(string? codigo, CancellationToken ct) =>
        Task.FromResult(codigo is not null && _codigos.Contains(codigo, StringComparer.Ordinal));
}
