using Flit.Diagnostico.Domain.Dictamen;

namespace Flit.Diagnostico.Domain.Saneamiento;

/// <summary>Fuente con hallazgo: un renglón automático.</summary>
public sealed record HallazgoFuente(FuenteSaneamiento Fuente, HallazgoSaneamiento Hallazgo);

/// <summary>
/// Generación de renglones al tomar el saneamiento por primera vez (plan §3.3, S11, brief C4). Pura, sobre <see cref="HechosDictamen"/>
/// (los arma <c>LectorHechosDictamen</c> para cualquier modo) y con <see cref="PredicadosDictamen"/>, los mismos de las reglas del
/// catálogo; no mira el modo ni el tipo de trámite y nunca llama a <see cref="EvaluadorDictamen.Evaluar"/>, que solo cubre TRASPASO:
/// <list type="bullet">
/// <item>SIMIT del vehículo, del propietario (no en leasing, igual que el evaluador) y del locatario con documento: <c>DEUDA</c> si
/// tiene multas (comparendos o sin paz y salvo) o un acuerdo con saldo. La licencia suspendida o cancelada no es deuda.</item>
/// <item>Leasing sin documento del locatario: <c>SIMIT_LOCATARIO SIN_VERIFICAR</c>.</item>
/// <item>Impuesto con la marca de deuda o monto mayor que cero: <c>DEUDA</c> (los hechos no traen vigencias).</item>
/// <item>SOAT sin cobertura (no el remolque; la maquinaria sí) y RTM exigible sin cobertura: <c>NO_VIGENTE</c>.</item>
/// </list>
/// Una fuente que no está disponible (N/A, error o sin resultado) no genera renglón. Orden: el de <see cref="FuenteSaneamiento"/>.
/// </summary>
/// <example>
/// <code>
/// HallazgosSaneamiento.Generar(hechos); // [HallazgoFuente(Impuesto, Deuda)]
/// </code>
/// </example>
public static class HallazgosSaneamiento
{
    /// <summary>Un hallazgo por fuente, en el orden de <see cref="FuenteSaneamiento"/>; vacío si no hay ninguno.</summary>
    public static IReadOnlyList<HallazgoFuente> Generar(HechosDictamen hechos)
    {
        ArgumentNullException.ThrowIfNull(hechos);
        var hallazgos = new List<HallazgoFuente>();
        var runt = hechos.EstadoRunt == EstadoFuenteDictamen.Disponible ? hechos.Runt : null;
        var leasing = runt?.EsLeasing == true;

        if (hechos.EstadoSimitVehiculo == EstadoFuenteDictamen.Disponible && Deuda(hechos.SimitVehiculo))
            hallazgos.Add(new(FuenteSaneamiento.SimitVehiculo, HallazgoSaneamiento.Deuda));
        if (!leasing && hechos.EstadoSimitPropietario == EstadoFuenteDictamen.Disponible && Deuda(hechos.SimitPropietario))
            hallazgos.Add(new(FuenteSaneamiento.SimitPropietario, HallazgoSaneamiento.Deuda));
        if (PredicadosDictamen.SimitLocatarioSinVerificar(runt))
            hallazgos.Add(new(FuenteSaneamiento.SimitLocatario, HallazgoSaneamiento.SinVerificar));
        else if (runt?.HayDocumentoLocatario == true && Deuda(hechos.SimitLocatario))
            hallazgos.Add(new(FuenteSaneamiento.SimitLocatario, HallazgoSaneamiento.Deuda));
        if (PredicadosDictamen.ImpuestoConDeuda(hechos))
            hallazgos.Add(new(FuenteSaneamiento.Impuesto, HallazgoSaneamiento.Deuda));
        if (runt is not null && PredicadosDictamen.SoatNoVigente(runt))
            hallazgos.Add(new(FuenteSaneamiento.Soat, HallazgoSaneamiento.NoVigente));
        if (runt is not null && PredicadosDictamen.RtmNoVigente(runt, hechos.Hoy))
            hallazgos.Add(new(FuenteSaneamiento.Rtm, HallazgoSaneamiento.NoVigente));
        return hallazgos;
    }

    private static bool Deuda(HechosSimit? simit) =>
        simit is not null && (PredicadosDictamen.TieneMultas(simit) || PredicadosDictamen.TieneAcuerdoConSaldo(simit));
}
