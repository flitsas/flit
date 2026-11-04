namespace Flit.Diagnostico.Domain.Dictamen;

/// <summary>Regla que disparó. El monto en pesos solo viene en <c>IMPUESTO</c>; en el resto es null.</summary>
public sealed record ReglaEvaluada(string Codigo, string Fuente, string Efecto, long? MontoPesos);

/// <summary>Veredicto <c>VIABLE</c> o <c>BLOQUEADO</c> y las reglas que dispararon, en el orden del catálogo.</summary>
public sealed record DictamenEvaluado(string Veredicto, IReadOnlyList<ReglaEvaluada> Reglas);

/// <summary>
/// Motor del catálogo v1. No redacta, no persiste y no lee celdas. Una regla <c>BLOQUEA</c> deja el veredicto en
/// <c>BLOQUEADO</c>. Si solo hay <c>ADVIERTE</c>, o ninguna dispara, el veredicto es <c>VIABLE</c>.
/// </summary>
public static class EvaluadorDictamen
{
    /// <summary>Evalúa los hechos contra el catálogo del tipo de trámite.</summary>
    public static DictamenEvaluado Evaluar(HechosDictamen hechos)
    {
        ArgumentNullException.ThrowIfNull(hechos);
        if (!CatalogoReglasDictamen.TiposTramite.Contains(hechos.TipoTramiteCodigo, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(hechos),
                hechos.TipoTramiteCodigo,
                "El catálogo v1 solo cubre la familia TRASPASO.");
        }

        var reglas = new List<ReglaEvaluada>();
        var runt = LecturaRunt(hechos);
        if (runt is not null)
            AgregarCobertura(hechos, runt, reglas);

        if (hechos.EstadoSimitVehiculo == EstadoFuenteDictamen.Disponible)
        {
            var simit = hechos.SimitVehiculo ?? throw Lectura("SIMIT del vehículo");
            if (DisparaSimit(simit))
                reglas.Add(Emitir(hechos, "SIMIT_VEHICULO", "SIMIT_VEHICULO"));
        }

        var leasing = runt?.EsLeasing == true;
        if (hechos.EstadoSimitPropietario == EstadoFuenteDictamen.Disponible && !leasing)
        {
            var simit = hechos.SimitPropietario ?? throw Lectura("SIMIT del propietario");
            if (DisparaSimit(simit))
                reglas.Add(Emitir(hechos, "SIMIT_PROPIETARIO", "SIMIT_PROPIETARIO"));
        }
        else if (hechos.EstadoSimitPropietario == EstadoFuenteDictamen.Disponible && hechos.SimitPropietario is null)
        {
            throw Lectura("SIMIT del propietario");
        }

        AgregarLocatario(hechos, runt, reglas);
        if (runt is not null)
            AgregarPropiedad(hechos, runt, reglas);
        AgregarLicencia(hechos, leasing, reglas);
        if (hechos.ImpuestoTieneDeuda || hechos.ImpuestoMontoPesos is > 0)
            reglas.Add(Emitir(hechos, "IMPUESTO", "IMPUESTO_DEPARTAMENTAL", hechos.ImpuestoMontoPesos is > 0 ? hechos.ImpuestoMontoPesos : null));
        AgregarFuentes(hechos, reglas);

        var veredicto = reglas.Exists(r => r.Efecto == "BLOQUEA") ? "BLOQUEADO" : "VIABLE";
        return new DictamenEvaluado(veredicto, reglas);
    }

    private static HechosRuntVehiculo? LecturaRunt(HechosDictamen hechos)
    {
        if (hechos.EstadoRunt != EstadoFuenteDictamen.Disponible)
            return null;
        var runt = hechos.Runt ?? throw Lectura("RUNT del vehículo");
        if (runt.EstadosSoat is null || runt.EstadosRtm is null)
            throw new ArgumentException("Las listas de SOAT y de RTM no pueden ser nulas.", nameof(hechos));
        return runt;
    }

    private static void AgregarCobertura(HechosDictamen hechos, HechosRuntVehiculo runt, List<ReglaEvaluada> reglas)
    {
        if (!Cubre(runt.EstadosSoat))
            reglas.Add(Emitir(hechos, "SOAT_NO_VIGENTE", "RUNT"));
        if (EvaluaRtm(runt.FechaMatricula, hechos.Hoy) && !Cubre(runt.EstadosRtm))
            reglas.Add(Emitir(hechos, "RTM_NO_VIGENTE", "RUNT"));
        if (EstadoDispara(runt.EstadoVehiculo))
            reglas.Add(Emitir(hechos, "ESTADO_VEHICULO", "RUNT"));
    }

