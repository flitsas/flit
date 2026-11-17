using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Evento de auditoría de cada cambio de configuración hecho desde el backoffice (ajustes 2026-11-11, punto 1 AC7). Desde
/// ese punto el SuperAdmin de cualquier compañía edita la configuración de las demás, así que además del usuario (que ya
/// guardan <c>cambios_contrato.usuario_id</c> y <c>definiciones_fuentes_obligatorias.actualizado_por</c>) queda la compañía
/// del actor y la compañía objetivo. Solo ids (seudónimos) y el nombre de la sección; nunca valores ni datos de personas.
/// Evento <see cref="Evento"/> con los valores estructurados <c>Seccion</c>, <c>UsuarioId</c>, <c>EmpresaActor</c>,
/// <c>EmpresaObjetivo</c> (null en el catálogo global de fuentes obligatorias) y <c>Campos</c>.
/// </summary>
internal static partial class AuditoriaConfiguracionBackoffice
{
    public const string Evento = "CambioConfiguracionBackoffice";
    public const string SeccionContrato = "contrato";
    public const string SeccionFuentesObligatorias = "fuentes_obligatorias";

    public static void Registrar(ILogger logger, UsuarioActual usuario, string seccion, Guid? empresaObjetivo, IEnumerable<string> campos)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(usuario);
        if (!logger.IsEnabled(LogLevel.Information))
            return;

        var lista = string.Join(',', campos);
        var autor = usuario.Id;
        var empresaActor = usuario.EmpresaId;
        LogCambio(logger, seccion, autor, empresaActor, empresaObjetivo, lista);
    }

    [LoggerMessage(EventId = 141111, EventName = Evento, Level = LogLevel.Information,
        Message = "Cambio de configuración del backoffice: sección {Seccion}, usuario {UsuarioId}, compañía del actor {EmpresaActor}, compañía objetivo {EmpresaObjetivo}, campos {Campos}.")]
    private static partial void LogCambio(ILogger logger, string seccion, Guid usuarioId, Guid? empresaActor, Guid? empresaObjetivo, string campos);
}
