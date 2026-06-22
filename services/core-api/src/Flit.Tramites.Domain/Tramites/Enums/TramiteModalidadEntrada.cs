namespace Flit.Tramites.Domain.Tramites.Enums;

/// <summary>
/// Modalidad de entrada al wizard de trámites (paridad <c>TramiteModalidadEntrada</c> de Johan).
/// <para>
/// - <see cref="MatriculaInicial"/>: VIN-first, 1 actor (comprador). 5 pasos.
/// - <see cref="Traspaso"/>: placa-first, 2 actores (vendedor + comprador). 6 pasos.
/// </para>
/// </summary>
public enum TramiteModalidadEntrada
{
    MatriculaInicial,
    Traspaso,
}

/// <summary>Códigos canónicos persistibles para <see cref="TramiteModalidadEntrada"/>.</summary>
public static class TramiteModalidadEntradaCodes
{
    public const string MatriculaInicial = "matricula_inicial";
    public const string Traspaso = "traspaso";

    public static string ToCode(TramiteModalidadEntrada modalidad) => modalidad switch
    {
        TramiteModalidadEntrada.MatriculaInicial => MatriculaInicial,
        TramiteModalidadEntrada.Traspaso => Traspaso,
        _ => throw new ArgumentOutOfRangeException(nameof(modalidad), modalidad, null),
    };

    public static TramiteModalidadEntrada? FromCode(string? code) => code switch
    {
        MatriculaInicial => TramiteModalidadEntrada.MatriculaInicial,
        Traspaso => TramiteModalidadEntrada.Traspaso,
        _ => null,
    };
}