    private static void AgregarPropiedad(HechosDictamen hechos, HechosRuntVehiculo runt, List<ReglaEvaluada> reglas)
    {
        if (runt.Limitaciones > 0)
            reglas.Add(Emitir(hechos, "LIMITACION", "RUNT"));
        if (runt.TieneGravamenes)
            reglas.Add(Emitir(hechos, "GRAVAMEN", "RUNT"));
        if (runt.GarantiasMobiliarias > 0)
            reglas.Add(Emitir(hechos, "GARANTIA_MOBILIARIA", "RUNT"));
        if (runt.TienePrenda)
            reglas.Add(Emitir(hechos, "PRENDA", "RUNT"));
        if (runt.Infracciones > 0)
            reglas.Add(Emitir(hechos, "INFRACCION_RUNT", "RUNT"));
    }

    private static void AgregarLocatario(HechosDictamen hechos, HechosRuntVehiculo? runt, List<ReglaEvaluada> reglas)
    {
        if (runt is null)
            return;
        if (runt.EsLeasing && !runt.HayDocumentoLocatario)
        {
            reglas.Add(Emitir(hechos, "SIMIT_LOCATARIO", "SIMIT_LOCATARIO"));
            return;
        }

        if (runt.HayDocumentoLocatario && hechos.SimitLocatario is { } simit && DisparaSimit(simit))
            reglas.Add(Emitir(hechos, "SIMIT_LOCATARIO", "SIMIT_LOCATARIO"));
    }

    private static void AgregarLicencia(HechosDictamen hechos, bool leasing, List<ReglaEvaluada> reglas)
    {
        if (hechos.LicenciaPropietario is { } propietario && LicenciaDispara(propietario))
        {
            reglas.Add(Emitir(hechos, "LICENCIA_CONDUCTOR", "RUNT_PROPIETARIO"));
            return;
        }

        if (leasing && hechos.LicenciaLocatario is { } locatario && LicenciaDispara(locatario))
            reglas.Add(Emitir(hechos, "LICENCIA_CONDUCTOR", "RUNT_LOCATARIO"));
    }

    private static void AgregarFuentes(HechosDictamen hechos, List<ReglaEvaluada> reglas)
    {
        Agregar(hechos.EstadoRunt, "RUNT");
        Agregar(hechos.EstadoSimitVehiculo, "SIMIT_VEHICULO");
        Agregar(hechos.EstadoRuntPropietario, "RUNT_PROPIETARIO");
        Agregar(hechos.EstadoSimitPropietario, "SIMIT_PROPIETARIO");
        if (hechos.EstadoImpuesto == EstadoFuenteDictamen.Error)
            reglas.Add(Emitir(hechos, "FUENTE_PENDIENTE", "IMPUESTO_DEPARTAMENTAL"));

        void Agregar(EstadoFuenteDictamen estado, string fuente)
        {
            if (estado != EstadoFuenteDictamen.Disponible)
                reglas.Add(Emitir(hechos, "FUENTE_PENDIENTE", fuente));
        }
    }

    private static bool Cubre(IReadOnlyList<string?> estados) =>
        estados.Any(estado => Normalizar(estado) is "VIGENTE" or "NO REQUIERE");

    private static bool EvaluaRtm(DateOnly? fechaMatricula, DateOnly hoy) =>
        fechaMatricula is null || hoy >= fechaMatricula.Value.AddYears(5);

    private static bool EstadoDispara(string? estado)
    {
        var normalizado = Normalizar(estado);
        return normalizado is not ("REGISTRADO" or "ACTIVO");
    }

    private static bool DisparaSimit(HechosSimit simit)
    {
        if (simit.Acuerdos is null)
            throw new ArgumentException("Los acuerdos de SIMIT no pueden ser nulos.", nameof(simit));
        return simit.CantidadComparendos > 0
            || simit.PazYSalvo == false
            || simit.Suspendida
            || simit.Cancelada
            || simit.Acuerdos.Any(a => a.SaldoPendientePesos != 0);
    }

    private static bool LicenciaDispara(LicenciaConductor licencia)
    {
        var normalizado = Normalizar(licencia.Estado);
        var mala = normalizado is "SUSPENDIDA" or "SUSPENDIDO" or "VENCIDA" or "VENCIDO" or "CANCELADA" or "CANCELADO";
        return mala || !licencia.CertificadoAptitudVigente;
    }

    private static string Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? string.Empty : valor.Trim().Replace('_', ' ').ToUpperInvariant();

    private static ReglaEvaluada Emitir(HechosDictamen hechos, string codigo, string fuente, long? monto = null)
    {
        var fila = CatalogoReglasDictamen.Filas.Single(f =>
            f.TipoTramiteCodigo == hechos.TipoTramiteCodigo && f.Codigo == codigo);
        return new ReglaEvaluada(fila.Codigo, fuente, fila.Efecto, monto);
    }

    private static ArgumentException Lectura(string fuente) =>
        new($"La fuente {fuente} está disponible y no trae lectura.");
}
