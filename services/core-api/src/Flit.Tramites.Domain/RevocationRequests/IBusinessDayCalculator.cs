namespace Flit.Tramites.Domain.RevocationRequests;

/// <summary>
/// HU #12571 — cálculo de "días hábiles" para la ventana de revocatoria
/// (<c>admin.transit_office_profiles.revocation_window_business_days</c>, HU #12567).
///
/// <para>
/// No existía ninguna utilidad de días hábiles reutilizable en el repo al escribir esta HU (se buscó
/// por <c>business_day</c>/<c>dias_habiles</c>/<c>BusinessDay</c>/<c>WorkingDay</c> en
/// <c>Flit.Tramites.Domain</c> y <c>Flit.Admin.Domain</c> sin resultados). <see cref="BusinessDayCalculator"/>
/// es una implementación simple y deliberadamente mínima: excluye sábados y domingos, SIN calendario de
/// festivos colombianos (no existe ninguno en el repo y esta HU no introduce una integración externa
/// para traerlo). Si en el futuro se necesita un calendario de festivos, se implementa detrás de esta
/// misma interfaz sin tocar <see cref="RevocationRequestGate"/>.
/// </para>
/// </summary>
public interface IBusinessDayCalculator
{
    /// <summary>
    /// Fecha/hora resultante de sumarle <paramref name="businessDays"/> días hábiles a
    /// <paramref name="start"/>, preservando la hora del día. Es el límite (inclusive) de la ventana:
    /// una solicitud con <c>now &gt; resultado</c> llegó tarde (AC2).
    /// </summary>
    DateTimeOffset AddBusinessDays(DateTimeOffset start, int businessDays);
}
