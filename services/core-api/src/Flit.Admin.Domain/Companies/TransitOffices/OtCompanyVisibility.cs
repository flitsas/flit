namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Bug #12912 (Ley 1581, decisión del usuario) — qué compañías puede ver, por nombre, quien consulta
/// las compañías de un organismo de tránsito.
/// </summary>
public enum OtCompanyVisibility
{
    /// <summary>
    /// Toda compañía que puede radicar en el OT según la lista efectiva de red (HU #12347): vista de
    /// configuración de la plataforma y de la propia compañía.
    /// </summary>
    WholeNetwork = 0,

    /// <summary>
    /// Vista del propio organismo: compañías con grant directo habilitado respaldado por la lista
    /// efectiva, más las que entran solo por la red y ya le entregaron algún trámite.
    /// </summary>
    DirectOrWithReceivedProcedures = 1,
}
