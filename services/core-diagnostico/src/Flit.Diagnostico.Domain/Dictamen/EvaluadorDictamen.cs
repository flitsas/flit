namespace Flit.Diagnostico.Domain.Dictamen;

/// <summary>Regla que disparó. El monto en pesos solo viene en <c>IMPUESTO</c>; en el resto es null.</summary>
public sealed record ReglaEvaluada(string Codigo, string Fuente, string Efecto, long? MontoPesos);

/// <summary>Veredicto <c>VIABLE</c> o <c>BLOQUEADO</c> y las reglas que dispararon, en el orden del catálogo.</summary>
public sealed record DictamenEvaluado(string Veredicto, IReadOnlyList<ReglaEvaluada> Reglas);

/// <summary>
/// Motor del catálogo vigente (<see cref="CatalogoReglasDictamen.Version"/>). No redacta, no persiste y no lee celdas.
/// Una regla <c>BLOQUEA</c> deja el veredicto en <c>BLOQUEADO</c>. Si solo hay <c>ADVIERTE</c>, o ninguna dispara, el
/// veredicto es <c>VIABLE</c>. Los dictámenes ya emitidos con otra versión no se reevalúan.
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
                "El catálogo de dictamen solo cubre la familia TRASPASO.");
        }

        var reglas = new List<ReglaEvaluada>();
        var runt = LecturaRunt(hechos);
        if (runt is not null)
            AgregarCobertura(hechos, runt, reglas);

        var leasing = runt?.EsLeasing == true;
        AgregarSimit(hechos, runt, leasing, reglas);
        if (runt is not null)
            AgregarPropiedad(hechos, runt, reglas);
        AgregarLicencia(hechos, leasing, reglas);
        if (PredicadosDictamen.ImpuestoConDeuda(hechos))
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

    /// <summary>
    /// SOAT y RTM por clase (ola 2026-11-11, V4/V5): el remolque no exige SOAT y la maquinaria solo advierte
    /// (<c>SOAT_MAQUINARIA</c>); la RTM se exige desde los años de <see cref="ClasificadorVehiculoDictamen.AniosPrimeraRtm"/>
    /// (nunca en maquinaria ni remolque) o sin fecha de matrícula. El efecto de cada regla sale del catálogo del tipo.
    /// </summary>
    private static void AgregarCobertura(HechosDictamen hechos, HechosRuntVehiculo runt, List<ReglaEvaluada> reglas)
    {
        if (!PredicadosDictamen.Cubre(runt.EstadosSoat))
        {
            if (runt.Clase == ClaseVehiculoDictamen.Maquinaria)
                reglas.Add(Emitir(hechos, "SOAT_MAQUINARIA", "RUNT"));
            else if (runt.Clase != ClaseVehiculoDictamen.Remolque)
                reglas.Add(Emitir(hechos, "SOAT_NO_VIGENTE", "RUNT"));
        }

        if (PredicadosDictamen.RtmNoVigente(runt, hechos.Hoy))
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

    /// <summary>
    /// SIMIT del catálogo v2. Se exigen el SIMIT del vehículo y el del propietario o, en leasing, el del locatario. Las
    /// multas (comparendos o sin paz y salvo) disparan la regla de cada fuente; la falta de una consulta exigida da un
    /// solo <c>SIMIT_SIN_VERIFICAR</c> con la primera fuente que falta. Licencia y acuerdo de pago son reglas aparte.
    /// </summary>
    private static void AgregarSimit(HechosDictamen hechos, HechosRuntVehiculo? runt, bool leasing, List<ReglaEvaluada> reglas)
    {
        var leidas = new List<(HechosSimit Simit, string Fuente)>(3);
        string? sinVerificar = null;

        if (hechos.EstadoSimitVehiculo == EstadoFuenteDictamen.Disponible)
            leidas.Add((hechos.SimitVehiculo ?? throw Lectura("SIMIT del vehículo"), "SIMIT_VEHICULO"));
        else
            sinVerificar = "SIMIT_VEHICULO";

        if (hechos.EstadoSimitPropietario == EstadoFuenteDictamen.Disponible && hechos.SimitPropietario is null)
            throw Lectura("SIMIT del propietario");
        if (!leasing)
        {
            if (hechos.EstadoSimitPropietario == EstadoFuenteDictamen.Disponible)
                leidas.Add((hechos.SimitPropietario!, "SIMIT_PROPIETARIO"));
            else
                sinVerificar ??= "SIMIT_PROPIETARIO";
        }

        if (leasing && (!runt!.HayDocumentoLocatario || hechos.SimitLocatario is null))
            sinVerificar ??= "SIMIT_LOCATARIO";
        else if (runt?.HayDocumentoLocatario == true && hechos.SimitLocatario is { } locatario)
            leidas.Add((locatario, "SIMIT_LOCATARIO"));

        foreach (var (simit, fuente) in leidas)
        {
            if (PredicadosDictamen.TieneMultas(simit))
                reglas.Add(Emitir(hechos, fuente, fuente));
        }

        if (sinVerificar is not null)
            reglas.Add(Emitir(hechos, "SIMIT_SIN_VERIFICAR", sinVerificar));

        foreach (var (simit, fuente) in leidas)
        {
            if (simit.Suspendida || simit.Cancelada)
                reglas.Add(Emitir(hechos, "SIMIT_LICENCIA", fuente));
        }

        foreach (var (simit, fuente) in leidas)
        {
            if (PredicadosDictamen.TieneAcuerdoConSaldo(simit))
                reglas.Add(Emitir(hechos, "SIMIT_ACUERDO_PAGO", fuente));
        }
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
        // Desde v2 el SIMIT faltante no es FUENTE_PENDIENTE: lo cubre SIMIT_SIN_VERIFICAR en AgregarSimit.
        Agregar(hechos.EstadoRunt, "RUNT");
        Agregar(hechos.EstadoRuntPropietario, "RUNT_PROPIETARIO");
        if (hechos.EstadoImpuesto == EstadoFuenteDictamen.Error)
            reglas.Add(Emitir(hechos, "FUENTE_PENDIENTE", "IMPUESTO_DEPARTAMENTAL"));

        void Agregar(EstadoFuenteDictamen estado, string fuente)
        {
            if (estado != EstadoFuenteDictamen.Disponible)
                reglas.Add(Emitir(hechos, "FUENTE_PENDIENTE", fuente));
        }
    }

    private static bool EstadoDispara(string? estado)
    {
        var normalizado = Normalizar(estado);
        return normalizado is not ("REGISTRADO" or "ACTIVO");
    }

    private static bool LicenciaDispara(LicenciaConductor licencia)
    {
        var normalizado = Normalizar(licencia.Estado);
        var mala = normalizado is "SUSPENDIDA" or "SUSPENDIDO" or "VENCIDA" or "VENCIDO" or "CANCELADA" or "CANCELADO";
        return mala || !licencia.CertificadoAptitudVigente;
    }

    private static string Normalizar(string? valor) => PredicadosDictamen.Normalizar(valor);

    private static ReglaEvaluada Emitir(HechosDictamen hechos, string codigo, string fuente, long? monto = null)
    {
        var fila = CatalogoReglasDictamen.Filas.Single(f =>
            f.TipoTramiteCodigo == hechos.TipoTramiteCodigo && f.Codigo == codigo);
        return new ReglaEvaluada(fila.Codigo, fuente, fila.Efecto, monto);
    }

    private static ArgumentException Lectura(string fuente) =>
        new($"La fuente {fuente} está disponible y no trae lectura.");
}
