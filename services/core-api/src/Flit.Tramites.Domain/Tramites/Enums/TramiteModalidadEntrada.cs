namespace Flit.Tramites.Domain.Tramites.Enums;

/// <summary>
/// Modalidad de entrada al wizard de trámites (paridad <c>TramiteModalidadEntrada</c> de Johan).
/// <para>
/// - <see cref="MatriculaInicial"/>: VIN-first, 1 actor (comprador). 5 pasos.
/// - <see cref="Traspaso"/>: placa-first, 2 actores (vendedor + comprador). 6 pasos.
/// - <see cref="TraspasoUnilateral"/>: placa-first, 2 partes (arrendadora que transfiere y valida
///   identidad + locatario documental). Modalidad de primera clase del traspaso unilateral de leasing
///   (HU #10590); la familia del <c>procedure_type</c> sigue siendo <c>TRASPASO</c>. 5 pasos.
/// </para>
/// </summary>
public enum TramiteModalidadEntrada
{
    MatriculaInicial,
    Traspaso,
    TraspasoUnilateral,
}

/// <summary>Códigos canónicos persistibles para <see cref="TramiteModalidadEntrada"/>.</summary>
public static class TramiteModalidadEntradaCodes
{
    public const string MatriculaInicial = "matricula_inicial";
    public const string Traspaso = "traspaso";
    public const string TraspasoUnilateral = "traspaso_unilateral";

    public static string ToCode(TramiteModalidadEntrada modalidad) => modalidad switch
    {
        TramiteModalidadEntrada.MatriculaInicial => MatriculaInicial,
        TramiteModalidadEntrada.Traspaso => Traspaso,
        TramiteModalidadEntrada.TraspasoUnilateral => TraspasoUnilateral,
        _ => throw new ArgumentOutOfRangeException(nameof(modalidad), modalidad, null),
    };

    public static TramiteModalidadEntrada? FromCode(string? code) => code switch
    {
        MatriculaInicial => TramiteModalidadEntrada.MatriculaInicial,
        Traspaso => TramiteModalidadEntrada.Traspaso,
        TraspasoUnilateral => TramiteModalidadEntrada.TraspasoUnilateral,
        _ => null,
    };
}
