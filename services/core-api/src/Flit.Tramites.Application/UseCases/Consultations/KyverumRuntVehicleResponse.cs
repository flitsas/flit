using System.Text.Json.Serialization;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// DTOs tipados para deserializar la respuesta de Kyverum RUNT <c>POST /v1/vehiculos:consultar</c>
/// (VIN o placa+documento) — ver <c>context/reference/kyverum-runt/CONTRATO-API.md</c>. Kyverum es la
/// fuente canónica de estas consultas (HU #10478): el mapper converge al mismo
/// <see cref="ConsultationResult"/> que Verifik. Solo se modelan los campos que consume
/// <see cref="KyverumRuntVehicleResultMapper"/>; el resto de la superficie del RUNT se ignora
/// (System.Text.Json descarta propiedades no mapeadas). <c>ok:false</c> se maneja en el cliente HTTP,
/// no aquí.
/// </summary>
public sealed class KyverumRuntVehicleResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public KyverumRuntVehicleData? Data { get; set; }
}

public sealed class KyverumRuntVehicleData
{
    [JsonPropertyName("vehiculo")]
    public KyverumRuntVehiculo? Vehiculo { get; set; }

    // Tipo de documento del propietario que RESUELVE el propio RUNT (C=CC, N=NIT, E=CE, T=TI, P=PAS…).
    // Kyverum lo devuelve aunque no se envíe en la consulta por placa; lo usamos para sembrar el tipo
    // del vendedor en traspaso (HU #10478) sin pedírselo al usuario.
    [JsonPropertyName("tipoDocPropietario")]
    public string? TipoDocPropietario { get; set; }

    [JsonPropertyName("datosTecnicos")]
    public KyverumRuntDatosTecnicos? DatosTecnicos { get; set; }

    [JsonPropertyName("soat")]
    public List<KyverumRuntSoat>? Soat { get; set; }

    [JsonPropertyName("rtm")]
    public List<KyverumRuntRtm>? Rtm { get; set; }

    /// <summary>
    /// Detalle de garantías/prendas del RUNT (acreedor, documento, fecha). Cuando
    /// <c>vehiculo.prendas</c> es <c>SI</c> suele traer al menos un ítem; vacío si no hay.
    /// </summary>
    [JsonPropertyName("garantias")]
    public List<KyverumRuntGarantia>? Garantias { get; set; }

    /// <summary>
    /// Variante adicional de prendas que Kyverum a veces envía aparte de <see cref="Garantias"/>.
    /// Misma forma de ítem; el mapper las une al hidratar <c>runt_gravamenes</c>.
    /// </summary>
    [JsonPropertyName("garantiasPrendas")]
    public List<KyverumRuntGarantia>? GarantiasPrendas { get; set; }
}

/// <summary>
/// Ítem de garantía/prenda en la respuesta Kyverum RUNT. El nombre del acreedor llega como
/// <c>acreedor</c> (no <c>nombreAcreedor</c> como en Intempo); el mapper normaliza al contrato
/// común de <c>runt_gravamenes</c>.
/// </summary>
public sealed class KyverumRuntGarantia
{
    [JsonPropertyName("tipoDocumentoAcreedor")]
    public string? TipoDocumentoAcreedor { get; set; }

    [JsonPropertyName("numeroDocumentoAcreedor")]
    public string? NumeroDocumentoAcreedor { get; set; }

    [JsonPropertyName("acreedor")]
    public string? Acreedor { get; set; }

    [JsonPropertyName("nombreAcreedor")]
    public string? NombreAcreedor { get; set; }

    [JsonPropertyName("fechaInscripcion")]
    public string? FechaInscripcion { get; set; }

    [JsonPropertyName("idPrenda")]
    public long? IdPrenda { get; set; }

    [JsonPropertyName("estadoPrenda")]
    public string? EstadoPrenda { get; set; }
}

public sealed class KyverumRuntVehiculo
{
    [JsonPropertyName("placa")]
    public string? Placa { get; set; }

