namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13375 (Épica #13216, CF-16) — revalidación del acceso del solicitante antes de procesar cada ítem.
/// Devuelve <c>false</c> (⇒ <c>acceso_revocado</c>) si (a) el solicitante ya no está activo; (b) no conserva
/// una membresía activa en la compañía congelada con un rol activo que otorgue
/// <see cref="ConsolidadoLotePermisos.Descargar"/> (el Super Admin con rol activo pasa); (c) el trámite tiene
/// borrado lógico o no es de la compañía congelada. La parte (a)+(b) se cachea 60 s por lote.
/// </summary>
/// <remarks>
/// Atiende <c>tramites</c>, <c>superadmin</c> y, para el solicitante, <c>ot_bandeja</c> (#13392: membresía y rol en el
/// tenant OT del lote, o Super Admin activo); en <c>ot_bandeja</c> el trámite lo revalida el procesador OT con la regla
/// de la bandeja.
/// Uso de ejemplo: <c>var ok = await checker.TieneAccesoAsync(LoteItemContexto.Desde(lote, item), ct);</c>.
/// </remarks>
public interface IConsolidadoLoteAccessChecker
{
    /// <exception cref="NotSupportedException">Origen que no atiende.</exception>
    Task<bool> TieneAccesoAsync(LoteItemContexto contexto, CancellationToken ct = default);
}

/// <summary>Slug RBAC del lote de descarga masiva (sembrado por la HU #13369).</summary>
public static class ConsolidadoLotePermisos
{
    public const string Descargar = "consolidado-masivo.download";
}
