namespace Flit.Admin.Domain.Banners;

/// <summary>
/// Estado calculado del banner (HU #12239, AC2). Se deriva en cada lectura a partir de
/// <c>is_active</c> y <c>valid_from</c>/<c>valid_until</c> comparados contra el instante actual —
/// nunca se persiste, porque cambia con el paso del tiempo sin que nadie edite la fila.
///
/// Precedencia (mutuamente excluyentes, se evalúa en este orden):
/// 1. Si hay vigencia programada (<c>valid_from</c> y/o <c>valid_until</c> no nulos), la
///    programación manda sobre <c>is_active</c>: <see cref="Expirado"/> si ya pasó
///    <c>valid_until</c>, <see cref="Programado"/> si aún no llega <c>valid_from</c>, o
///    <see cref="Activo"/> dentro del intervalo — en los tres casos sin mirar el flag manual.
/// 2. <see cref="Inactivo"/> — solo aplica cuando NO hay vigencia programada (ambas fechas
///    nulas) y <c>is_active = false</c>. El interruptor manual del administrador (AC3) gobierna
///    el estado únicamente en ausencia de fechas.
/// 3. <see cref="Activo"/> — sin fechas programadas e <c>is_active = true</c>.
///
/// Decisión de producto (Bug #12584, 2026-09-15): invierte la precedencia original de HU #12239
/// AC2 ("is_active=false domina sobre cualquier estado temporal"). No es un defecto del código
/// original — la HU #12239 se implementó tal como se acordó entonces; es un cambio de criterio
/// explícito del PO tras la certificación de QA del Feature #12236, documentado para que quede
/// trazabilidad de que no cuenta como bug de desarrollo.
/// </summary>
public enum BannerEstado
{
    Programado,
    Activo,
    Inactivo,
    Expirado,
}