    [JsonPropertyName("vin")]
    public string? Vin { get; set; }

    [JsonPropertyName("marca")]
    public string? Marca { get; set; }

    [JsonPropertyName("linea")]
    public string? Linea { get; set; }

    [JsonPropertyName("modelo")]
    public string? Modelo { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("clase")]
    public string? Clase { get; set; }

    [JsonPropertyName("tipoCombustible")]
    public string? TipoCombustible { get; set; }

    [JsonPropertyName("numChasis")]
    public string? NumChasis { get; set; }

    [JsonPropertyName("numMotor")]
    public string? NumMotor { get; set; }

    [JsonPropertyName("numSerie")]
    public string? NumSerie { get; set; }

    [JsonPropertyName("cilindraje")]
    public string? Cilindraje { get; set; }

    [JsonPropertyName("tipoServicio")]
    public string? TipoServicio { get; set; }

    [JsonPropertyName("tipoCarroceria")]
    public string? TipoCarroceria { get; set; }

    [JsonPropertyName("pasajerosSentados")]
    public string? PasajerosSentados { get; set; }

    [JsonPropertyName("pesoBruto")]
    public string? PesoBruto { get; set; }

    [JsonPropertyName("numeroEjes")]
    public string? NumeroEjes { get; set; }

    [JsonPropertyName("organismoTransito")]
    public string? OrganismoTransito { get; set; }

    [JsonPropertyName("estadoAutomotor")]
    public string? EstadoAutomotor { get; set; }

    /// <summary>
    /// Fecha de matrícula del vehículo (HU #11303). Kyverum la manda aquí y no en
    /// <c>fechaMatricula</c>, que llega <c>null</c> en las tres consultas capturadas — por eso se creía
    /// que este proveedor no la reportaba.
    /// <para>Es el insumo de la regla de antigüedad de la RTM: sin ella, el bloque de revisión
    /// técnico-mecánica del certificado no puede decidir si le aplica al vehículo.</para>
    /// </summary>
    [JsonPropertyName("fechaRegistro")]
    public string? FechaRegistro { get; set; }

    /// <summary>Variante declarada del RUNT. Llega <c>null</c> en las capturas; se conserva como respaldo.</summary>
    [JsonPropertyName("fechaMatricula")]
    public string? FechaMatricula { get; set; }

    // Señal de gravámenes/prendas: strings "SI"/"NO" en el propio vehículo (igual que el RUNT vía Verifik).
    [JsonPropertyName("gravamenes")]
    public string? Gravamenes { get; set; }

    [JsonPropertyName("prendas")]
    public string? Prendas { get; set; }
}

public sealed class KyverumRuntDatosTecnicos
{
    // Fallback de peso/ejes cuando el bloque vehiculo no los trae.
    [JsonPropertyName("pesoBrutoVehicular")]
    public string? PesoBrutoVehicular { get; set; }

    [JsonPropertyName("noEjes")]
    public string? NoEjes { get; set; }
}

/// <summary>
/// Registro de SOAT de Kyverum.
/// </summary>
/// <remarks>
/// <b>Corrección de HU #11303 (Feature #11301).</b> Hasta esta versión, este tipo modelaba tres campos
/// y afirmaba por escrito que Kyverum «no trae póliza ni fechas de expedición». Las tres consultas
/// reales capturadas lo desmienten: <c>numSoat</c>, <c>fechaExpediSoat</c> y <c>fechaInicioPoliza</c>
/// vienen en <b>las tres</b>. El modelo se había deducido de fixtures y, como el payload crudo no se
/// guardaba en ninguna parte, la afirmación se volvió profecía autocumplida: el campo no se leía, no
/// quedaba rastro de que el proveedor lo mandaba, y las celdas del certificado se atribuían a una
/// carencia del proveedor.
///
/// <para><c>numSoat</c> es <b>string</b> y no numérico: en la placa YNK04A tiene 16 dígitos, por
/// encima de <c>int</c>, y no es un número que se opere.</para>
/// </remarks>
public sealed class KyverumRuntSoat
{
    [JsonPropertyName("estado")]
    public string? Estado { get; set; }

