using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Api.Modulos.Configuracion;

/// <summary>
/// Lo que le falta a una empresa para operar (Epic GDC-3). Una sola regla para el Parametrizador (/admin/empresas) y el
/// Inicio del SuperAdmin (/admin/resumen), para que las dos pantallas digan lo mismo.
/// </summary>
public static class FaltantesEmpresa
{
    public static List<string> Calcular(bool monitoreo, EstadoConfiguracionConsultas? consultas, bool derechosPeticion, bool cambioContraventor, bool representante, bool tipologias)
    {
        var faltantes = new List<string>();
        if (!monitoreo)
            faltantes.Add("Sin NIT ni placa monitoreados");
        if (consultas is null or EstadoConfiguracionConsultas.Borrador)
            faltantes.Add("Consultas sin activar");
        if ((derechosPeticion || cambioContraventor) && !representante)
            faltantes.Add("Sin representante legal");
        if (derechosPeticion && !tipologias)
            faltantes.Add("Sin tipologías de DP habilitadas");
        return faltantes;
    }

    /// <summary>Azul activa, gris inactiva (consultas pausadas), naranja pendiente de configuración.</summary>
    public static string Estado(EstadoConfiguracionConsultas? consultas, IReadOnlyCollection<string> faltantes) =>
        consultas == EstadoConfiguracionConsultas.Pausada ? "Inactiva" : faltantes.Count > 0 ? "Pendiente" : "Activa";

    /// <summary>
    /// El correo para respuestas va en el documento del DP y en la solicitud de cambio de contraventor: sin él, esas dos
    /// acciones quedan bloqueadas. Solo aplica si la empresa tiene alguna de las dos capacidades.
    /// </summary>
    public static bool FaltaCorreoRespuesta(bool derechosPeticion, bool cambioContraventor, string? correoRespuesta) =>
        (derechosPeticion || cambioContraventor) && string.IsNullOrWhiteSpace(correoRespuesta);
}
