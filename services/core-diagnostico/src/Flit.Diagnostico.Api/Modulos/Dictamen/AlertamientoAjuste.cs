namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>Alertamiento pedido en el ajuste FLIT (HU #14563 B7, ADR-0086 D-2): código, texto, efecto y fuente.</summary>
internal sealed record AlertamientoAjuste(string Codigo, string Texto, string Efecto, string? Fuente);