    /// <summary>Número de póliza. 16 dígitos en casos reales ⇒ <b>siempre string</b>.</summary>
    [JsonPropertyName("numSoat")]
    public string? NumSoat { get; set; }

    /// <summary>Fecha de expedición de la póliza.</summary>
    [JsonPropertyName("fechaExpediSoat")]
    public string? FechaExpediSoat { get; set; }

    /// <summary>Variante que Kyverum manda junto a <see cref="FechaExpediSoat"/> con el mismo valor.</summary>
    [JsonPropertyName("fechaExpedicion")]
    public string? FechaExpedicion { get; set; }

    /// <summary>Inicio de vigencia de la póliza (la celda «Vigencia» del certificado).</summary>
    [JsonPropertyName("fechaInicioPoliza")]
    public string? FechaInicioPoliza { get; set; }

    [JsonPropertyName("fechaVencimSoat")]
    public string? FechaVencimSoat { get; set; }

    [JsonPropertyName("razonSocialAsegur")]
    public string? RazonSocialAsegur { get; set; }
}

/// <summary>
/// Revisión técnico-mecánica de Kyverum.
/// </summary>
/// <remarks>
/// <b>Corrección de HU #11303 (Feature #11301).</b> Igual que el SOAT: se afirmaba que este proveedor
/// no trae número de certificado ni fechas de expedición, y las capturas reales traen
/// <c>numeCerti</c>, <c>fechaExpedicionRvt</c>, <c>nombreCda</c> y <c>tipoRevision</c> en las dos
/// consultas que tienen sección RTM.
///
/// <para><c>estadoRvt</c> es informativo y <b>no es vigencia</b>: la placa YNK04A trae cuatro
/// revisiones <c>APROBADA</c>, las cuatro con <c>vigente:"NO"</c>. La vigencia la declara
/// <see cref="Vigente"/>, y el certificado la resuelve por fecha.</para>
///
/// <para><c>nombreCda</c> llega con espacio inicial en al menos una captura real: el dato del RUNT
/// viene sucio y hay que normalizarlo antes de imprimirlo.</para>
/// </remarks>
public sealed class KyverumRuntRtm
{
    // Vigencia de la revisión técnico-mecánica: "SI" / "NO" / "NO APLICA" (mismo dominio que Verifik).
    // OJO: la RTM de Kyverum NO usa "estado"/"fechaVencimiento" (como sí el SOAT), sino "vigente" y
    // "fechaVencimientoRvt". Leer los nombres equivocados dejaba la RTM en null → novedad falsa
    // "RTM no vigente" aunque el RUNT sí traía una revisión vigente.
    [JsonPropertyName("vigente")]
    public string? Vigente { get; set; }

    // Estado del trámite de la RVT ("APROBADA", ...). Informativo — NO es vigencia.
    [JsonPropertyName("estadoRvt")]
    public string? EstadoRvt { get; set; }

    /// <summary>Número del certificado de revisión.</summary>
    [JsonPropertyName("numeCerti")]
    public string? NumeCerti { get; set; }

    [JsonPropertyName("fechaExpedicionRvt")]
    public string? FechaExpedicionRvt { get; set; }

    [JsonPropertyName("fechaVencimientoRvt")]
    public string? FechaVencimientoRvt { get; set; }

    /// <summary>Centro de diagnóstico automotor que expidió la revisión. Llega con espacios sobrantes.</summary>
    [JsonPropertyName("nombreCda")]
    public string? NombreCda { get; set; }

    /// <summary>«REVISION TECNICO-MECANICO», etc. No va al certificado; se guarda para auditar.</summary>
    [JsonPropertyName("tipoRevision")]
    public string? TipoRevision { get; set; }
}
