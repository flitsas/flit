namespace Flit.Ict.Domain.Trazabilidad;

/// <summary>Quién pide el revelado. Sale del JWT, nunca del cuerpo de la petición.</summary>
public sealed record SolicitanteRevelado(string Sujeto, string Rol);

/// <summary>
/// Datos personales de un trámite EN CLARO, con la constancia de que se registró el acceso.
/// </summary>
/// <param name="Auditado">
/// Se informa a la pantalla para que pueda decírselo a quien lo pide. No es un adorno: saber que
/// el acceso queda registrado cambia la decisión de pedirlo, y ese es justamente el control.
/// </param>
public sealed record DatosPersonalesRevelados(
    long Numero,
    IReadOnlyList<SeccionDatos> Secciones,
    bool Auditado);

/// <summary>
/// Revelado auditado de los datos personales de un trámite (HU #11820).
/// </summary>
/// <remarks>
/// Es la ÚNICA pieza del Feature que escribe. Todo lo demás es de solo lectura. Si el negocio
/// decidiera mantener el enmascarado permanente, esta interfaz y su endpoint se retiran sin tocar
/// las otras cinco historias.
/// </remarks>
public interface IRevelarDatosPersonalesQuery
{
    /// <summary>Null cuando el trámite no existe o es de otro tenant.</summary>
    Task<DatosPersonalesRevelados?> RevelarAsync(
        long numero, Guid? tenantId, SolicitanteRevelado solicitante, CancellationToken ct = default);
}

/// <summary>Permiso necesario para ver los datos personales en claro.</summary>
public static class PermisoRevelado
{
    /// <summary>
    /// Es un permiso PROPIO y no <c>ict.logs.read</c>.
    /// </summary>
    /// <remarks>
    /// Reutilizar el permiso del módulo dejaría el revelado al alcance de cualquiera que pueda abrir
    /// la pantalla, y entonces el enmascarado no protegería de nada. Al ser un código nuevo, hoy no
    /// lo tiene nadie salvo el SuperAdmin: el valor por defecto es «cerrado», que es el correcto
    /// para un acceso a datos personales, y abrirlo es una decisión explícita de quien administra
    /// los roles.
    /// </remarks>
    public const string Codigo = "ict.pii.reveal";
}
