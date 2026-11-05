using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Configuración global de las consultas (<c>Diagnostico:Consultas</c>, HU #13737, D-12): igual para todas las compañías, no
/// por compañía. <see cref="MaxReintentos"/> son los reintentos automáticos de errores transitorios por celda, en
/// [<see cref="PoliticaReintentos.Minimo"/>, <see cref="PoliticaReintentos.Maximo"/>] (por defecto
/// <see cref="PoliticaReintentos.PorDefecto"/>); fuera de rango el servicio no arranca.
/// </summary>
public sealed class OpcionesConsultas
{
    public const string Seccion = "Diagnostico:Consultas";

    /// <summary>Mensaje de arranque con un valor fuera de rango (indica el rango permitido).</summary>
    public static readonly string MensajeFueraDeRango =
        $"{Seccion}:{nameof(MaxReintentos)} debe estar en el rango [{PoliticaReintentos.Minimo}, {PoliticaReintentos.Maximo}] (D-12).";

    public int MaxReintentos { get; set; } = PoliticaReintentos.PorDefecto;

    /// <summary>Verdadero si <see cref="MaxReintentos"/> está en el rango permitido.</summary>
    public bool EsValida() => MaxReintentos is >= PoliticaReintentos.Minimo and <= PoliticaReintentos.Maximo;
}
