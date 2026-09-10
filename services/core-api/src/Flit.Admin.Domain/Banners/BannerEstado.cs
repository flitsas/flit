namespace Flit.Admin.Domain.Banners;

/// <summary>
/// Estado calculado del banner (HU #12239, AC2). Se deriva en cada lectura a partir de
/// <c>is_active</c> y <c>valid_from</c>/<c>valid_until</c> comparados contra el instante actual —
/// nunca se persiste, porque cambia con el paso del tiempo sin que nadie edite la fila.
///
/// Precedencia (mutuamente excluyentes, se evalúa en este orden):
/// 1. <see cref="Inactivo"/> — <c>is_active = false</c>. Es el interruptor manual del
///    administrador (AC3) y domina sobre cualquier estado temporal: un banner desactivado no
///    debe mostrarse como "Expirado" ni "Programado".
/// 2. <see cref="Expirado"/> — <c>valid_until</c> ya pasó. Hecho temporal absoluto, independiente
///    de si el administrador olvidó desactivarlo.
/// 3. <see cref="Programado"/> — <c>valid_from</c> aún no llega.
/// 4. <see cref="Activo"/> — dentro de vigencia, o sin fechas programadas.
/// </summary>
public enum BannerEstado
{
    Programado,
    Activo,
    Inactivo,
    Expirado,
}
