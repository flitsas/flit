using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// Épica #13216 (HU #13370, ADR-0070 A5.6 R-a) — carga del filtro de «Seleccionar todos», propia de cada
/// origen. El contrato del motor no se tipa con el filtro del listado de <c>/tramites</c>: la bandeja del OT
/// (#13308) aporta su propio filtro (<c>OtBandejaLoteFiltro</c>) y su propio resolver.
/// </summary>
public abstract record LoteFiltro;

/// <summary>
/// Filtro del listado de <c>/tramites</c> (cuerpo de <c>POST /instances/search</c>). <c>TenantId</c>,
/// <c>UsuarioActualId</c>, <c>Skip</c> y <c>Take</c> de <see cref="Criterios"/> se IGNORAN: el tenant y el
/// usuario los pone <see cref="LoteSeleccionContexto"/> (token), y la selección no tiene página.
/// </summary>
public sealed record TramitesLoteFiltro(ProcedureInstanceListRequest Criterios) : LoteFiltro;

/// <summary>Selección del usuario: ids sueltos o «todos los del filtro» menos excluidos.</summary>
public abstract record LoteSeleccion;

/// <summary>Modo <c>ids</c>: casillas marcadas a mano (máximo <see cref="LoteSeleccionTopes.MaxIds"/>).</summary>
public sealed record SeleccionPorIds(IReadOnlyList<Guid> Ids) : LoteSeleccion;

/// <summary>
/// Modo <c>filtro</c>: todos los trámites del filtro activo, sin tope, menos <see cref="Excluidos"/>
/// (máximo <see cref="LoteSeleccionTopes.MaxExcluidos"/>).
/// </summary>
public sealed record SeleccionPorFiltro(LoteFiltro Filtro, IReadOnlyList<Guid>? Excluidos = null) : LoteSeleccion;

/// <summary>
/// Quién resuelve. Sale SIEMPRE del token / middleware, nunca del cuerpo de la petición.
/// <c>TenantId</c> <c>null</c> = todas las compañías: solo el Super Admin (#13383); el guard que falla
/// cerrado para el resto vive en el caso de uso que crea el lote.
/// </summary>
public sealed record LoteSeleccionContexto(Guid? TenantId, Guid? UsuarioActualId);

/// <summary>Topes de la selección (ADR-0070, Q7). El modo filtro no tiene tope de resultados.</summary>
public static class LoteSeleccionTopes
{
    public const int MaxIds = 10_000;
    public const int MaxExcluidos = 10_000;
}

/// <summary>
/// La selección no se puede resolver tal como llegó (tope superado, filtro inválido). Error de validación:
/// la API lo traduce a 422 con <see cref="Exception.Message"/> y <see cref="Codigo"/>.
/// </summary>
public sealed class LoteSeleccionInvalidaException(string codigo, string message)
    : InvalidOperationException(message)
{
    public const string CodigoExcedeTope = "seleccion_excede_tope";
    public const string CodigoFiltroInvalido = "filtro_invalido";

    public string Codigo { get; } = codigo;
}
