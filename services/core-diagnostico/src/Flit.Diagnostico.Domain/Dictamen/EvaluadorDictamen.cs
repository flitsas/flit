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
            if (TieneMultas(simit))
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
            if (simit.Acuerdos.Any(a => a.SaldoPendientePesos != 0))
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

    private static bool Cubre(IReadOnlyList<string?> estados) =>
        estados.Any(estado => Normalizar(estado) is "VIGENTE" or "NO REQUIERE");

    private static bool EvaluaRtm(DateOnly? fechaMatricula, DateOnly hoy) =>
        fechaMatricula is null || hoy >= fechaMatricula.Value.AddYears(5);

    private static bool EstadoDispara(string? estado)
    {
        var normalizado = Normalizar(estado);
        return normalizado is not ("REGISTRADO" or "ACTIVO");
    }

    /// <summary>Multas del catálogo v2: comparendos o sin paz y salvo. Licencia y acuerdo de pago van aparte.</summary>
    private static bool TieneMultas(HechosSimit simit)
    {
        if (simit.Acuerdos is null)
            throw new ArgumentException("Los acuerdos de SIMIT no pueden ser nulos.", nameof(simit));
        return simit.CantidadComparendos > 0 || simit.PazYSalvo == false;
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
