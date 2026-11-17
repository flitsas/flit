namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>Códigos estables de la consulta y el override del dictamen (corte 7). La app y las pruebas los leen.</summary>
internal static class CodigosDictamen
{
    public const string ConfirmadoRequerido = "CONFIRMADO_REQUERIDO";

    public const string VeredictoInvalido = "VEREDICTO_INVALIDO";

    public const string SinDictamen = "SIN_DICTAMEN";

    public const string VeredictoSinCambio = "VEREDICTO_SIN_CAMBIO";

    public const string DictamenEnConflicto = "DICTAMEN_EN_CONFLICTO";

    /// <summary>B7 (D9): cambio de veredicto o cuerpo nuevo sin motivo de 10 a 500 caracteres.</summary>
    public const string MotivoRequerido = "MOTIVO_REQUERIDO";

    /// <summary>B7: la <c>version_esperada</c> ya no es la vigente.</summary>
    public const string DictamenDesactualizado = "DICTAMEN_DESACTUALIZADO";

    /// <summary>B7: <c>version_esperada</c> no es un entero mayor que cero, o falta en un ajuste con alertamientos.</summary>
    public const string VersionEsperadaInvalida = "VERSION_ESPERADA_INVALIDA";

    /// <summary>B7 (P3 = b): un alertamiento <c>AJUSTE_FLIT</c> con una secuencia de 6 o más dígitos.</summary>
    public const string AlertamientoConDigitos = "ALERTAMIENTO_CON_DIGITOS";

    /// <summary>B7: VIABLE con un alertamiento que bloquea, o BLOQUEADO sin ninguno.</summary>
    public const string DictamenIncoherente = "DICTAMEN_INCOHERENTE";

    /// <summary>B7: alertamiento con código fuera del catálogo, efecto o fuente fuera del enum, o texto vacío o de más de 500.</summary>
    public const string AlertamientoInvalido = "ALERTAMIENTO_INVALIDO";

    /// <summary>B7 (ADR-0084): el vehículo ya tiene trámite y el ajuste no trae <c>confirmar_con_tramite</c>.</summary>
    public const string VehiculoConTramite = "VEHICULO_CON_TRAMITE";

    /// <summary>B7: código del alertamiento que redacta soporte FLIT (visible a la compañía).</summary>
    public const string AjusteFlit = "AJUSTE_FLIT";
}
