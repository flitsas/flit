namespace Flit.Diagnostico.Domain.Dictamen;

/// <summary>
/// Predicados de deuda y vigencia del catálogo de dictamen (brief C3, HU #14576), públicos para que el saneamiento use LOS MISMOS
/// que <see cref="EvaluadorDictamen"/> sin pasar por él (el evaluador solo cubre la familia TRASPASO). Extracción sin cambio de
/// comportamiento: el evaluador los llama. Puros.
/// </summary>
public static class PredicadosDictamen
{
    /// <summary>Multas del catálogo v2: comparendos o sin paz y salvo (reglas <c>SIMIT_VEHICULO/PROPIETARIO/LOCATARIO</c>).</summary>
    public static bool TieneMultas(HechosSimit simit)
    {
        ArgumentNullException.ThrowIfNull(simit);
        if (simit.Acuerdos is null)
            throw new ArgumentException("Los acuerdos de SIMIT no pueden ser nulos.", nameof(simit));
        return simit.CantidadComparendos > 0 || simit.PazYSalvo == false;
    }

    /// <summary>Algún acuerdo de pago con saldo distinto de cero (regla <c>SIMIT_ACUERDO_PAGO</c>).</summary>
    public static bool TieneAcuerdoConSaldo(HechosSimit simit)
    {
        ArgumentNullException.ThrowIfNull(simit);
        return simit.Acuerdos.Any(a => a.SaldoPendientePesos != 0);
    }

    /// <summary>
    /// SOAT sin cobertura en un vehículo que lo exige: dispara <c>SOAT_NO_VIGENTE</c> o, en maquinaria, <c>SOAT_MAQUINARIA</c>. El
    /// remolque no exige SOAT.
    /// </summary>
    public static bool SoatNoVigente(HechosRuntVehiculo runt)
    {
        ArgumentNullException.ThrowIfNull(runt);
        return !Cubre(runt.EstadosSoat) && runt.Clase != ClaseVehiculoDictamen.Remolque;
    }

    /// <summary>RTM exigible por clase y servicio (o sin fecha de matrícula) y sin cobertura (regla <c>RTM_NO_VIGENTE</c>).</summary>
    public static bool RtmNoVigente(HechosRuntVehiculo runt, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(runt);
        return ClasificadorVehiculoDictamen.AniosPrimeraRtm(runt.Clase, runt.Servicio) is { } anios
            && (runt.FechaMatricula is null || hoy >= runt.FechaMatricula.Value.AddYears(anios))
            && !Cubre(runt.EstadosRtm);
    }

    /// <summary>Deuda del impuesto: la marca de deuda o un monto mayor que cero (regla <c>IMPUESTO</c>).</summary>
    public static bool ImpuestoConDeuda(HechosDictamen hechos)
    {
        ArgumentNullException.ThrowIfNull(hechos);
        return hechos.ImpuestoTieneDeuda || hechos.ImpuestoMontoPesos is > 0;
    }

    /// <summary>Leasing sin documento del locatario: su SIMIT no se pudo consultar (<c>SIMIT_SIN_VERIFICAR</c> del locatario).</summary>
    public static bool SimitLocatarioSinVerificar(HechosRuntVehiculo? runt) => runt is { EsLeasing: true, HayDocumentoLocatario: false };

    /// <summary>Alguna póliza o revisión en VIGENTE o NO REQUIERE (sin distinguir mayúsculas ni guion bajo).</summary>
    public static bool Cubre(IReadOnlyList<string?> estados)
    {
        ArgumentNullException.ThrowIfNull(estados);
        return estados.Any(estado => Normalizar(estado) is "VIGENTE" or "NO REQUIERE");
    }

    internal static string Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? string.Empty : valor.Trim().Replace('_', ' ').ToUpperInvariant();
}
