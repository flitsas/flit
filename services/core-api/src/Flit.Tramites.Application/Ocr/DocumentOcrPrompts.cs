namespace Flit.Tramites.Application.Ocr;

/// <summary>
/// Biblioteca de prompts por tipo de documento para el OCR semántico de trámites. Cada prompt le indica
/// al modelo de visión qué validar, qué rechazar y qué campos devolver en JSON. El bloque
/// "IMPORTANTE — DOCUMENTO MULTIPAGINA" va inline en cada prompt para identificar el subconjunto de
/// páginas del tipo solicitado en PDFs multi-documento. Los cambios son SIEMPRE aditivos: ningún campo
/// ya existente se renombra ni se elimina, porque la persistencia de HU #10975 depende de sus nombres.
/// <para><b>Versionado (Feature #10972):</b> el prompt de <c>soat</c> pasa a <b>v2</b> — HU #10976 le
/// AÑADE <c>fecha_expedicion</c> (el certificado la pide en celda propia y v1 solo daba el inicio de
/// vigencia). El prompt de <c>rtm</c> es nuevo en HU #10977.</para>
/// <para><b>v3 — declaraciones de lote.</b> Medido sobre 22 expedientes reales, <c>aduana</c> perdía
/// el 27 % de sus documentos y acertaba el VIN en 13 de 31. La causa: una Declaración de Importación
/// ampara un lote de 30 a 50 vehículos y el prompt pedía el esquema de uno solo, así que el modelo
/// improvisaba —concatenaba los 50 VIN, enumeraba campos <c>vehiculo_vin_N</c> hasta reventar
/// <c>max_tokens</c>, o elegía el primero de la lista, que casi nunca es el del trámite—. Ahora el
/// prompt reconoce el caso: <c>ampara_multiples_vehiculos</c> y los campos del vehículo VACÍOS, que
/// es preferible a un VIN plausible y falso. Se añade además a <c>factura</c> y <c>aduana</c> la
/// advertencia de confusión de caracteres que <c>impronta</c> ya tenía (esa acertó 35 de 35 VIN), y
/// se sitúa el formato FTH-002 de MinTransporte fuera de <c>aduana</c> en los dos extremos del
/// pipeline, que hasta ahora se contradecían sobre él.</para>
/// </summary>
public static class DocumentOcrPrompts
{
    /// <summary>
    /// Tipos de documento soportados por el endpoint OCR (matrícula: los 4 originales; traspaso:
    /// impronta + soat). HU #10977 añade <c>rtm</c> en ambas modalidades.
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedTipos =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "factura", "aduana", "impronta", "soat", "rtm",
            // HU #11996 — la licencia de transito (tarjeta de propiedad). Es el documento MAS cargado
            // de la plataforma (72.813 cargas medidas en V1) y hasta ahora no tenia ninguna validacion.
            "tarjeta_propiedad",
            // HU #11998 — paz y salvo de impuestos. Presente en 32.514 de los 32.870 traspasos de V1,
            // y con 15 casillas ya configuradas en V2.
            "paz_salvo",
            // HU #11999 — inscripcion de prenda (garantia mobiliaria). Obligatoria en los tres tramites
            // de prenda de V2, y en V1 el 17 % de la casilla era un PDF en blanco reutilizado.
            "inscripcion_prenda",
            // HU #12000 — comprobante de pago. En V1 la casilla contiene dos documentos distintos segun
            // el tramite: el impuesto en matricula y los derechos de transito en traspaso y otros.
            "comprobante_derechos",
            // HU #12001 — contrato de leasing. Solo lo pide MATRICULA_LEASING, que entra por VIN: por eso
            // el vehiculo aun no tiene placa y el prompt tiene prohibido exigirla.
            "contrato_leasing",
            // HU #12030 — certificado de camara de comercio. Cubre a la PERSONA JURIDICA, que hoy no
            // valida nadie: la cedula de la persona natural ya la captura Kyverum.
            "camara_comercio",
            // HU #12037 — Certificado CEPD. No es un documento propio: es la seccion EMISIONES de la
            // ficha de homologacion. Depende del enderezado (HU #12036) para poder leerse.
            "certificado_ambiental",
            // Solo extract de Plataforma → Mandatos; el lote de trámites NO lo solicita.
            "mandato_config",
        };

    /// <summary>true si <paramref name="tipo"/> tiene prompt OCR asociado.</summary>
    public static bool IsSupported(string? tipo) => tipo is not null && SupportedTipos.Contains(tipo);

    /// <summary>
    /// HU #12034 — los tipos DOCUMENTALES de trámite con OCR, que es <see cref="SupportedTipos"/> sin
    /// <c>mandato_config</c>: ese no es un documento del expediente sino el extractor de plantillas de
    /// Plataforma → Mandatos, y ofrecérselo al wizard sería ofrecer una casilla que no existe.
    /// <para>Es la fuente de verdad que consume el frontend por <c>GET /api/v1/tramites/ocr/tipos</c>,
    /// en lugar de la lista por modalidad que mantenía a mano. Antes, encender el OCR de un documento
    /// exigía tocar cuatro sitios y desplegar, y si alguien lo asignaba a un trámite de la modalidad
    /// «equivocada» el análisis no corría y no había error en ninguna parte.</para>
    /// </summary>
    public static IReadOnlyCollection<string> TiposDocumentales { get; } =
        SupportedTipos.Where(t => t != "mandato_config").OrderBy(t => t, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// HU #12036 — sonda de orientación. Deliberadamente NO pide leer nada: solo mirar. Y pide un
    /// binario, no una dirección, porque preguntar hacia qué lado estaba girada acertó 3 de 4 veces
    /// mientras que «¿está derecha?» sale fiable. Quien corrige prueba los giros y vuelve a preguntar.
    /// No entra en <see cref="SupportedTipos"/> ni en <see cref="PromptFor"/>: no es un documento.
    /// </summary>
    public const string OrientationProbePrompt =
"""
Mira esta pagina. NO extraigas datos, NO la transcribas y NO intentes leer su contenido.
Lo unico que tienes que decidir es si el texto esta DERECHO: es decir, si se lee en horizontal de
izquierda a derecha tal como te la estoy mostrando, SIN necesidad de girar la pagina.
Si para leerla habria que girarla —en cualquier sentido, 90 o 180 grados— entonces NO esta derecha.
Una pagina sin texto, o en la que no distingas ninguna palabra, cuenta como derecha.
JSON valido sin markdown:
{"derecha":true}
""";

    /// <summary>Devuelve el prompt del tipo, o null si el tipo no está soportado.</summary>
    public static string? PromptFor(string tipo) => tipo switch
    {
        "factura" => Factura,
        "aduana" => Aduana,
        "impronta" => Impronta,
        "soat" => Soat,
        "rtm" => Rtm,
        "tarjeta_propiedad" => TarjetaPropiedad,
        "paz_salvo" => PazSalvo,
        "inscripcion_prenda" => InscripcionPrenda,
        "comprobante_derechos" => ComprobanteDerechos,
        "contrato_leasing" => ContratoLeasing,
        "camara_comercio" => CamaraComercio,
        "certificado_ambiental" => CertificadoAmbiental,
        "mandato_config" => MandatoConfig,
        _ => null,
    };

    /// <summary>
    /// Prompt de CLASIFICACIÓN del cargue masivo. A diferencia de los prompts por tipo (que son
    /// dirigidos: "esto es una factura, verifícala"), este es el inverso — recibe un documento
    /// cualquiera y decide QUÉ hay en cada página. Es la única pieza del OCR que no sabe de antemano
    /// qué está mirando, y por eso corre en el modelo fuerte y una sola vez por archivo: de su mapa
    /// <c>tipo → páginas</c> salen los recortes que después verifica el prompt por tipo de arriba.
    /// <para><paramref name="tipos"/> son los tipos que el trámite espera (varían por modalidad:
    /// traspaso no lleva factura ni aduana), así el modelo no propone tipos que nadie va a recibir.</para>
    /// </summary>
    public static string ClassificationPrompt(IEnumerable<string> tipos)
    {
        var solicitados = string.Join(", ", tipos.Where(IsSupported));
        return $$"""
Analiza este documento PDF o imagen. Puede contener UN solo documento, o VARIOS documentos distintos
concatenados en un mismo archivo (un expediente completo). Tu tarea es decir QUÉ documento hay en CADA
página, y agrupar las páginas que forman cada documento.

TIPOS QUE DEBES IDENTIFICAR (y SOLO estos): {{solicitados}}

Cómo reconocer cada tipo:
- factura: FACTURA ELECTRONICA DE VENTA, factura de venta, cuenta de cobro o documento equivalente por la
  compraventa del vehiculo. Lleva numero de factura, CUFE o resolucion DIAN, emisor con NIT, comprador,
  descripcion del vehiculo y valores (subtotal, IVA, total).
- aduana: DECLARACION DE IMPORTACION (formulario DIAN/MUISCA), manifiesto de importacion, certificado de
  homologacion o licencia de importacion. Lleva numero de declaracion, subpartida arancelaria (8703,
  8704, 8711...), importador o agente de aduana, pais de origen y valores FOB/CIF. Tambien cuenta la
  certificacion de nacionalizacion que expide el importador citando el numero de declaracion —cada uno
  la titula distinto: "Certificacion DIAN", "Certificado de empadronamiento", "Certificado de
  importacion"—, y el
  certificado individual de aduanas de un vehiculo ENSAMBLADO en Colombia bajo regimen de
  transformacion (Sofasa y similares), que sustituye a la declaracion de importacion.
  OJO: una misma declaracion suele amparar un LOTE de 30 a 50 vehiculos y ocupar VARIAS paginas
  seguidas (2 a 5). Agrupalas TODAS en una sola entrada, no una entrada por pagina.
- impronta: CERTIFICADO DE IMPRONTAS, hoja de improntas digitales, acta de improntas o fotoimpronta. Lleva
  los numeros fisicos del vehiculo (motor, chasis, VIN, serie), normalmente en recuadros o calcos.
  OJO: suele ocupar DOS paginas seguidas — la primera con el encabezado y los datos del vehiculo, y la
  siguiente con las fotos o calcos de los numeros. Agrupa AMBAS en una sola entrada: la pagina del
  encabezado sola no permite verificar nada.
  Cuenta tanto el certificado de un CDA como la hoja de "improntas del cliente" que solo trae la foto
  de la placa VIN y los numeros transcritos: ambas son el documento de improntas.
- soat: POLIZA SOAT o certificado de SOAT de una aseguradora colombiana. Lleva numero de poliza,
  aseguradora, vigencia y datos del vehiculo.
- rtm: CERTIFICADO DE REVISION TECNICO-MECANICA Y DE EMISIONES CONTAMINANTES expedido por un CDA. Lleva
  numero de certificado, nombre del CDA, vigencia y resultado.

DOCUMENTOS QUE NO SON NINGUNO DE LOS ANTERIORES:
Los expedientes suelen traer muchas paginas que NO corresponden a ningun tipo solicitado. NO las fuerces
dentro de un tipo: reportalas como no reconocidas. Ejemplos frecuentes:
- Contrato privado de mandato o poder / autorizacion al apoderado
- Solicitud de tramite de forma virtual
- Carta de validacion de identidad, carta selfie, cedula o documento de identidad
- Formulario del Ministerio de Transporte (FUR) y su hoja de instrucciones
- Formato FTH-002 del Ministerio de Transporte ("Caracteristicas tecnico-mecanicas de vehiculos"): es
  la ficha tecnica del vehiculo, NO un documento de aduana. Aunque hable de homologacion, va a
  paginas no reconocidas
- Licencia de transito
- Contrato de compraventa
- Formato o datos de prenda / garantia
- Certificado de paz y salvo de impuestos o de tradicion
- Certificados de consulta al RUNT generados por una plataforma (NO son el SOAT ni el RTM originales:
  son un reporte de consulta, no el certificado expedido por la aseguradora o el CDA)
- Portadas del expediente y hojas sueltas de firmas, hashes y sellos de tiempo. OJO: muchas paginas
  llevan al pie un bloque de "Firma digital impronta" o un hash; eso es la firma electronica de la
  pagina y NO decide su tipo. Clasifica por el contenido principal de la pagina, no por ese bloque.

REGLAS CRITICAS:
1. Una pagina pertenece a UN solo documento. No repitas el mismo numero de pagina en dos entradas.
2. Un documento puede ocupar varias paginas consecutivas: agrupalas en una sola entrada.
3. Un mismo tipo puede aparecer MAS DE UNA VEZ en el archivo (dos facturas, dos improntas). Devuelve una
   entrada por cada aparicion; no las fusiones.
4. Si una pagina esta escaneada, borrosa, torcida o ilegible y no puedes determinar que es, va a
   paginas_no_reconocidas. NO adivines.
5. NO inventes tipos fuera de la lista solicitada.
6. confianza es tu certeza real de 0.0 a 1.0. Si dudas, baja la confianza en vez de omitir el documento.
7. total_paginas es el numero total de paginas del archivo. Para una imagen, es 1.
8. Los numeros de pagina son base 1.
9. No incluyas entradas con paginas vacias. Si un tipo no aparece en el archivo, simplemente no lo
   pongas en documentos.

Devuelve UNICAMENTE este JSON, sin markdown y sin texto adicional:
{"total_paginas":0,"documentos":[{"tipo":"factura","paginas":[1,2],"confianza":0.95,"motivo":"Factura electronica de venta con CUFE y datos del vehiculo"}],"paginas_no_reconocidas":[3,4]}
""";
    }

    private const string Factura =
"""
Analiza este documento. Determina si contiene una FACTURA DE VENTA de vehiculo colombiana.

VALIDACIONES:
1. Debe ser factura de venta, cuenta de cobro o documento equivalente (NO un FUR, NO un formulario de tramite, NO un recibo, NO una cotizacion, NO un certificado)
2. Debe tener datos del emisor (NIT, razon social)
3. Debe tener datos del comprador (nombre, documento)
4. Debe tener descripcion del vehiculo
5. Debe tener valores monetarios (subtotal, IVA, total)

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos (factura + FUR + improntas + etc.), identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el documento solicitado (ej: [1,2] o [3] o [1]). Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro (FUR, mandato,
poder, licencia de transito, declaracion de importacion, factura, escrituras...), NO lo invalida y
NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste
en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del
expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en
false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.

LECTURA DEL VIN Y EL NUMERO DE MOTOR:
Leelos caracter por caracter, exactamente como aparecen. NO completes, NO corrijas, NO deduzcas.
Presta maxima atencion a los caracteres que se confunden: 0 vs O vs D, 1 vs I vs l, 5 vs S, 8 vs B vs
6, 2 vs Z, 6 vs G. El VIN tiene EXACTAMENTE 17 caracteres y nunca contiene las letras I, O ni Q. Si
lees mas o menos de 17, vuelve a mirar.

SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

EXTRAER:
- legibilidad: "buena" | "parcial" | "mala"
- tipo_documento: "factura_electronica" | "factura_venta" | "cuenta_cobro" | "documento_equivalente" | "no_es_factura"
- es_factura_valida: true/false
- paginas_documento: [numeros de pagina donde esta la factura]
- total_paginas: numero total de paginas del PDF
- numero_factura, fecha (YYYY-MM-DD), resolucion_dian
- emisor_nit, emisor_nombre, emisor_direccion, emisor_ciudad
- comprador_nombre, comprador_documento, comprador_tipo_doc (CC/NIT/CE)
- vehiculo_marca, vehiculo_linea, vehiculo_modelo, vehiculo_color, vehiculo_vin, vehiculo_motor, vehiculo_clase, vehiculo_cilindraje, vehiculo_placa
- subtotal (numerico), iva (numerico), total (numerico)
- forma_pago, cufe, observaciones

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"factura_electronica","es_factura_valida":true,"paginas_documento":[1],"total_paginas":1,"numero_factura":"","fecha":"","resolucion_dian":"","emisor_nit":"","emisor_nombre":"","emisor_direccion":"","emisor_ciudad":"","comprador_nombre":"","comprador_documento":"","comprador_tipo_doc":"CC","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","vehiculo_color":"","vehiculo_vin":"","vehiculo_motor":"","vehiculo_clase":"","vehiculo_cilindraje":"","vehiculo_placa":"","subtotal":0,"iva":0,"total":0,"forma_pago":"","cufe":"","observaciones":""}
""";

    private const string Aduana =
"""
Analiza este documento. Determina si contiene un MANIFIESTO DE IMPORTACION, DECLARACION DE IMPORTACION, o CERTIFICADO DE IMPORTACION de vehiculo o motocicleta en Colombia.

VALIDACIONES:
1. DEBE ser uno de estos documentos aduaneros: Declaracion de Importacion (DI) DIAN, Manifiesto de Importacion, Certificado de Homologacion, Licencia de Transito de Importacion (LTI), o documento de aduana equivalente
2. NO es valido si es: una factura de venta, un FUR (Formulario Unico de Registro), un certificado de improntas, una poliza SOAT, un recibo de pago, una cotizacion, un contrato de compraventa
2b. TAMPOCO es valido el FORMATO FTH-002 del Ministerio de Transporte ("CARACTERISTICAS TECNICO-MECANICAS DE VEHICULOS", Direccion de Transporte y Transito). Es una ficha tecnica del vehiculo, no un documento de aduana: no lleva numero de declaracion, ni subpartida arancelaria, ni valores FOB/CIF, ni datos del importador. Si el documento es ese formato, es_valido va en false.
3. DEBE contener datos del importador o agente de aduana (NIT, razon social)
4. DEBE contener descripcion del vehiculo o motocicleta (marca, modelo, VIN o serial)
5. DEBE contener datos de la operacion aduanera (numero declaracion, aduana, pais origen)
6. Para vehiculos nuevos importados (matricula inicial): debe tener referencia al vehiculo especifico con VIN o numero de chasis

TIPOS DE DOCUMENTOS ADUANEROS COLOMBIANOS:
- DECLARACION DE IMPORTACION (DI): Formulario oficial DIAN generado por MUISCA. Tiene numero de declaracion (formato numerico largo), subpartida arancelaria (8710 para vehiculos, 8711 para motos), datos del importador, valores FOB/CIF, tributos (arancel + IVA). Logo DIAN arriba.
- MANIFIESTO DE IMPORTACION: Documento que acompana la mercancia desde puerto hasta zona franca o bodega. Numero de manifiesto, datos del transportador, descripcion de carga.
- CERTIFICADO DE HOMOLOGACION: Documento ICONTEC/NTC que certifica que el vehiculo cumple normas tecnicas colombianas. Codigo NTC, numero certificado.
- LICENCIA DE IMPORTACION: Permiso del MinComercio para importar vehiculos (necesario para algunos tipos). Numero de licencia, vigencia.
- CERTIFICACION DE NACIONALIZACION: la expide el IMPORTADOR (no la DIAN) citando la Resolucion 13292 de diciembre 4 de 2009, y declara que importo y nacionalizo el vehiculo, con su numero de declaracion y su VIN. Cada importador la titula distinto: "CERTIFICACION DIAN", "CERTIFICADO DE EMPADRONAMIENTO", "CERTIFICADO DE IMPORTACION", "CERTIFICACION DE NACIONALIZACION". Todas son el MISMO documento y TODAS son validas: el titulo no decide, lo decide el contenido (importador que certifica + numero de declaracion + vehiculo). Es ademas el documento aduanero que identifica a UN vehiculo concreto, asi que aqui SI se llenan los campos del vehiculo.
- CERTIFICADO INDIVIDUAL DE ADUANAS: para vehiculos ENSAMBLADOS en Colombia bajo regimen de transformacion (Sofasa y similares). Sustituye a la declaracion de importacion y es valido.

DECLARACIONES QUE AMPARAN VARIOS VEHICULOS — REGLA CRITICA:
Una sola Declaracion de Importacion suele amparar UN LOTE de 30 a 50 vehiculos, cada uno con su propio
VIN y su propio numero de motor. Este formulario pide los datos de UN vehiculo, y ese molde NO encaja
con un documento de lote. Cuando la declaracion ampare mas de un vehiculo:
1. Pon ampara_multiples_vehiculos en true y cantidad con el numero de vehiculos.
2. Deja vehiculo_vin, vehiculo_motor y vehiculo_chasis VACIOS (""). NO elijas uno cualquiera de la
   lista, NO uses el primero: no hay forma de saber cual de los 40 es el del tramite, y un VIN
   plausible pero equivocado es peor que un campo vacio, porque se cruza contra el tramite y cuadra
   por accidente.
3. NO concatenes todos los VIN en un mismo campo, NO los separes por comas y NO inventes campos
   numerados como vehiculo_vin_1 o vehiculo_motor_15. La respuesta debe tener EXACTAMENTE los campos
   listados abajo, ni uno mas.
4. Los datos que SI son del lote entero (numero de declaracion, aduana, importador, subpartida, pais
   de origen, valores FOB/CIF, tributos) se extraen normalmente.
Cuando la declaracion ampare un solo vehiculo, ampara_multiples_vehiculos va en false y los campos del
vehiculo se llenan como siempre.

LECTURA DE NUMEROS DE IDENTIFICACION:
Lee el VIN y el numero de motor caracter por caracter, exactamente como aparecen. NO completes, NO
corrijas, NO deduzcas. Presta maxima atencion a los caracteres que se confunden: 0 vs O vs D, 1 vs I
vs l, 5 vs S, 8 vs B vs 6, 2 vs Z, 6 vs G. El VIN tiene EXACTAMENTE 17 caracteres y nunca contiene
las letras I, O ni Q. Si lees mas o menos de 17, vuelve a mirar.

SUBPARTIDAS ARANCELARIAS VEHICULOS COLOMBIA:
- 8703: Automoviles y vehiculos para transporte de personas
- 8704: Vehiculos para transporte de mercancias
- 8711: Motocicletas y ciclomotores
- 8702: Vehiculos para transporte colectivo (buses)
- 8701: Tractores

DATOS A EXTRAER:
- tipo_documento: "declaracion_importacion" | "manifiesto_importacion" | "certificacion_nacionalizacion" | "certificado_individual_aduanas" | "certificado_homologacion" | "licencia_importacion" | "otro"
- es_valido: true/false (false si NO es documento aduanero)
- paginas_documento: [paginas donde esta el documento aduanero]
- total_paginas: total paginas del PDF
- numero_documento: numero de la declaracion, manifiesto o certificado
- fecha: fecha del documento (YYYY-MM-DD)
- aduana: nombre de la aduana (ej: "Aduana de Buenaventura", "Aduana de Cartagena", "Aduana de Bogota")
- importador_nombre: razon social del importador o agente de aduana
- importador_nit: NIT del importador
- importador_direccion: direccion del importador
- importador_ciudad: ciudad del importador
- agente_aduana: nombre del agente de aduana (si aplica)
- agente_aduana_nit: NIT del agente de aduana
- pais_origen: pais de fabricacion del vehiculo (ej: "Estados Unidos", "China", "India", "Japon")
- pais_procedencia: pais desde donde se envio (puede diferir del origen)
- puerto_entrada: puerto de ingreso a Colombia (Buenaventura, Cartagena, Barranquilla, Santa Marta, Bogota)
- subpartida_arancelaria: codigo arancelario (ej: "8703.80.90.00")
- tipo_vehiculo: "automovil" | "motocicleta" | "camioneta" | "bus" | "camion" | "otro"
- vehiculo_marca: marca del vehiculo
- vehiculo_linea: linea o referencia del vehiculo
- vehiculo_modelo: ano del modelo
- vehiculo_vin: VIN (17 caracteres) o numero de chasis
- vehiculo_motor: numero de motor
- vehiculo_chasis: numero de chasis si es diferente del VIN
- vehiculo_cilindraje: cilindraje en cc
- vehiculo_color: color
- vehiculo_clase: clase (automovil, campero, camioneta, motocicleta, etc)
- vehiculo_combustible: tipo de combustible (gasolina, diesel, electrico, hibrido)
- vehiculo_pasajeros: capacidad de pasajeros
- vehiculo_peso_bruto: peso bruto en kg
- cantidad: cantidad de vehiculos que ampara la declaracion
- ampara_multiples_vehiculos: true si la declaracion ampara MAS DE UN vehiculo, false si ampara uno solo
- valor_fob_usd: valor FOB en dolares (numerico)
- valor_flete_usd: valor del flete en dolares (numerico)
- valor_seguro_usd: valor del seguro en dolares (numerico)
- valor_cif_usd: valor CIF en dolares = FOB + flete + seguro (numerico)
- valor_cif_cop: valor CIF en pesos colombianos (numerico)
- tasa_cambio: tasa de cambio USD/COP usada
- arancel_porcentaje: porcentaje de arancel aplicado
- arancel_valor: valor del arancel en COP (numerico)
- iva_porcentaje: porcentaje de IVA importacion (usualmente 19%)
- iva_valor: valor del IVA en COP (numerico)
- total_tributos: total de tributos aduaneros en COP (numerico)
- regimen: tipo de regimen aduanero ("importacion ordinaria", "zona franca", "trafico postal", etc)
- observaciones: notas, restricciones, o informacion adicional relevante

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos (factura + FUR + improntas + etc.), identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el documento solicitado (ej: [1,2] o [3] o [1]). Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro (FUR, mandato,
poder, licencia de transito, declaracion de importacion, factura, escrituras...), NO lo invalida y
NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste
en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del
expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en
false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.


SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"declaracion_importacion","es_valido":true,"paginas_documento":[1],"total_paginas":1,"numero_documento":"","fecha":"","aduana":"","importador_nombre":"","importador_nit":"","importador_direccion":"","importador_ciudad":"","agente_aduana":"","agente_aduana_nit":"","pais_origen":"","pais_procedencia":"","puerto_entrada":"","subpartida_arancelaria":"","tipo_vehiculo":"automovil","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","vehiculo_vin":"","vehiculo_motor":"","vehiculo_chasis":"","vehiculo_cilindraje":"","vehiculo_color":"","vehiculo_clase":"","vehiculo_combustible":"","vehiculo_pasajeros":"","vehiculo_peso_bruto":"","cantidad":1,"ampara_multiples_vehiculos":false,"valor_fob_usd":0,"valor_flete_usd":0,"valor_seguro_usd":0,"valor_cif_usd":0,"valor_cif_cop":0,"tasa_cambio":0,"arancel_porcentaje":0,"arancel_valor":0,"iva_porcentaje":0,"iva_valor":0,"total_tributos":0,"regimen":"","observaciones":""}
""";

    private const string Impronta =
"""
Analiza este documento. Determina si contiene un CERTIFICADO DE IMPRONTAS, HOJA DE IMPRONTAS DIGITALES, o ACTA DE IMPRONTAS de un vehiculo o motocicleta en Colombia.

VALIDACIONES:
1. DEBE ser uno de estos documentos de identificacion vehicular: Certificado de Improntas Digitales, Hoja de Improntas Digitales del Vehiculo, Acta de Improntas, Informe Pericial de Identificacion Vehicular, o Fotoimpronta certificada
2. NO es valido si es: una factura de venta, un FUR (Formulario Unico de Registro), una declaracion de importacion, una poliza SOAT, un certificado de revision tecnico-mecanica RTM (a menos que incluya seccion de improntas), un recibo de pago, un contrato
3. DEBE contener al menos UNO de estos numeros de identificacion del vehiculo: numero de motor, numero de chasis, VIN, o numero de serie
4. DEBE contener datos del vehiculo (marca, modelo como minimo)
5. Origen valido: CDA (Centro de Diagnostico Automotor), VUS (Ventanilla Unica de Servicios),
organismo de transito, DIJIN o entidad certificada. TAMBIEN es valida la "hoja de improntas del
cliente" que genera la propia plataforma de tramites: trae la foto de la placa VIN y los numeros de
motor y chasis transcritos, con hash y sello de tiempo, y NO lleva sello de CDA. Es un documento de
improntas legitimo: es_valido va en true. Que los calcos sean fotografias informales tomadas por el
cliente NO lo invalida.

TIPOS DE DOCUMENTOS DE IMPRONTAS COLOMBIANOS:
- HOJA DE IMPRONTAS DIGITALES: Documento digital moderno conforme Resolucion 17145 de 2023. Tiene secciones coloreadas para cada impronta (rojo=motor, azul=chasis, verde=VIN/serie). Fondo grafito simulando calco fisico. Hash SHA-256 y codigo QR de verificacion. Radicado formato IMPR-XXXXX.
- CERTIFICADO DE IMPRONTAS: Documento oficial de CDA o transito con datos del vehiculo, fotos o calcos de numeros de identificacion, comparacion con RUNT, y resultado (COINCIDE/NO COINCIDE/REGRABADO).
- ACTA DE IMPRONTAS: Formulario preimpreso con espacios para calcos fisicos (papel presionado sobre numeros estampados). Incluye croquis del vehiculo con ubicacion de numeros.
- INFORME PERICIAL DIJIN: Documento forense de la policia judicial para casos sospechosos. Analisis tecnico de autenticidad de estampados.
- FOTOIMPRONTA: Fotografias de alta definicion de los numeros de identificacion. Valida segun Resolucion 5748 de 2016.

NUMEROS DE IDENTIFICACION VEHICULAR:
- NUMERO DE MOTOR: Estampado en el bloque del motor. 8-15 caracteres alfanumericos. Profundidad minima 0.2mm.
- NUMERO DE CHASIS: Estampado en el bastidor/larguero del vehiculo. Puede coincidir con VIN.
- VIN (Vehicle Identification Number): 17 caracteres alfanumericos segun ISO 3779/3780. Posiciones: 1-3=fabricante, 4-8=descriptor, 9=digito verificacion, 10=ano modelo, 11=planta, 12-17=secuencial.
- NUMERO DE SERIE: Identificador adicional unico del fabricante.

CONSIDERACIONES POR TIPO DE VEHICULO:
- AUTOMOVILES: Tienen motor, chasis y VIN/serie. Los 3 deben aparecer.
- MOTOCICLETAS: Tienen motor y chasis/marco. Menos puntos de estampado que autos.
- VEHICULOS ELECTRICOS (Tesla, BYD, etc): NO tienen numero de motor tradicional (motores electricos no llevan estampado fisico). El VIN es el identificador principal. Campo motor puede estar vacio o ser N/A — esto es NORMAL y valido.
- VEHICULOS DE CARGA: Pueden tener estampados adicionales en largueros del chasis.

ESTADOS DE VERIFICACION — ATENCION CRITICA:
- COINCIDE: El numero encontrado coincide EXACTAMENTE caracter por caracter con el registrado
- NO COINCIDE: Cualquier diferencia, incluso un solo digito/letra diferente
- REGRABADO: Evidencia de re-estampado (profundidad irregular, desalineacion, marcas de herramienta)
- ILEGIBLE: Corrosion o dano impide lectura completa
- NO VERIFICADO: El documento no muestra comparacion o no se puede determinar

INSTRUCCIONES CRITICAS DE LECTURA:
1. Lee CADA numero de identificacion (motor, chasis, VIN, serie) EXACTAMENTE como aparece en la impronta/calco/foto. Caracter por caracter, digito por digito. NO asumas, NO completes, NO corrijas.
2. Si el documento muestra DOS versiones del mismo numero (uno en el calco/foto y otro en los datos del vehiculo), COMPARALOS tu mismo caracter por caracter. Si son IDENTICOS = "coincide". Si hay CUALQUIER diferencia (un solo caracter distinto, un digito de mas o de menos, una letra cambiada) = "no_coincide".
3. NO confies ciegamente en lo que dice el estado del documento. Si el documento dice "COINCIDE" pero TU comparacion muestra que los numeros son diferentes = reporta "no_coincide" y agrega alerta.
4. Si el documento solo muestra UN numero (sin comparacion), el estado es "no_verificado".
5. Presta MAXIMA atencion a caracteres que se confunden facilmente: 0 vs O, 1 vs I vs l, 5 vs S, 8 vs B, 2 vs Z, 6 vs G. En VIN nunca hay I, O, Q.
6. El VIN tiene EXACTAMENTE 17 caracteres. Si ves mas o menos, hay un error de lectura.
7. Si el numero en la impronta (calco/grafito/foto) difiere del numero escrito en la seccion de datos del vehiculo del MISMO documento = ALERTA CRITICA, reporta "no_coincide" aunque el documento diga lo contrario.

DATOS A EXTRAER:
- tipo_documento: "hoja_improntas_digitales" | "certificado_improntas" | "acta_improntas" | "informe_pericial" | "fotoimpronta" | "otro"
- es_valido: true/false (false si NO es documento de improntas)
- paginas_documento: [paginas donde estan las improntas]
- total_paginas: total paginas del PDF
- numero_certificado: numero del certificado, radicado o acta (ej: "IMPR-7G9K5XBZQ")
- fecha: fecha del documento (YYYY-MM-DD)
- entidad_emisora: nombre del CDA, VUS, organismo de transito o DIJIN que emite
- entidad_nit: NIT de la entidad emisora
- entidad_ciudad: ciudad de la entidad
- inspector_nombre: nombre del inspector o tecnico que tomo las improntas
- inspector_documento: numero de documento del inspector
- vehiculo_placa: placa del vehiculo
- vehiculo_marca: marca
- vehiculo_linea: linea
- vehiculo_modelo: modelo (ano)
- vehiculo_color: color
- vehiculo_clase: clase (automovil, motocicleta, camioneta, campero, etc)
- vehiculo_servicio: tipo de servicio (particular, publico, oficial)
- vehiculo_vin: VIN leido de la IMPRONTA/CALCO (el numero fisico del vehiculo). EXACTAMENTE como aparece. 17 caracteres.
- vehiculo_vin_datos: VIN que aparece en la seccion de DATOS del documento (si existe). Puede diferir del de la impronta.
- vehiculo_motor: numero de motor leido de la IMPRONTA/CALCO. EXACTAMENTE como aparece. Vacio o "N/A" si es electrico.
- vehiculo_motor_datos: numero de motor en la seccion de DATOS (si existe).
- vehiculo_chasis: numero de chasis leido de la IMPRONTA/CALCO. EXACTAMENTE como aparece.
- vehiculo_chasis_datos: numero de chasis en la seccion de DATOS (si existe).
- vehiculo_serie: numero de serie si es diferente del VIN
- estado_motor: TU propia comparacion caracter por caracter entre vehiculo_motor y vehiculo_motor_datos. "coincide" SOLO si son IDENTICOS. "no_coincide" si hay CUALQUIER diferencia. "no_aplica" si es electrico. "no_verificado" si no hay dos versiones para comparar.
- estado_chasis: misma logica de comparacion propia
- estado_vin: misma logica — compara vehiculo_vin con vehiculo_vin_datos caracter por caracter
- estado_serie: misma logica
- tiene_qr: true/false (si el documento tiene codigo QR de verificacion)
- tiene_hash: true/false (si tiene hash SHA-256 de autenticidad)
- hash_valor: valor del hash si es visible
- resolucion_referencia: resolucion citada (ej: "Resolucion 17145 de 2023")
- alertas: array de alertas detectadas (ej: ["REGRABADO en motor", "Numero chasis ilegible"])
- observaciones: observaciones del inspector o notas del documento

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos (factura + FUR + improntas + etc.), identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el documento solicitado (ej: [1,2] o [3] o [1]). Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro (FUR, mandato,
poder, licencia de transito, declaracion de importacion, factura, escrituras...), NO lo invalida y
NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste
en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del
expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en
false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.


SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"certificado_improntas","es_valido":true,"paginas_documento":[1],"total_paginas":1,"numero_certificado":"","fecha":"","entidad_emisora":"","entidad_nit":"","entidad_ciudad":"","inspector_nombre":"","inspector_documento":"","vehiculo_placa":"","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","vehiculo_color":"","vehiculo_clase":"","vehiculo_servicio":"","vehiculo_vin":"","vehiculo_vin_datos":"","vehiculo_motor":"","vehiculo_motor_datos":"","vehiculo_chasis":"","vehiculo_chasis_datos":"","vehiculo_serie":"","estado_motor":"no_verificado","estado_chasis":"no_verificado","estado_vin":"no_verificado","estado_serie":"no_verificado","tiene_qr":false,"tiene_hash":false,"hash_valor":"","resolucion_referencia":"","alertas":[],"observaciones":""}
""";

    private const string Soat =
"""
Analiza este documento. Determina si contiene una POLIZA SOAT (Seguro Obligatorio de Accidentes de Transito) de Colombia.

VALIDACIONES:
1. DEBE ser una poliza SOAT vigente o certificado de SOAT emitido por una aseguradora colombiana autorizada
2. NO es valido si es: una factura, un FUR, un certificado de improntas, una declaracion de importacion, un recibo de pago, una cotizacion de seguro
3. DEBE contener: numero de poliza, nombre de aseguradora, fechas de vigencia, datos del vehiculo
4. Aseguradoras SOAT validas en Colombia: Seguros del Estado, La Previsora, Suramericana, Liberty, Mapfre, Allianz, Bolivar, AXA Colpatria, Mundial, Solidaria, La Equidad, entre otras

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos (factura + FUR + improntas + etc.), identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el documento solicitado (ej: [1,2] o [3] o [1]). Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro (FUR, mandato,
poder, licencia de transito, declaracion de importacion, factura, escrituras...), NO lo invalida y
NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste
en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del
expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en
false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.

SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

EXTRAER:
- legibilidad: "buena" | "parcial" | "mala"
- tipo_documento: "soat" | "certificado_soat" | "otro"
- es_valido: true/false
- paginas_documento: [paginas], total_paginas: numero
- numero_poliza: numero de la poliza SOAT
- aseguradora: nombre de la aseguradora
- fecha_expedicion: fecha en que se EXPIDIO la poliza (YYYY-MM-DD). Puede ser anterior al inicio de vigencia; si el documento no la muestra, dejar vacia. NO copiar aqui la fecha de inicio de vigencia.
- fecha_inicio: fecha inicio vigencia (YYYY-MM-DD)
- fecha_vencimiento: fecha vencimiento (YYYY-MM-DD)
- estado_poliza: "vigente" | "vencida" | "anulada" | "no_determinado"
- vehiculo_placa, vehiculo_marca, vehiculo_linea, vehiculo_modelo, vehiculo_clase
- vehiculo_vin: VIN si aparece
- tomador_nombre, tomador_documento
- valor_prima: valor de la prima (numerico)
- observaciones

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"soat","es_valido":true,"paginas_documento":[1],"total_paginas":1,"numero_poliza":"","aseguradora":"","fecha_expedicion":"","fecha_inicio":"","fecha_vencimiento":"","estado_poliza":"no_determinado","vehiculo_placa":"","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","vehiculo_clase":"","vehiculo_vin":"","tomador_nombre":"","tomador_documento":"","valor_prima":0,"observaciones":""}
""";

    // HU #10977 (Feature #10972) — prompt NUEVO. El certificado de vigencia SOAT y RTM pide seis datos
    // de la revision y el RUNT solo entrega vencimiento, estado y CDA: numero, expedicion y vigencia
    // no los da ningun proveedor, asi que salen del propio certificado del CDA.
    private const string Rtm =
"""
Analiza este documento. Determina si contiene un CERTIFICADO DE REVISION TECNICO-MECANICA Y DE EMISIONES CONTAMINANTES (RTM) de Colombia.

VALIDACIONES:
1. DEBE ser un certificado de revision tecnico-mecanica y de emisiones contaminantes expedido por un CDA (Centro de Diagnostico Automotor) autorizado
2. NO es valido si es: una poliza SOAT, una factura, un FUR, un certificado de improntas, una declaracion de importacion, una licencia de transito, un recibo de pago
3. DEBE contener: numero de certificado (o numero de runt/consecutivo), nombre del CDA que expide, fechas de vigencia, datos del vehiculo
4. El certificado suele llevar el logo del ONAC / organismo acreditador y un codigo de barras o QR de verificacion

DISTINCION CRITICA ENTRE FECHAS:
- fecha_expedicion: el dia en que se realizo la revision y se expidio el certificado
- fecha_vigencia: el dia en que EMPIEZA a regir (suele coincidir con la expedicion, pero no siempre)
- fecha_vencimiento: el dia en que DEJA de regir (normalmente un ano despues, o dos para vehiculos nuevos)
Si el documento solo muestra una fecha de expedicion y una de vencimiento, deja fecha_vigencia vacia. NO inventes ni deduzcas fechas.

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos (factura + FUR + improntas + etc.), identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el documento solicitado (ej: [1,2] o [3] o [1]). Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro (FUR, mandato,
poder, licencia de transito, declaracion de importacion, factura, escrituras...), NO lo invalida y
NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste
en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del
expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en
false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.

SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

EXTRAER:
- legibilidad: "buena" | "parcial" | "mala"
- tipo_documento: "rtm" | "certificado_rtm" | "otro"
- es_valido: true/false
- paginas_documento: [paginas], total_paginas: numero
- numero_certificado: numero del certificado de revision (o consecutivo RUNT)
- cda_expide: nombre del CDA que expidio la revision
- cda_nit: NIT del CDA si aparece
- fecha_expedicion: fecha de expedicion (YYYY-MM-DD)
- fecha_vigencia: fecha de inicio de vigencia (YYYY-MM-DD), vacia si el documento no la distingue
- fecha_vencimiento: fecha de vencimiento (YYYY-MM-DD)
- estado: "vigente" | "vencida" | "no_determinado"
- resultado: "aprobado" | "rechazado" | "no_determinado"
- vehiculo_placa, vehiculo_marca, vehiculo_linea, vehiculo_modelo, vehiculo_clase
- vehiculo_vin: VIN si aparece
- observaciones

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"rtm","es_valido":true,"paginas_documento":[1],"total_paginas":1,"numero_certificado":"","cda_expide":"","cda_nit":"","fecha_expedicion":"","fecha_vigencia":"","fecha_vencimiento":"","estado":"no_determinado","resultado":"no_determinado","vehiculo_placa":"","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","vehiculo_clase":"","vehiculo_vin":"","observaciones":""}
""";

    /// <summary>
    /// Extract de config de mandato para Plataforma → Mandatos. NO se usa en el lote de trámites.
    /// El PDF subido solo aporta datos; el documento oficial se regenera con diseño FLIT.
    /// </summary>
    /// <summary>
    /// HU #11996 — licencia de transito (tarjeta de propiedad). Calibrado sobre 55 licencias reales de
    /// las 8 secretarias que expiden el documento, mas 9 documentos basura como prueba negativa.
    /// <para><b>El documento es un escaneo:</b> 61 de 64 ejemplares no traen capa de texto, asi que el
    /// trabajo es 100 % de vision. Se llega en tres disposiciones distintas —reverso primero, anverso
    /// primero, o ambas caras en una sola pagina— y por eso el prompt prohibe explicitamente suponer
    /// que la pagina 1 es el anverso.</para>
    /// <para><b>Tres reglas salieron de fallos medidos, no de intuicion.</b> (1) Enunciar "el VIN tiene
    /// 17 caracteres" no bastaba: 7 de 11 VIN erroneos tenian otra longitud, asi que la regla se
    /// convirtio en un procedimiento —"CUENTALOS uno por uno"—. (2) Lo mismo con la placa, que subio de
    /// 88,7 % a 92,7 % al pedir contar caracteres y verificar el patron en vez de solo describirlo; su
    /// error dominante es confundir Q/O/G/D. (3) Endurecer el rechazo de recibos rompio la regla de
    /// alcance y aparecieron 2 falsos rechazos de expedientes que SI traian la tarjeta, asi que la
    /// reconciliacion entre ambas reglas va explicita y en dos pasos.</para>
    /// <para><b>Asteriscos.</b> Un campo con solo asteriscos significa SIN DATO y debe salir vacio
    /// (tipico del motor y la cilindrada en electricos), pero un asterisco dentro del valor es parte
    /// del troquelado y se conserva: "L4F*242904046*" es un numero de motor completo. La BD de V1 ya
    /// fija esa convencion.</para>
    /// <para><b>Medicion (claude-haiku-4-5, max_tokens 2000, con recorte previo):</b> placa 92,7 %,
    /// VIN 92,7 %, chasis 94,5 %, 0 falsos rechazos, 0 falsos positivos sobre los 9 documentos basura,
    /// 0 errores de parseo. Sin el recorte el VIN cae a 75,5 %: ver PdfContentCropper.</para>
    /// </summary>
    private const string TarjetaPropiedad =
"""
Analiza este documento. Determina si contiene una LICENCIA DE TRANSITO (tarjeta de propiedad) de Colombia, expedida por el Ministerio de Transporte.

COMO ES EL DOCUMENTO:
Es una tarjeta plastica del tamano de una cedula, con DOS CARAS, casi siempre ESCANEADA o FOTOGRAFIADA (no tiene texto seleccionable).
- ANVERSO: encabezado "REPUBLICA DE COLOMBIA / MINISTERIO DE TRANSPORTE / LICENCIA DE TRANSITO No." con el escudo a la izquierda y el logo del Ministerio a la derecha. Trae placa, marca, linea, modelo, cilindrada, color, clase, carroceria, combustible, servicio, capacidad, motor, VIN, serie, chasis y propietario.
- REVERSO: restriccion movilidad, blindaje, potencia HP, declaracion de importacion, fecha de importacion, puertas, limitacion a la propiedad, fecha de matricula, fecha de expedicion de la licencia, fecha de vencimiento, ORGANISMO DE TRANSITO, una huella dactilar, un codigo de barras y un serial que empieza por "LT".

VALIDACIONES:
1. DEBE ser la licencia de transito / tarjeta de propiedad del vehiculo. Basta con que aparezca UNA de las dos caras para que sea valida.
2. NO es valido si es: un recibo de pago o factura de derechos de una secretaria de transito (aunque mencione "Especie Venal Lic.Tto.", "COSTO LAMINA LICENCIA DE TRANSITO" o "Licencia de Transito" en el detalle de conceptos, y aunque traiga la placa), el Comprobante Unico de Pago y Liquidacion del RUNT, un FUR, un certificado de improntas, una poliza SOAT, un certificado de revision tecnico-mecanica, una declaracion de importacion, un contrato de compraventa, una cedula, ni una hoja en blanco.
   OJO: que un recibo diga "Licencia de Transito" en la lista de conceptos cobrados NO lo convierte en la licencia. La licencia real es la TARJETA con el encabezado "REPUBLICA DE COLOMBIA / MINISTERIO DE TRANSPORTE" y el numero de 11 digitos.
   PERO ESTO NO SIGNIFICA RECHAZAR EL ARCHIVO. Solo significa que esas paginas de recibo NO van en paginas_documento. Si en ALGUNA OTRA pagina del archivo aparece la tarjeta, el documento ES VALIDO: pon solo las paginas de la tarjeta y sigue adelante. Rechaza unicamente cuando la tarjeta NO este en NINGUNA pagina.
3. Una PAGINA EN BLANCO o un archivo cuyo unico contenido es un logo o la palabra "FLIT" NO es una licencia: es_valido en false.

DISPOSICION DE LAS CARAS — NO ASUMAS EL ORDEN:
Las dos caras aparecen en cualquiera de estas formas, y todas son validas:
- Reverso en la pagina 1 y anverso en la pagina 2 (ES FRECUENTE: no supongas que la pagina 1 es el anverso).
- Anverso en la pagina 1 y reverso en la pagina 2.
- LAS DOS CARAS EN UNA MISMA PAGINA, una encima de la otra.
- Una sola cara, sin la otra.
Identifica cada cara por su CONTENIDO, nunca por su posicion.

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos (recibos de pago + comprobante RUNT + licencia + etc.), identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta la licencia (ej: [4,5] o [1,2] o [1]). Base 1. Incluye AMBAS caras cuando esten en paginas distintas.
- total_paginas: total de paginas del PDF
Si la licencia NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA, PROCEDE EN ESTE ORDEN:
PASO 1. Recorre TODAS las paginas UNA POR UNA y pregunta de cada una: "en ESTA pagina, ¿hay una tarjeta con el encabezado REPUBLICA DE COLOMBIA / MINISTERIO DE TRANSPORTE / LICENCIA DE TRANSITO No.?". Anota los numeros de las paginas donde la respuesta sea SI.
PASO 2. Si anotaste AL MENOS UNA pagina, entonces es_valido = true, paginas_documento = esas paginas, y extrae los datos de ellas. El hecho de que las DEMAS paginas sean recibos, comprobantes del RUNT, FUR o cualquier otra cosa es IRRELEVANTE: no las mires y no dejes que influyan en tu decision. Un expediente de 5 paginas con 3 recibos y la tarjeta en las paginas 4 y 5 es VALIDO, con paginas_documento [4,5].
Solo si NO anotaste NINGUNA pagina en el paso 1, es_valido = false.
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro, NO lo invalida y NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste en paginas_documento, NUNCA al archivo entero.
NUNCA rechaces un archivo por lo que contienen las paginas que NO son la licencia. Ese es el error mas grave que puedes cometer aqui.

ASTERISCOS — REGLA CRITICA, TIENEN DOS SIGNIFICADOS DISTINTOS:
a) Un campo cuyo valor son SOLO asteriscos ("*****", "******") significa SIN DATO / NO APLICA. Devuelve CADENA VACIA "" en ese campo, NUNCA los asteriscos. Es lo normal en Cilindrada y Numero de Motor de vehiculos ELECTRICOS, y en Restriccion Movilidad, Blindaje, Limitacion a la Propiedad y Fecha de Vencimiento.
b) Un asterisco DENTRO de un valor alfanumerico es parte del troquelado y SI se conserva: "L4F*242904046*" y "LJO*18R62820141*" son numeros de motor completos, copialos tal cual con sus asteriscos, incluido el asterisco final si lo lleva.

DOS NUMEROS QUE SE CONFUNDEN — NO LOS MEZCLES:
- numero_licencia: el que sigue a "LICENCIA DE TRANSITO No." en el ANVERSO, arriba a la derecha. Son 11 digitos, empieza por 100 (ej: 10039604989).
- serial_especie_venal: el del REVERSO, debajo del codigo de barras, empieza por las letras "LT" (ej: LT10001101673).

CONFUSION DE CARACTERES — CRITICO PARA VIN, CHASIS, SERIE Y MOTOR:
Estos codigos se leen mal con facilidad, y un solo caracter equivocado invalida el cruce con el tramite. Transcribe caracter por caracter y presta especial atencion a: 0 vs O vs D vs Q, 1 vs I vs L, 5 vs S, 8 vs B, 2 vs Z, 6 vs G, U vs V, 7 vs T.

EL VIN — TRES COMPROBACIONES OBLIGATORIAS ANTES DE RESPONDER:
1. LONGITUD: un VIN tiene EXACTAMENTE 17 caracteres. CUENTALOS uno por uno sobre lo que transcribiste. Si te salen 16, vuelve a mirar: te comiste un caracter. Si te salen 18, duplicaste uno. NO entregues nunca un VIN que no tenga 17.
2. ALFABETO: un VIN NUNCA contiene las letras I, O ni Q. Si crees leer una I es un 1; si crees leer una O o una Q es un 0.
3. COHERENCIA: en la licencia colombiana el VIN, el NUMERO DE SERIE y el NUMERO DE CHASIS son EL MISMO codigo impreso en tres sitios distintos de la tarjeta. Leelos por separado en sus tres casillas y comparalos. Si no te salen identicos, NO elijas uno al azar: vuelve a mirar los tres y quedate con la lectura que cumpla las comprobaciones 1 y 2.
Si tras las tres comprobaciones un codigo sigue sin leerse con seguridad, dejalo VACIO. Es preferible un campo vacio a un valor inventado: un VIN plausible y falso hace pasar un documento que no corresponde al vehiculo.

LA PLACA — TRES COMPROBACIONES OBLIGATORIAS ANTES DE RESPONDER:
1. DE DONDE SALE: del ANVERSO, arriba a la izquierda, en el recuadro rotulado "PLACA". De ningun otro sitio.
2. LONGITUD Y FORMATO: CUENTA los caracteres. Son SIEMPRE EXACTAMENTE 6, sin espacios ni guiones, y con uno de estos dos patrones:
   - automoviles: 3 LETRAS + 3 DIGITOS   (QOU860, NZT090, PRP780)
   - motos:       3 LETRAS + 2 DIGITOS + 1 LETRA
   Si lo que transcribiste tiene 7, 9 o 10 caracteres, o empieza por "LT", o mezcla letras y digitos de otra forma, ESTA MAL: no es la placa. Vuelve a mirar el recuadro "PLACA" del anverso. "LT10099922" y "LIT000992" son el serial del reverso, NO placas.
3. LAS TRES PRIMERAS SON LETRAS, LAS ULTIMAS SON DIGITOS. En estos escaneos las letras Q, O, G, D y C se confunden entre si de forma constante, y es el error MAS FRECUENTE de todos. Antes de dar la placa por buena, mira cada una de las tres letras y decide:
   - Q lleva una colita o rabito que sale del circulo por abajo a la derecha.
   - O es un ovalo cerrado y limpio, sin nada que sobresalga.
   - G lleva una barra horizontal hacia dentro en el lado derecho.
   - D tiene el lado izquierdo COMPLETAMENTE RECTO.
   - C esta abierta por la derecha.
   Si dudas entre Q y O, fijate solo en si hay algo por debajo del circulo.

SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

EXTRAER:
- legibilidad: "buena" | "parcial" | "mala"
- tipo_documento: "licencia_transito" | "recibo_pago" | "comprobante_runt" | "otro"
- es_valido: true/false
- paginas_documento: [paginas], total_paginas: numero
- caras_presentes: "anverso" | "reverso" | "ambas"
- numero_licencia: los 11 digitos que siguen a "LICENCIA DE TRANSITO No."
- vehiculo_placa: la placa (3 letras + 3 digitos, o 3 letras + 2 digitos + 1 letra en motos)
- vehiculo_marca, vehiculo_linea, vehiculo_modelo (ano, 4 digitos)
- vehiculo_cilindrada, vehiculo_color, vehiculo_clase, vehiculo_carroceria, vehiculo_combustible
- vehiculo_servicio: "PARTICULAR" | "PUBLICO" | "OFICIAL" | "DIPLOMATICO" | ""
- vehiculo_capacidad
- vehiculo_motor: numero de motor (con asteriscos si los lleva troquelados; vacio si son solo asteriscos)
- vehiculo_vin: 17 caracteres
- vehiculo_serie, vehiculo_chasis
- regrabado_motor, regrabado_serie, regrabado_chasis: "S" | "N" | "" (la casilla REG junto a cada numero)
- propietario_nombre, propietario_tipo_documento ("CC" | "NIT" | "CE" | "PA" | ""), propietario_documento
- potencia_hp, puertas
- declaracion_importacion, fecha_importacion (YYYY-MM-DD)
- fecha_matricula, fecha_expedicion, fecha_vencimiento (YYYY-MM-DD; vacia si son asteriscos)
- organismo_transito: el que aparece bajo "ORGANISMO DE TRANSITO" en el reverso, tal cual
- serial_especie_venal: el codigo que empieza por "LT" bajo el codigo de barras
- restriccion_movilidad, blindaje, limitacion_propiedad (vacios si son asteriscos)
- observaciones: si es_valido es false, explica en una frase QUE es el documento

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"licencia_transito","es_valido":true,"paginas_documento":[1],"total_paginas":1,"caras_presentes":"ambas","numero_licencia":"","vehiculo_placa":"","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","vehiculo_cilindrada":"","vehiculo_color":"","vehiculo_clase":"","vehiculo_carroceria":"","vehiculo_combustible":"","vehiculo_servicio":"","vehiculo_capacidad":"","vehiculo_motor":"","vehiculo_vin":"","vehiculo_serie":"","vehiculo_chasis":"","regrabado_motor":"","regrabado_serie":"","regrabado_chasis":"","propietario_nombre":"","propietario_tipo_documento":"","propietario_documento":"","potencia_hp":"","puertas":"","declaracion_importacion":"","fecha_importacion":"","fecha_matricula":"","fecha_expedicion":"","fecha_vencimiento":"","organismo_transito":"","serial_especie_venal":"","restriccion_movilidad":"","blindaje":"","limitacion_propiedad":"","observaciones":""}
""";

    /// <summary>
    /// HU #11998 — paz y salvo de impuestos. Calibrado sobre 54 documentos reales de 12 secretarias.
    /// <para><b>Esto no es un documento, es una funcion.</b> A diferencia de la licencia de transito
    /// —un artefacto con formato fijo—, el paz y salvo es un REQUISITO que cada departamento acredita
    /// con algo distinto: estado de cuenta de la gobernacion, historico de pagos del portal, o la
    /// declaracion del impuesto. Ninguno de los 43 ejemplares con capa de texto contiene la frase
    /// «PAZ Y SALVO». Por eso el prompt decide por QUIEN LO EMITE (autoridad tributaria) y no por el
    /// formato, y por eso rechaza dos cosas que se le parecen mucho: el comprobante de pago PSE
    /// —acredita una transaccion, no un estado de cuenta— y el recibo de caja de derechos de la
    /// secretaria de transito, que cobra sistematizacion o semaforizacion y ni siquiera es del
    /// impuesto vehicular aunque mencione el SIMIT (que son multas).</para>
    /// <para><b>El dato de valor no es la identidad, es la deuda.</b> Aqui el documento ya trae la
    /// placa; lo que importa es si el vehiculo adeuda vigencias. El estado de cuenta lo dice en un
    /// recuadro propio, vacio cuando esta al dia; el historico hay que deducirlo; una declaracion
    /// suelta no lo permite saber y devuelve «no_determinado» a proposito. El OCR informa, no bloquea.</para>
    /// <para><b>Medicion (claude-haiku-4-5, max_tokens 2000, dos corridas):</b> la decision de
    /// aceptar/rechazar salio IDENTICA en ambas —41 aceptados y los MISMOS 13 rechazados— y el tipo
    /// coincidio en 53 de 54. La placa oscilo entre 87,8 % y 90,2 %. Lo binario es estable, los
    /// porcentajes tienen una banda de ±8 puntos: no se mezclan en un mismo umbral.</para>
    /// <para><b>Dato de negocio:</b> 20 de 61 documentos de la muestra (33 %) no eran un paz y salvo.</para>
    /// </summary>
    private const string PazSalvo =
"""
Analiza este documento. Determina si acredita el ESTADO DE IMPUESTOS de un vehiculo ante la autoridad tributaria en Colombia (lo que en el tramite se pide como "Paz y Salvo de Impuestos").

QUE SE CONSIDERA VALIDO — LA CLAVE ES QUIEN LO EMITE:
Este requisito NO tiene un formato unico: cada departamento lo acredita con un documento distinto. Es VALIDO cualquier documento emitido por una AUTORIDAD TRIBUTARIA (gobernacion, secretaria de hacienda, unidad de rentas, o el portal oficial de impuestos del departamento) que informe sobre el impuesto de vehiculos automotores de un vehiculo concreto. Los formatos que veras:
1. ESTADO DE CUENTA o CERTIFICADO de la gobernacion / unidad de rentas: lleva numero de certificado, la tabla de declaraciones por vigencia, un recuadro de VIGENCIAS ADEUDADAS, firma de un funcionario y a veces codigo de barras. Es el mas completo.
2. HISTORICO DE PAGOS del portal de impuestos del departamento: una pagina web impresa con la placa y una tabla de vigencias con formulario, fecha y valor pagado. Puede llevar visibles los botones "Regresar" o "Imprimir": eso NO lo invalida, es una impresion de pantalla del portal oficial.
3. DECLARACION del impuesto sobre vehiculos automotores (formulario departamental o formulario web), presentada por el contribuyente ante la gobernacion.

QUE NO ES VALIDO:
1. Un COMPROBANTE DE PAGO BANCARIO o de PSE ("Pago PSE", "Pago exitoso", "Sucursal Virtual", "Comprobante en linea", numero CUS): acredita UNA transaccion, no el estado de cuenta del vehiculo. Aunque el comercio sea una gobernacion, sigue siendo un comprobante de pago.
2. Un RECIBO DE CAJA de derechos de tramite de una secretaria de transito o movilidad: cobra conceptos como "Derechos de Sistematizacion", "Facturacion", "Especie Venal" o "Costo lamina", lleva "NOMBRE Y FIRMA CAJERO", y NO es del impuesto vehicular. Ojo: puede mencionar un periodo liquidado y decir que el usuario no tiene deudas con el SIMIT —el SIMIT son MULTAS, no impuestos— y aun asi NO sirve.
3. Una PAGINA EN BLANCO, una plantilla vacia o un archivo cuyo unico contenido es un logo.
4. Un SOAT, una revision tecnico-mecanica, una licencia de transito, una factura, una impronta o una declaracion de importacion.

COMO DECIDIR — PROCEDE EN ESTE ORDEN:
PASO 1. Busca al emisor: gobernacion, departamento, secretaria de hacienda, unidad de rentas o portal de impuestos ⇒ candidato a valido. Banco, PSE o secretaria de transito/movilidad ⇒ NO valido.
PASO 2. Busca el objeto: debe hablar del IMPUESTO SOBRE VEHICULOS AUTOMOTORES de un vehiculo identificado por su placa. Si solo hay una transaccion o unos derechos de tramite, NO es valido.
PASO 3. Solo si los dos pasos anteriores dan positivo, es_valido = true.

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos (comprobante de pago + certificado + otros), identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el documento solicitado (ej: [1] o [2,3]). Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro, NO lo invalida y NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.

LA DEUDA — ES EL DATO MAS VALIOSO, LEELO CON CUIDADO:
El punto de este documento es saber si el vehiculo esta al dia. Determina estado_deuda asi:
- "al_dia": el recuadro VIGENCIAS ADEUDADAS esta VACIO, o el documento dice expresamente que no adeuda, o el historico muestra pagada la vigencia del ano en curso.
- "adeuda": el recuadro VIGENCIAS ADEUDADAS lista uno o mas anos, o aparecen procesos fiscales, bloqueos o sanciones pendientes.
- "no_determinado": el documento no permite saberlo (tipico de una declaracion suelta, que acredita que se declaro pero no el saldo).
NO deduzcas "al_dia" solo porque el documento existe. Un recuadro vacio SI significa al dia; la ausencia del recuadro NO. Ante la duda, "no_determinado".
En vigencias_adeudadas pon los anos listados como adeudados, separados por coma; vacio si no adeuda ninguno.

LA PLACA — DOS COMPROBACIONES OBLIGATORIAS:
1. FORMATO: una placa colombiana es 3 LETRAS + 3 DIGITOS (automoviles) o 3 LETRAS + 2 DIGITOS + 1 LETRA (motos). SIEMPRE 6 caracteres, sin espacios ni guiones. Si lo que transcribiste no encaja, esta mal leido.
2. Las letras Q, O, G, D y C se confunden entre si en los escaneos: Q lleva colita, O es un ovalo limpio, G lleva barra horizontal, D tiene el lado izquierdo recto, C esta abierta. Mira cada una de las tres letras antes de darla por buena.
Si el documento no trae placa, dejala VACIA. No la inventes ni la tomes de otro documento del archivo.

SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

EXTRAER:
- legibilidad: "buena" | "parcial" | "mala"
- tipo_documento: "estado_cuenta" | "historico_pagos" | "declaracion_impuesto" | "comprobante_pago_bancario" | "recibo_derechos_transito" | "otro"
- es_valido: true/false
- paginas_documento: [paginas], total_paginas: numero
- emisor: nombre de la entidad que lo expide, tal cual aparece
- emisor_es_autoridad_tributaria: true/false
- numero_certificado: numero del certificado o del formulario, si lo tiene
- vehiculo_placa
- vehiculo_marca, vehiculo_linea, vehiculo_modelo
- propietario_nombre, propietario_documento
- municipio, departamento
- estado_deuda: "al_dia" | "adeuda" | "no_determinado"
- vigencias_adeudadas: anos adeudados separados por coma, vacio si ninguno
- vigencia_certificada: periodo que cubre el documento (ej: "2023 - 2026"), vacio si no aplica
- fecha_expedicion (YYYY-MM-DD)
- avaluo: avaluo del vehiculo si aparece (numerico, sin puntos)
- observaciones: si es_valido es false, explica en una frase QUE es el documento y por que no sirve

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"estado_cuenta","es_valido":true,"paginas_documento":[1],"total_paginas":1,"emisor":"","emisor_es_autoridad_tributaria":true,"numero_certificado":"","vehiculo_placa":"","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","propietario_nombre":"","propietario_documento":"","municipio":"","departamento":"","estado_deuda":"no_determinado","vigencias_adeudadas":"","vigencia_certificada":"","fecha_expedicion":"","avaluo":0,"observaciones":""}
""";

    /// <summary>
    /// HU #11999 — <b>Inscripción de Prenda</b> (<c>id_attached_registered_pledge</c> en V1). Obligatoria
    /// en los tres trámites de prenda de V2: Inscribir prenda, Levantar e inscribir prenda y Cambio acreedor.
    /// <para><b>El dato de valor es el ACREEDOR, no el vehículo.</b> A diferencia de la licencia de tránsito
    /// —donde lo útil era cruzar placa y VIN— aquí lo que el trámite necesita saber es a favor de quién queda
    /// el vehículo, para contrastarlo con el acreedor registrado en el trámite. Por eso el prompt insiste en
    /// buscar «ACREEDOR GARANTIZADO» / «a favor de» en el CUERPO del contrato: quien firma al pie suele ser
    /// el deudor o un apoderado, y ese nombre no sirve.</para>
    /// <para><b>Tres formatos, todos válidos:</b> el contrato de garantía mobiliaria (prenda sin tenencia),
    /// el certificado de inscripción en el RUG/RNGM de Confecámaras, y la consulta del RUNT que muestra la
    /// garantía ya registrada. Solo 16 de los 65 ejemplares medidos traían capa de texto: esto es visión sobre
    /// escaneos, no extracción de texto.</para>
    /// <para><b>Lo que el prompt NO debe exigir.</b> El trámite existe precisamente para INSCRIBIR la prenda,
    /// así que el contrato llega normalmente ANTES de estar registrado: pedir folio del RUG o constancia de
    /// registro sería pedir el resultado como requisito de entrada. Tampoco se exige la placa —muchos contratos
    /// identifican el vehículo solo por chasis— y basta uno de placa/chasis/VIN/motor. Ambas exigencias
    /// causaron falsos rechazos reales en la primera versión.</para>
    /// <para><b>Medición (claude-haiku-4-5, max_tokens 2000, 65 documentos de 7 secretarías, dos corridas):</b>
    /// acreedor correcto 40/40 en ambas y NIT 37/40 en ambas; decisión aceptar/rechazar IDÉNTICA entre corridas
    /// y <c>tipo_documento</c> coincidente en 65/65. La placa acierta el 81 % donde el documento la trae, pero
    /// 14 de los 50 aceptados sencillamente no la traen. Falsos positivos: 0 — los 11 documentos en blanco de
    /// la muestra se rechazaron los 11.</para>
    /// <para><b>Límite conocido, y no es del prompt.</b> Un expediente de 37 páginas con el contrato válido en
    /// la página 1 y 31 páginas de certificado de cámara de comercio del banco se rechaza en las tres versiones
    /// del prompt. El mismo prompt contra el mismo archivo recortado a 6 páginas lo acepta y extrae todo bien:
    /// es dilución de atención por longitud. Se resuelve con selección de páginas en el preprocesado, no
    /// escribiendo más instrucciones. Impacto medido: 1 de 65.</para>
    /// <para><b>Dato de negocio:</b> 11 de los 65 documentos (17 %) eran una página en blanco, y 10 de ellos
    /// eran el MISMO archivo byte a byte reutilizado para poder pasar el formulario de V1.</para>
    /// </summary>
    private const string InscripcionPrenda =
"""
Analiza este documento. Determina si acredita que sobre un vehiculo se constituyo o se registro una PRENDA a favor de un acreedor (lo que el tramite pide como "Inscripcion / Registro de Prenda"). En Colombia esa figura se llama hoy GARANTIA MOBILIARIA sobre vehiculo automotor, tambien llamada "prenda sin tenencia".

QUE SE CONSIDERA VALIDO — TRES FORMAS, TODAS SIRVEN:
1. CONTRATO DE GARANTIA MOBILIARIA (o "prenda sin tenencia", o "garantia mobiliaria prioritaria de adquisicion"). Es el mas frecuente. Lo emite una entidad financiera y lleva: el nombre del ACREEDOR GARANTIZADO (banco, cooperativa, compania de financiamiento o leasing), el nombre del GARANTE o DEUDOR, la cuantia garantizada, y una tabla con los datos del vehiculo (PLACA, MARCA, LINEA, MODELO, COLOR, CLASE, CHASIS, MOTOR, CILINDRAJE). Suele ocupar varias paginas de clausulas y venir escaneado.
2. CERTIFICADO o FORMULARIO DE INSCRIPCION en el Registro de Garantias Mobiliarias (RUG / RNGM, operado por Confecamaras). Lleva un numero de folio o de inscripcion, la fecha de registro, el acreedor garantizado y el bien.
3. CONSULTA DEL RUNT que muestre la seccion de GARANTIAS MOBILIARIAS con la garantia ya registrada: una tabla con "ID Prenda", "Fecha de Registro", la identificacion y el nombre de la entidad, y un estado como "Registro de la garantia en el RNGM por parte de RUNT". Es una impresion de pantalla del portal oficial y eso NO la invalida.

QUE NO ES VALIDO:
1. Una PAGINA EN BLANCO, un archivo cuyo unico contenido es la palabra "Blanco", una plantilla vacia o un archivo con solo un logo. Es un relleno para pasar el formulario, no un documento.
2. Una CARTA DE APROBACION DE CREDITO, un PAGARE o una carta de intencion: anuncian un credito o mencionan que habra una prenda, pero NO constituyen la garantia sobre el vehiculo.
3. Una POLIZA DE SEGURO, aunque el banco figure como beneficiario.
4. Una FACTURA de compraventa del vehiculo, o una cotizacion.
5. Un CERTIFICADO DE CAMARA DE COMERCIO o de existencia y representacion legal del acreedor.
6. Una consulta del RUNT SIN la seccion de garantias mobiliarias diligenciada, o con esa seccion vacia.
7. Un SOAT, una revision tecnico-mecanica, una licencia de transito, un paz y salvo o una impronta.

COMO DECIDIR — PROCEDE EN ESTE ORDEN:
PASO 1. Busca la figura juridica: un contrato de garantia mobiliaria o prenda, un registro en el RUG/RNGM, o la tabla de garantias del RUNT. Si no aparece ninguna de las tres, NO es valido.
PASO 2. Busca al ACREEDOR: debe identificarse la entidad a favor de la cual queda la garantia. Sin acreedor identificable NO es valido.
PASO 3. Busca el BIEN: el vehiculo debe estar identificado por AL MENOS UNO de estos: placa, chasis, VIN o numero de motor. UNO SOLO BASTA. Que falte la placa NO es motivo de rechazo si hay chasis, VIN o motor. Solo se rechaza si la garantia es generica sobre bienes indeterminados.
PASO 4. Solo si los tres pasos dan positivo, es_valido = true.
Un contrato SIN firmar sigue siendo valido para este control: no juzgues la firma, solo el contenido.

LO QUE NO DEBES EXIGIR — LEE ESTO ANTES DE RECHAZAR:
El tramite para el que sirve este documento es, precisamente, INSCRIBIR la prenda. Por eso el contrato llega normalmente ANTES de estar registrado. NO exijas numero de folio del RUG, ni fecha de inscripcion, ni constancia de registro: su ausencia es lo normal y NO es motivo de rechazo. Tampoco exijas la placa, ni el avaluo, ni la firma, ni que el contrato este completo palabra por palabra.

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Estos archivos suelen traer decenas de paginas y mezclar documentos. Si el PDF contiene MULTIPLES documentos, identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el documento solicitado (ej: [1] o [2,3]). Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro, NO lo invalida y NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.
SI EL ARCHIVO TIENE MAS DE 10 PAGINAS, sigue este orden antes de decidir: mira la PAGINA 1, luego la 2 y la 3, y solo despues el resto. En los expedientes largos el contrato va casi siempre al principio y los anexos corporativos del banco (certificado de camara de comercio, estatutos, poderes) van despues; si te dejas llevar por lo que mas se repite vas a rechazar un contrato valido.
LA PROPORCION NO CUENTA. Es habitual que el contrato ocupe 6 paginas y las otras 31 sean el certificado de camara de comercio del banco: eso sigue siendo VALIDO. UNA SOLA pagina con el contrato basta para que es_valido sea true. No dejes que el documento mayoritario decida por ti: RECORRE TODAS LAS PAGINAS, y en especial la PRIMERA, antes de concluir que la prenda no esta.

EL ACREEDOR — ES EL DATO MAS VALIOSO:
El punto de este documento es saber A FAVOR DE QUIEN queda el vehiculo. En un contrato el acreedor es quien aparece como "EL ACREEDOR GARANTIZADO", "a favor de", "el Banco" o "el acreedor prendario"; NO es el garante, ni el deudor, ni el concesionario, ni el titular del vehiculo. Buscalo en el CUERPO del contrato, no al pie: quien firma abajo suele ser el deudor o un apoderado, y su nombre NO va en acreedor_nombre. Si solo encuentras nombres de personas naturales y ninguna entidad designada como acreedora, deja acreedor_nombre VACIO en lugar de poner a la persona que firma. Copia la razon social completa tal como esta escrita, incluidos "S.A.", "BIC" o "S.A.S.". Si el documento lleva una marca comercial junto a la entidad (por ejemplo "Sufi" junto a Bancolombia), pon en acreedor_nombre la ENTIDAD, no la marca.
Si aparece el NIT del acreedor, ponlo en acreedor_documento solo con digitos, sin puntos, sin guion y SIN el digito de verificacion.

LA PLACA — DOS COMPROBACIONES OBLIGATORIAS:
1. FORMATO: una placa colombiana es 3 LETRAS + 3 DIGITOS (automoviles) o 3 LETRAS + 2 DIGITOS + 1 LETRA (motos). SIEMPRE 6 caracteres, sin espacios ni guiones. Si lo que transcribiste no encaja, esta mal leido.
   USA EL FORMATO PARA CORREGIRTE, POSICION POR POSICION: los caracteres 1, 2 y 3 son SIEMPRE LETRAS y los caracteres 4, 5 y 6 son SIEMPRE DIGITOS (salvo en motos, donde el 6 es letra). Por eso: un 0 en las tres primeras posiciones es en realidad una O; un 4 ahi es una A o una Y; un 1 es una I o una L; un 8 es una B. Y al reves, una O entre los tres ultimos es un 0, y una I o una L ahi es un 1. Corrige cada caracter que este en la posicion equivocada ANTES de responder.
2. Las letras Q, O, G, D y C se confunden entre si en los escaneos: Q lleva colita, O es un ovalo limpio, G lleva barra horizontal, D tiene el lado izquierdo recto, C esta abierta. Mira cada una de las tres letras antes de darla por buena.
3. NO confundas la placa con el VIN, el chasis o la serie: si lo que ibas a copiar tiene mas de 6 caracteres o mezcla mas de 3 letras, NO es la placa.
4. Confusiones de digito frecuentes en escaneos: Y con 4, 6 con 5, 1 con 7, 8 con B. Reconstruye cada caracter mirando la forma, no el parecido global.
Si el documento no trae placa, dejala VACIA. No la inventes ni la tomes de otro documento del archivo. Repito: la placa vacia NO invalida el documento.

EL CHASIS Y EL VIN:
Muchos contratos traen CHASIS pero no VIN, o al reves, y en los vehiculos importados ambos coinciden. Transcribe cada uno en su campo y NO copies el chasis dentro de vehiculo_vin si el documento no lo llama VIN. Si un campo aparece relleno de asteriscos o guiones, dejalo vacio.

SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

EXTRAER:
- legibilidad: "buena" | "parcial" | "mala"
- tipo_documento: "contrato_garantia_mobiliaria" | "certificado_rug" | "consulta_runt_garantias" | "aprobacion_credito" | "pagare" | "poliza" | "factura" | "certificado_camara_comercio" | "documento_en_blanco" | "otro"
  Si el archivo mezcla varios, pon el tipo del documento SOLICITADO cuando lo encuentres, no el del mayoritario.
- es_valido: true/false
- paginas_documento: [paginas], total_paginas: numero
- acreedor_nombre: razon social de la entidad a favor de la cual queda la garantia
- acreedor_documento: NIT del acreedor, solo digitos, sin digito de verificacion
- acreedor_es_entidad_financiera: true/false
- garante_nombre, garante_documento: quien da el vehiculo en garantia
- numero_registro: folio del RUG, "ID Prenda" del RUNT o numero de garantia, si lo tiene
- fecha_registro (YYYY-MM-DD): fecha de inscripcion en el registro, si aparece
- fecha_contrato (YYYY-MM-DD): fecha de suscripcion del contrato, si aparece
- cuantia_garantia: monto garantizado (numerico, sin puntos ni simbolos), 0 si no aparece
- vehiculo_placa
- vehiculo_vin, vehiculo_chasis, vehiculo_motor
- vehiculo_marca, vehiculo_linea, vehiculo_modelo
- observaciones: si es_valido es false, explica en una frase QUE es el documento y por que no sirve

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"contrato_garantia_mobiliaria","es_valido":true,"paginas_documento":[1],"total_paginas":1,"acreedor_nombre":"","acreedor_documento":"","acreedor_es_entidad_financiera":true,"garante_nombre":"","garante_documento":"","numero_registro":"","fecha_registro":"","fecha_contrato":"","cuantia_garantia":0,"vehiculo_placa":"","vehiculo_vin":"","vehiculo_chasis":"","vehiculo_motor":"","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","observaciones":""}
""";

    /// <summary>
    /// HU #12000 — <b>Comprobante de pago</b> (<c>id_attached_payment_receipt</c> en V1, «Comprobante de
    /// derechos» en el catálogo de V2).
    /// <para><b>Una casilla, dos documentos.</b> El cruce entre la tabla de origen y el tipo detectado no
    /// deja lugar a dudas: en <i>matrícula</i> 38 de 50 son la declaración o el pago del impuesto vehicular,
    /// y en <i>traspaso</i> y <i>otros servicios</i> 14 de 16 son el recibo de derechos del organismo de
    /// tránsito. No es que los usuarios se equivoquen: son dos requisitos distintos compartiendo un mismo
    /// campo en V1. Por eso el prompt acepta las tres familias —recibo de derechos, comprobante electrónico
    /// y declaración del impuesto— en vez de fijar un formato.</para>
    /// <para><b>El criterio se INVIERTE respecto al Paz y Salvo, y hay que decirlo explícitamente.</b> Allí
    /// un comprobante PSE se rechaza porque acredita una transacción y no un estado de cuenta; aquí un
    /// comprobante PSE es exactamente lo que se pide. Y el recibo de caja de la secretaría, que el paz y
    /// salvo rechaza, es aquí la familia mayoritaria del traspaso. El prompt lo advierte para no heredar el
    /// criterio del prompt vecino.</para>
    /// <para><b>El valor va siempre, el pago se informa aparte.</b> En la primera versión el campo se
    /// llamaba <c>valor_pagado</c> y el modelo lo interpretó al pie de la letra: 15 de 63 documentos
    /// aceptados salieron sin valor, y la correlación con «no pagado» era perfecta. No era un fallo de
    /// lectura sino una ambigüedad del contrato de datos. Separado en <c>valor_total</c> (lo que el
    /// documento liquida o cobra, esté pagado o no) y <c>hay_constancia_pago</c>, la extracción pasó a
    /// 63 de 63. Una liquidación sin pagar sigue siendo el documento correcto: se informa, no se rechaza.</para>
    /// <para><b>Medición (claude-haiku-4-5, max_tokens 2000, 66 documentos de 7 secretarías, dos corridas):</b>
    /// placa correcta 96,8 % —la más alta de los cuatro documentos medidos, porque son PDFs de una página
    /// generados por máquina—, decisión aceptar/rechazar IDÉNTICA entre corridas y <c>tipo_documento</c>
    /// coincidente en 66/66. Falsos positivos y falsos rechazos: 0. Los tres rechazos son dos licencias de
    /// tránsito cargadas en la casilla del pago —ninguna con capa de texto, identificadas por visión— y una
    /// página en blanco.</para>
    /// <para><b>Dependencia de configuración:</b> el tipo existe en <c>document_types</c> pero ningún
    /// trámite lo pide todavía en <c>procedure_document_requirements</c>. Este prompt queda listo y no se
    /// ejecutará hasta que se configure en qué trámites se solicita.</para>
    /// </summary>
    private const string ComprobanteDerechos =
"""
Analiza este documento. Determina si acredita el PAGO o la LIQUIDACION de un valor que hay que cubrir para tramitar un vehiculo en Colombia (lo que el tramite pide como "Comprobante de pago").

QUE SE CONSIDERA VALIDO — LA CLAVE ES QUE HAYA DINERO LIQUIDADO O PAGADO:
Este requisito NO tiene un formato unico. Se cubre con tres documentos distintos segun el tramite y el organismo, y los TRES sirven:
1. RECIBO DE CAJA o LIQUIDACION DE DERECHOS de un organismo de transito o secretaria de movilidad: lleva el municipio y su NIT, la placa, y una tabla de CODIGO / CONCEPTO / VALOR con conceptos como "Derechos de Sistematizacion", "Facturacion Tramites", "Derechos RUNT", "Especie Venal", "Cancelacion Matricula" o "Semaforizacion". Suele llevar "NOMBRE Y FIRMA CAJERO".
2. COMPROBANTE DE PAGO ELECTRONICO O BANCARIO: pago por PSE, ecollect, sucursal virtual o ventanilla, con numero de transaccion, numero de autorizacion o CUS, fecha y hora, y el nombre del recaudador (una gobernacion, un municipio, un organismo de transito o el banco que recauda por ellos).
3. DECLARACION o LIQUIDACION DEL IMPUESTO SOBRE VEHICULOS AUTOMOTORES: el formulario departamental, con numero de formulario, placa, declarante y el valor a pagar. Sirve AUNQUE no tenga sello de pago: es la liquidacion oficial del valor.

OJO — AQUI EL COMPROBANTE DE PAGO SI VALE:
A diferencia de un paz y salvo, donde un comprobante de pago NO sirve porque no acredita el estado de cuenta, en ESTE documento el comprobante de pago es exactamente lo que se pide. Un "Pago PSE", un "Pago exitoso" o un numero CUS a favor de una gobernacion o de un organismo de transito es VALIDO.

QUE NO ES VALIDO:
1. Una PAGINA EN BLANCO, una plantilla vacia o un archivo cuyo unico contenido es un logo.
2. Un ESTADO DE CUENTA o PAZ Y SALVO de impuestos: informa si el vehiculo adeuda, pero no liquida ni acredita el pago de este tramite. Va en otra casilla.
3. Una LICENCIA DE TRANSITO o tarjeta de propiedad, un SOAT, una revision tecnico-mecanica, una impronta, un contrato de prenda o una cedula.
4. La FACTURA DE VENTA del vehiculo: acredita la compra del carro, no el pago de derechos ni de impuestos.
5. Una COTIZACION, una simulacion o una orden de pago sin valores.

COMO DECIDIR — PROCEDE EN ESTE ORDEN:
PASO 1. Busca DINERO: un valor liquidado, un total a pagar o un valor pagado. Si el documento no tiene ninguna cifra de dinero asociada al vehiculo o al tramite, NO es valido.
PASO 2. Busca al RECAUDADOR o LIQUIDADOR: un organismo de transito, una secretaria de movilidad, una gobernacion, una unidad de rentas, o un banco o pasarela recaudando a nombre de ellos. Si quien cobra es un particular, un concesionario o un taller, NO es valido.
PASO 3. Solo si los dos pasos anteriores dan positivo, es_valido = true.
Que el documento no lleve sello, firma ni constancia de pago NO es motivo de rechazo: una liquidacion sin pagar sigue siendo el documento correcto. Eso se informa en hay_constancia_pago, no en es_valido.

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos (liquidacion + comprobante + otros), identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el documento solicitado (ej: [1] o [2,3]). Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro, NO lo invalida y NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.

LA CONSTANCIA DE PAGO — ES EL DATO MAS VALIOSO:
Lo que el gestor necesita saber es si esto ya se pago o solo se liquido. Determina hay_constancia_pago asi:
- true: el documento muestra el pago hecho — numero de transaccion, numero de autorizacion o CUS, "Pago exitoso", "Aprobada", sello de recaudo, fecha y hora de pago, o el sello del banco.
- false: solo hay una liquidacion, un valor a pagar o un formulario diligenciado, sin ninguna huella de que el dinero se movio.
NO deduzcas que esta pagado solo porque el documento existe. Un formulario de declaracion recien impreso NO esta pagado. Ante la duda, false.
EL VALOR VA SIEMPRE, ESTE PAGADO O NO. En valor_total pon el TOTAL que el documento liquida o cobra —la suma de la tabla de conceptos, el "total a pagar" o el valor de la declaracion— en numeros, sin puntos ni simbolos. Ese campo NO depende de que el pago se haya hecho: un recibo sin cancelar igual trae su total, y hay que extraerlo. Solo dejalo en 0 si el documento realmente no muestra ninguna cifra.

LA PLACA — DOS COMPROBACIONES OBLIGATORIAS:
1. FORMATO: una placa colombiana es 3 LETRAS + 3 DIGITOS (automoviles) o 3 LETRAS + 2 DIGITOS + 1 LETRA (motos). SIEMPRE 6 caracteres, sin espacios ni guiones. Si lo que transcribiste no encaja, esta mal leido.
   USA EL FORMATO PARA CORREGIRTE, POSICION POR POSICION: los caracteres 1, 2 y 3 son SIEMPRE LETRAS y los caracteres 4, 5 y 6 son SIEMPRE DIGITOS (salvo en motos, donde el 6 es letra). Por eso: un 0 en las tres primeras posiciones es en realidad una O; un 4 ahi es una A o una Y; un 1 es una I o una L; un 8 es una B. Y al reves, una O entre los tres ultimos es un 0, y una I o una L ahi es un 1. Corrige cada caracter que este en la posicion equivocada ANTES de responder.
2. Las letras Q, O, G, D y C se confunden entre si en los escaneos: Q lleva colita, O es un ovalo limpio, G lleva barra horizontal, D tiene el lado izquierdo recto, C esta abierta. Mira cada una de las tres letras antes de darla por buena.
Si el documento no trae placa, dejala VACIA. No la inventes ni la tomes de otro documento del archivo.

SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

EXTRAER:
- legibilidad: "buena" | "parcial" | "mala"
- tipo_documento: "recibo_derechos_transito" | "comprobante_pago_electronico" | "declaracion_impuesto" | "estado_cuenta" | "factura_venta" | "licencia_transito" | "documento_en_blanco" | "otro"
- es_valido: true/false
- paginas_documento: [paginas], total_paginas: numero
- entidad_recaudadora: quien cobra o liquida, tal cual aparece
- entidad_es_autoridad: true si es un organismo de transito, secretaria, gobernacion o unidad de rentas (o un banco recaudando por ellos); false si no
- hay_constancia_pago: true/false
- valor_total: total liquidado o cobrado, en numeros y sin puntos ni simbolos, este pagado o no; 0 solo si el documento no muestra ninguna cifra
- fecha_pago (YYYY-MM-DD): la del pago si lo hubo, si no la de expedicion o liquidacion
- numero_referencia: numero de recibo, de formulario, de transaccion, de autorizacion o CUS, el que traiga
- conceptos: los conceptos cobrados separados por coma (ej: "Derechos de Sistematizacion, Derechos RUNT"); vacio si no los desglosa
- vehiculo_placa
- propietario_nombre, propietario_documento
- municipio, departamento
- observaciones: si es_valido es false, explica en una frase QUE es el documento y por que no sirve

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"recibo_derechos_transito","es_valido":true,"paginas_documento":[1],"total_paginas":1,"entidad_recaudadora":"","entidad_es_autoridad":true,"hay_constancia_pago":false,"valor_total":0,"fecha_pago":"","numero_referencia":"","conceptos":"","vehiculo_placa":"","propietario_nombre":"","propietario_documento":"","municipio":"","departamento":"","observaciones":""}
""";

    /// <summary>
    /// HU #12001 — <b>Contrato de Leasing</b> (<c>id_attached_leasing_contract</c> en V1). Lo pide un solo
    /// trámite, <c>MATRICULA_LEASING</c>, como obligatorio y en primer lugar.
    /// <para><b>El vehículo todavía no tiene placa, y eso condiciona todo el prompt.</b> Ese trámite entra
    /// por VIN —es una matrícula—, así que el contrato ampara un vehículo aún sin matricular. Medido: la
    /// placa aparece en <b>1 de 52</b> documentos. Un prompt que la exigiera rechazaría la muestra entera,
    /// de modo que la regla «no exijas la placa» es aquí más terminante que en la inscripción de prenda.</para>
    /// <para><b>Las dos partes son el dato de valor.</b> En el leasing el vehículo queda a nombre del
    /// ARRENDADOR y el LOCATARIO es una parte distinta que V2 registra aparte (ver
    /// <c>88-locatario-parte-propia.sql</c>). El prompt los separa explícitamente y prohíbe confundir al
    /// arrendador con el proveedor del vehículo o con el representante que firma.</para>
    /// <para><b>Riesgo tratado: un NIT inventado.</b> La carátula de estos contratos NO trae el NIT de la
    /// entidad; trae el nombre del arrendador y debajo la <i>cédula del representante</i>. En la primera
    /// versión el modelo rellenó el NIT en los 50 documentos aceptados y <b>los 50 estaban mal</b> — en la
    /// corrida de control quedaron tres, y eran tres números distintos para la misma entidad, ninguno el
    /// real. Se corrigió por dos vías: el prompt solo rellena el campo si hay un número rotulado como NIT
    /// del arrendador, y <b>el resumen del checklist no muestra ese campo</b>, para que el riesgo no dependa
    /// de que el prompt acierte siempre.</para>
    /// <para><b>Medición (claude-haiku-4-5, max_tokens 2000, 52 documentos, dos corridas):</b> arrendador
    /// correcto 49/49; 51 de 52 decisiones iguales entre corridas y <c>tipo_documento</c> coincidente en
    /// 52/52. Es el primer documento cuya decisión no sale idéntica, y el que oscila es justamente el caso
    /// frontera: un anexo de iniciación del plazo que nombra al arrendador pero no al locatario.</para>
    /// <para><b>Sin dilución, y el contraste importa.</b> 34 de los 52 documentos tienen entre 20 y 25
    /// páginas y los 34 se aceptan, al revés que el expediente de 37 páginas de la prenda. La diferencia no
    /// es la longitud sino qué hay en las páginas de más: aquí son el clausulado del MISMO documento, allí
    /// eran OTRO documento ahogando al válido. Lo que confunde al modelo no es un documento largo, es un
    /// documento distinto dentro del archivo.</para>
    /// <para><b>Coste:</b> ≈26.500 tokens de entrada por documento —unas cuatro veces los demás— porque son
    /// escaneos de 20 a 25 páginas: solo 1 de 52 traía capa de texto.</para>
    /// </summary>
    private const string ContratoLeasing =
"""
Analiza este documento. Determina si es un CONTRATO DE LEASING sobre un vehiculo, es decir un contrato de ARRENDAMIENTO FINANCIERO entre una compañia de leasing o entidad financiera (el ARRENDADOR, que sera el propietario del vehiculo) y un LOCATARIO (quien lo usa y puede comprarlo al final).

QUE SE CONSIDERA VALIDO:
Un contrato de arrendamiento financiero, leasing o "leasing financiero" en el que se identifiquen:
- El ARRENDADOR: una compañia de leasing, banco o entidad financiera vigilada. Aparece como "Leasing Bancolombia", "Datos de <entidad> S.A.", "el Arrendador", "la Compañia" o "el Banco".
- El LOCATARIO: la persona natural o juridica que recibe el bien. Aparece como "LOCATARIO", "DATOS DE EL(LOS) LOCATARIO(S)" o "el Arrendatario".
- Al menos un BIEN objeto del contrato que sea un vehiculo automotor, remolque o semirremolque.
Sirve tambien el anexo o acta de entrega del contrato, siempre que identifique arrendador, locatario y bien.

QUE NO ES VALIDO:
1. Una PAGINA EN BLANCO, una plantilla vacia, un archivo cuyo unico contenido es la palabra "OTROS ANEXOS" o un logo.
2. Una FACTURA de venta del vehiculo, una cotizacion o una orden de compra al proveedor.
3. Un CONTRATO DE GARANTIA MOBILIARIA o de prenda: ahi el banco es acreedor, no arrendador, y el vehiculo es del deudor. En el leasing el vehiculo es DEL ARRENDADOR.
4. Un PAGARE, una carta de aprobacion de credito o una poliza de seguro.
5. Un CERTIFICADO DE CAMARA DE COMERCIO o de existencia y representacion legal.
6. Un SOAT, una revision tecnico-mecanica, una licencia de transito, una impronta o una cedula.

COMO DECIDIR — PROCEDE EN ESTE ORDEN:
PASO 1. Busca la figura: arrendamiento financiero o leasing. Si lo que hay es una compraventa, una prenda o un credito, NO es valido.
PASO 2. Busca las DOS partes: arrendador (entidad) y locatario. Si falta cualquiera de las dos, NO es valido.
PASO 3. Busca el BIEN: debe ser un vehiculo, remolque o semirremolque, aunque solo se describa por marca, linea y modelo. Un leasing de inmuebles o de maquinaria que no rueda NO sirve.
PASO 4. Solo si los tres pasos dan positivo, es_valido = true.

LO QUE NO DEBES EXIGIR — LEE ESTO ANTES DE RECHAZAR:
1. NO exijas la PLACA. Este contrato sustenta una MATRICULA INICIAL: el vehiculo todavia NO tiene placa. Que no aparezca es lo normal y NO es motivo de rechazo. Lo mismo vale para el VIN, el chasis y el motor: pueden estar en un anexo o no estar.
2. NO exijas firmas, sellos, huellas ni autenticacion notarial.
3. NO exijas que el contrato este completo: basta con la caratula o la seccion de datos generales.
4. NO rechaces por la calidad del escaneo.

DOCUMENTO LARGO, ESCANEADO Y A VECES ROTADO:
Casi ninguno de estos archivos trae capa de texto: son escaneos, y muchas paginas vienen GIRADAS 90 GRADOS. Leelas igual, girando la lectura; que la pagina este de lado NO es motivo de rechazo.
Suelen tener entre 20 y 25 paginas, de las cuales solo las primeras traen los datos. MIRA LA PAGINA 1, luego la 2 y la 3 ANTES que el resto: ahi estan casi siempre la caratula, el numero de contrato, el arrendador, el locatario y la lista de bienes. El resto son clausulas repetidas y anexos. No dejes que el volumen de clausulado decida por ti.

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos (contrato + factura + poliza + otros), identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el documento solicitado (ej: [1] o [1,2,3]). Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro, NO lo invalida y NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.
LA PROPORCION NO CUENTA. UNA SOLA pagina con la caratula del contrato basta para que es_valido sea true.

LAS DOS PARTES — ES EL DATO MAS VALIOSO:
En el leasing el vehiculo queda a nombre del ARRENDADOR, y el LOCATARIO es una parte distinta que el tramite registra aparte. Por eso hay que separarlos bien:
- arrendador_nombre: la razon social de la compañia de leasing o entidad financiera, completa y tal como aparece ("Leasing Bancolombia S.A.", "Banco Davivienda S.A."). NO es el proveedor del vehiculo, ni el concesionario, ni el representante que firma. Si el encabezado nombra la marca de leasing y el pie la sociedad matriz, usa la que encabeza el contrato.
- locatario_nombre: la denominacion del locatario, bajo el rotulo "LOCATARIO", "DATOS DE EL(LOS) LOCATARIO(S)" o "Arrendatario". Buscalo tambien cuando la caratula este girada o el escaneo sea pobre: es un dato obligatorio del contrato y casi siempre esta en la primera pagina. Si son varios, pon el primero y cuenta los demas en numero_locatarios.
- NUNCA pongas el mismo nombre en los dos campos.
EL NIT — SOLO SI ESTA ROTULADO COMO DE ESA PARTE, NUNCA DEDUCIDO:
La caratula de estos contratos casi nunca trae el NIT del arrendador: trae el nombre de la entidad y, debajo, el nombre y la CEDULA DEL REPRESENTANTE que firma por ella. Esa cedula NO es el NIT del arrendador. Tampoco lo es el NIT del proveedor del vehiculo ni el del locatario.
Pon arrendador_documento SOLO si en el documento hay un numero rotulado expresamente como NIT o identificacion DEL ARRENDADOR o de la entidad. Si no lo ves asi rotulado, dejalo VACIO. Vacio es la respuesta correcta y esperada; un numero inventado o tomado de otra parte es un error grave.
La misma regla vale para locatario_documento: solo el numero rotulado como documento del locatario.
Cuando pongas un numero, ponlo solo con digitos, sin puntos, sin guion y SIN el digito de verificacion.

UN CONTRATO PUEDE CUBRIR VARIOS BIENES:
Es normal que un mismo contrato ampare varios vehiculos (un camion, un semirremolque, un tractocamion...). En numero_bienes pon cuantos bienes distintos enumera. En los campos del vehiculo describe el PRIMERO, y si hay mas de uno dilo en observaciones.

LA PLACA — SI Y SOLO SI APARECE:
Una placa colombiana es 3 LETRAS + 3 DIGITOS (automoviles) o 3 LETRAS + 2 DIGITOS + 1 LETRA (motos). SIEMPRE 6 caracteres, sin espacios ni guiones.
USA EL FORMATO PARA CORREGIRTE, POSICION POR POSICION: los caracteres 1, 2 y 3 son SIEMPRE LETRAS y los caracteres 4, 5 y 6 son SIEMPRE DIGITOS (salvo en motos, donde el 6 es letra). Por eso un 0 en las tres primeras posiciones es en realidad una O; un 4 ahi es una A o una Y; un 1 es una I o una L. Corrige cada caracter que este en la posicion equivocada ANTES de responder.
Si el documento no trae placa, dejala VACIA. No la inventes. Repito: la placa vacia es lo NORMAL en este documento.

SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

EXTRAER:
- legibilidad: "buena" | "parcial" | "mala"
- tipo_documento: "contrato_leasing" | "contrato_garantia_mobiliaria" | "factura_venta" | "poliza" | "certificado_camara_comercio" | "documento_en_blanco" | "otro"
- es_valido: true/false
- paginas_documento: [paginas], total_paginas: numero
- arrendador_nombre, arrendador_documento
- locatario_nombre, locatario_documento, numero_locatarios
- numero_contrato: el numero del contrato de leasing, si lo trae
- fecha_contrato (YYYY-MM-DD)
- numero_bienes: cuantos bienes distintos ampara el contrato
- vehiculo_descripcion: la descripcion del primer bien, tal como aparece
- vehiculo_placa, vehiculo_vin, vehiculo_chasis, vehiculo_motor
- vehiculo_marca, vehiculo_linea, vehiculo_modelo
- proveedor_nombre: el proveedor o concesionario del bien, si aparece
- observaciones: si es_valido es false, explica en una frase QUE es el documento y por que no sirve; si es valido y hay mas de un bien, dilo aqui

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"contrato_leasing","es_valido":true,"paginas_documento":[1],"total_paginas":1,"arrendador_nombre":"","arrendador_documento":"","locatario_nombre":"","locatario_documento":"","numero_locatarios":1,"numero_contrato":"","fecha_contrato":"","numero_bienes":1,"vehiculo_descripcion":"","vehiculo_placa":"","vehiculo_vin":"","vehiculo_chasis":"","vehiculo_motor":"","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","proveedor_nombre":"","observaciones":""}
""";

    /// <summary>
    /// HU #12030 — <b>Certificado de Cámara de Comercio</b> (`camara_comercio`, ya existente en
    /// <c>document_types</c>). En V1 se cargaba en <c>id_attached_buyer_id</c> y <c>id_attached_owner_id</c>.
    /// <para><b>Sustituye a la idea de hacer OCR del «documento de identidad», y conviene dejar dicho por
    /// qué.</b> Esa casilla no es un documento: son dos caminos según el tipo de persona. Para la persona
    /// natural, Kyverum ya valida la identidad con rostro contra documento en vivo y guarda la cédula por
    /// frente y reverso (<c>BiometricaCommand.cs:642-646</c>): un OCR sobre una copia subida a mano sería un
    /// control estrictamente peor. Para la persona jurídica, la biometría valida al representante legal como
    /// persona pero <b>a la empresa no la valida nadie</b>. Ese es el hueco, y es el mayoritario: dos de cada
    /// tres cargas recientes de esas casillas son NIT.</para>
    /// <para><b>El mejor ground truth que hemos tenido.</b> El actor jurídico del trámite ES la empresa del
    /// certificado, así que V1 da la respuesta correcta sin etiquetar nada: NIT y razón social al 100 %
    /// (68/68). De ahí que las cifras de abajo sean tan exigentes.</para>
    /// <para><b>Medición (claude-haiku-4-5, max_tokens 2000, 68 documentos de 10 secretarías, dos corridas):</b>
    /// <b>NIT correcto 54/54 en ambas</b>, razón social 51/54, decisión aceptar/rechazar idéntica, y
    /// <c>tipo_documento</c> y NIT coincidentes en 68/68. Es el resultado más estable de los seis documentos.
    /// Las 3 diferencias de razón social son datos desactualizados de V1 —«AUTONAL Y CIA» frente al «&amp;» del
    /// certificado, una LTDA transformada en S.A.S.—, no fallos de lectura.</para>
    /// <para><b>El confusable, y ya es el tercero de la misma clase.</b> Una ficha de homologación del
    /// Ministerio de Transporte cargada por error en esta casilla se aceptó como certificado en 3 de sus 4
    /// ejemplares, devolviendo «MINISTERIO DE TRANSPORTE» como razón social y un NIT inventado. Se parece de
    /// lejos: entidad oficial, números largos, muchos campos. El prompt la nombra para descartarla y fija que
    /// <b>el emisor manda sobre cualquier otra señal</b>; el NIT subió de 94,7 % a 100 %.</para>
    /// <para><b>Sin riesgo de rotación:</b> 68 de 68 declaran <c>Page rot: 0</c>, así que este documento está
    /// libre del fallo que bloquea al CEPD. Se comprobó ANTES de fiarse de ninguna medición.</para>
    /// <para><b>Dato de negocio:</b> 10 de los 68 (15 %) eran <c>no aplica documento.pdf</c>, los diez el mismo
    /// archivo — el mismo relleno que ya apareció en el paz y salvo.</para>
    /// <para><b>Dependencia:</b> ningún trámite pide todavía este documento en
    /// <c>procedure_document_requirements</c>. El prompt queda listo; configurarlo es decisión de negocio.</para>
    /// </summary>
    private const string CamaraComercio =
"""
Analiza este documento. Determina si es un CERTIFICADO DE EXISTENCIA Y REPRESENTACION LEGAL expedido por una CAMARA DE COMERCIO en Colombia, que es lo que acredita a una PERSONA JURIDICA como parte de un tramite de transito.

QUE SE CONSIDERA VALIDO:
Un certificado expedido por una Camara de Comercio que acredite la existencia de la sociedad y quien la representa. Se reconoce por:
- El encabezado con el nombre de la camara ("Camara de Comercio de Medellin para Antioquia", "Camara de Comercio de Bogota", "Camara de Comercio Aburra Sur"...).
- El titulo "CERTIFICADO DE EXISTENCIA Y REPRESENTACION LEGAL", a veces con "Y/O DE INSCRIPCION DE DOCUMENTOS".
- Una fecha de expedicion, un numero de recibo y casi siempre un codigo de verificacion.
- Los apartados de NOMBRE E IDENTIFICACION (razon social y NIT), MATRICULA, CONSTITUCION, OBJETO SOCIAL, REPRESENTACION LEGAL, FACULTADES DEL REPRESENTANTE LEGAL y NOMBRAMIENTOS.
Tambien es VALIDO el certificado de un ESTABLECIMIENTO DE COMERCIO o de una ENTIDAD SIN ANIMO DE LUCRO expedido por la misma camara, con la misma estructura.

QUE NO ES VALIDO:
1. El REGISTRO UNICO TRIBUTARIO (RUT) de la DIAN. Acredita la inscripcion tributaria, NO la existencia ni quien representa a la sociedad. Se reconoce por el encabezado de la DIAN y por el "Numero de formulario".
2. Una CEDULA DE CIUDADANIA suelta, sin el certificado.
3. Un CERTIFICADO RUES impreso del portal, sin ser el certificado de la camara.
4. La FICHA TECNICA DE HOMOLOGACION del Ministerio de Transporte ("FORMATO FTH-002", "CARACTERISTICAS TECNICO-MECANICAS DE VEHICULOS", con un numero de ficha tipo A00049538). Describe un modelo de vehiculo y NO tiene nada que ver con la existencia de una sociedad. Ojo: la emite un ministerio y lleva numeros largos, asi que se parece de lejos a un certificado; miralo bien antes de aceptarlo. Si el emisor es el MINISTERIO DE TRANSPORTE y no una camara de comercio, es_valido va en false.
5. Una FACTURA, una licencia de transito, un SOAT, una impronta, un contrato de prenda o una declaracion de importacion.
6. Una PAGINA EN BLANCO o una plantilla vacia, incluido el archivo tipo "no aplica documento".

COMO DECIDIR — PROCEDE EN ESTE ORDEN:
PASO 1. Busca al emisor: una CAMARA DE COMERCIO. Si el emisor es la DIAN, el MINISTERIO DE TRANSPORTE, un banco o un concesionario, NO es valido. El emisor manda sobre cualquier otra señal.
PASO 2. Busca el objeto: debe certificar la EXISTENCIA de una persona juridica y decir QUIEN la representa. Un documento de la camara que solo certifique una inscripcion de un acto suelto, sin razon social ni representante, NO sirve.
PASO 3. Solo si los dos pasos anteriores dan positivo, es_valido = true.

LO QUE NO DEBES EXIGIR — LEE ESTO ANTES DE RECHAZAR:
1. NO exijas que venga la CEDULA DEL REPRESENTANTE. Suele cargarse aparte. Si aparece, dilo en incluye_cedula_representante; su ausencia NO es motivo de rechazo.
2. NO exijas firma manuscrita ni sello humedo: estos certificados se expiden en linea y se validan con el codigo de verificacion.
3. NO exijas que el certificado sea reciente. Si esta vencido para el tramite, eso se informa con la fecha, no se rechaza el documento.
4. NO rechaces por la longitud: estos certificados tienen entre 1 y 25 paginas y el grueso es objeto social y facultades.

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos (certificado + cedula + RUT + otros), identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el certificado de la camara. Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro, NO lo invalida y NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.

EL NIT Y LA RAZON SOCIAL — SON LOS DATOS POR LOS QUE SE PIDE EL DOCUMENTO:
El tramite los compara con los de la empresa que figura como parte, asi que hay que leerlos exactos.
- razon_social: la que aparece bajo "NOMBRE, IDENTIFICACION Y DOMICILIO" o "RAZON SOCIAL", completa y con su sufijo (S.A.S., LTDA., S.A., y C.I. o similares si los lleva).
- nit: SOLO DIGITOS, sin puntos, sin guion y SIN EL DIGITO DE VERIFICACION. Si ves "900.485.418-1", pon "900485418".
- NO confundas el NIT con la MATRICULA MERCANTIL ni con el NUMERO DE RECIBO ni con el codigo de verificacion: son numeros distintos que aparecen cerca. La matricula va en su propio campo.

LA VIGENCIA — INFORMA, NO BLOQUEA:
- fecha_expedicion: la del CERTIFICADO ("Fecha de expedicion" o "Fecha Expedicion"), NO la de matricula ni la de renovacion.
- ultimo_ano_renovado: el año que diga "Ultimo año renovado". Si la sociedad no ha renovado la matricula del año en curso, el certificado lo dice y es un dato relevante.
- estado_sociedad: "activa" si nada indica lo contrario; "disuelta", "en_liquidacion" o "cancelada" si el certificado lo declara; "no_determinado" si no se puede saber.

EL REPRESENTANTE LEGAL:
Busca el apartado de NOMBRAMIENTOS o REPRESENTACION LEGAL y toma al representante legal PRINCIPAL, no al suplente. Si solo hay suplente, ponlo e indicalo en el cargo. Copia su documento si aparece.

SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

EXTRAER:
- legibilidad: "buena" | "parcial" | "mala"
- tipo_documento: "certificado_camara_comercio" | "rut" | "cedula" | "certificado_rues" | "ficha_homologacion" | "documento_en_blanco" | "otro"
- es_valido: true/false
- paginas_documento: [paginas], total_paginas: numero
- camara_emisora: la camara que lo expide, tal cual aparece
- razon_social, nit
- matricula_mercantil: el numero de matricula, si aparece
- fecha_expedicion (YYYY-MM-DD)
- ultimo_ano_renovado
- estado_sociedad: "activa" | "disuelta" | "en_liquidacion" | "cancelada" | "no_determinado"
- representante_legal_nombre, representante_legal_documento, representante_legal_cargo
- incluye_cedula_representante: true/false
- domicilio: ciudad del domicilio principal
- codigo_verificacion: si aparece
- observaciones: si es_valido es false, explica en una frase QUE es el documento y por que no sirve

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"certificado_camara_comercio","es_valido":true,"paginas_documento":[1],"total_paginas":1,"camara_emisora":"","razon_social":"","nit":"","matricula_mercantil":"","fecha_expedicion":"","ultimo_ano_renovado":"","estado_sociedad":"activa","representante_legal_nombre":"","representante_legal_documento":"","representante_legal_cargo":"","incluye_cedula_representante":false,"domicilio":"","codigo_verificacion":"","observaciones":""}
""";

    /// <summary>
    /// HU #12037 — <b>Certificado CEPD</b> (<c>certificado_ambiental</c>, ya en el catálogo;
    /// <c>id_attached_gas</c> en V1).
    /// <para><b>El CEPD no es un documento: es una sección.</b> Buscar un papel llamado «CEPD» no da nada
    /// —igual que ninguno de los 43 paz y salvo con texto decía «PAZ Y SALVO»—. Lo que acredita las
    /// emisiones es la <b>Ficha Técnica de Homologación del Ministerio de Transporte</b> (FORMATO FTH-002,
    /// «CARACTERÍSTICAS TÉCNICO-MECÁNICAS DE VEHÍCULOS», con un número tipo <c>A00201725</c>), en cuya
    /// sección «EMISIONES» están el CO y los HC en prueba estática, el CO/HC/NOx en prueba dinámica y el
    /// <c>% DE OPACIDAD</c> para diésel.</para>
    /// <para><b>Lo que de verdad hay en la casilla,</b> medido sobre 277 adjuntos recientes de 7
    /// secretarías: <b>58,8 % son listas de chequeo del concesionario</b>, 17,3 % son la ficha, y
    /// certificados de emisiones sueltos, <b>cero</b>. Rechazar seis de cada diez cargas es, por sí solo,
    /// el valor de este prompt.</para>
    /// <para><b>Hicieron falta DOS muestras, y la primera engañaba.</b> Estratificada por secretaría, sus
    /// 7 fichas resultaron ser 5 escaneos de UNA sola ficha de un solo camión: decir «marca 100 %» sobre
    /// eso habría sido decir «lee bien este documento». Estratificando por VEHÍCULO aparecieron <b>19
    /// fichas distintas</b> —Renault, VW, Ford, Chevrolet, Kia, Mercedes, International, JAC, RAM—, y sobre
    /// ellas: marca 26/26, referencia 26/26, sección de emisiones detectada 26/26, con decisión y número de
    /// ficha <b>idénticos entre dos corridas</b>.</para>
    /// <para><b>El caso que solo enseña una muestra diversa:</b> un vehículo ELÉCTRICO deja la sección de
    /// emisiones enteramente vacía, y el prompt lo rechazaba con un razonamiento impecable y una conclusión
    /// falsa. Su ficha es exactamente el documento pedido: no quema combustible, luego no hay nada que
    /// medir. Con la primera muestra —un camión diésel repetido— ese caso no habría aparecido nunca.</para>
    /// <para><b>Depende de la HU #12036.</b> Sin el enderezado previo, este mismo prompt <i>inventaba</i>
    /// sobre las fichas giradas: CHEVROLET CAVALIER y HYUNDAI ELANTRA para un FOTON, con tres números de
    /// ficha distintos. Tras enderezar, el número salió <c>A00201725</c> en 7 de 7.</para>
    /// <para><b>La cilindrada se extrae pero NO se pinta en el resumen.</b> 52-56 % literal, y desglosando:
    /// 5 vinieron vacías, 3 son diferencia legítima de fuente (la ficha da el desplazamiento nominal
    /// <c>3500</c> y V1 el exacto <c>3496</c>) y 4 son lecturas equivocadas — un 16 % de error real sobre
    /// un dato que el trámite ya tiene. Misma decisión que con el NIT del arrendador en el leasing: un dato
    /// que nadie ve no puede inducir a error.</para>
    /// </summary>
    private const string CertificadoAmbiental =
"""
Analiza este documento. Determina si acredita que el vehiculo CUMPLE LOS LIMITES DE EMISIONES contaminantes exigidos en Colombia (lo que el tramite pide como "Certificado CEPD", certificado de emisiones por prueba dinamica).

QUE SE CONSIDERA VALIDO — LO QUE IMPORTA ES QUE CERTIFIQUE LAS EMISIONES:
Este requisito NO se acredita con un documento de formato unico. En la practica llega de dos formas y las DOS sirven:
1. LA FICHA TECNICA DE HOMOLOGACION del Ministerio de Transporte: "FORMATO FTH-002", encabezada por "MINISTERIO DE TRANSPORTE / DIRECCION DE TRANSPORTE Y TRANSITO / SUBDIRECCION DE TRANSPORTE" y titulada "CARACTERISTICAS TECNICO-MECANICAS DE VEHICULOS". Lleva un numero de ficha (por ejemplo A00201725 o P00114156) y esta dividida en secciones numeradas. Es VALIDA porque incluye la seccion "EMISIONES", que es exactamente lo que el tramite pide acreditar. Suele tener dos hojas: la seccion de emisiones esta en la HOJA No. 2.
2. UN CERTIFICADO DE EMISIONES INDEPENDIENTE, si lo hubiera: cualquier documento cuyo objeto sea certificar las emisiones o la opacidad del vehiculo (prueba dinamica, gases de escape, maximos permisibles), emitido por el fabricante, el ensamblador, el importador o una autoridad.

QUE NO ES VALIDO:
1. Un CHECK LIST de concesionario. Es lo que mas se carga por error en esta casilla. Se reconoce por el titulo "CHECK LIST MATRICULAS INICIALES" o "CHECK LIST VEHICULOS", y por sus campos: "Proveedor", "Fecha de Recepcion", "Tipo de servicio", "# Orden de Compra", "Numero de Factura". Es un control interno del concesionario y NO certifica emisiones.
2. Un CERTIFICADO DE CAMARA DE COMERCIO o de existencia y representacion legal.
3. Una FACTURA de venta, una orden de compra o un RUT.
4. Un contrato de PRENDA, una IMPRONTA, una cedula, una licencia de transito o una declaracion de importacion.
5. Una PAGINA EN BLANCO o una plantilla vacia.

COMO DECIDIR — PROCEDE EN ESTE ORDEN:
PASO 1. Busca al emisor y el objeto: el Ministerio de Transporte con el formato FTH-002, o un documento que certifique emisiones. Si lo que tienes delante es un control de un concesionario, un banco o un proveedor, NO es valido.
PASO 2. Busca la SECCION DE EMISIONES: un apartado titulado "EMISIONES" con monoxido de carbono, hidrocarburos, oxidos de nitrogeno u opacidad. En la ficha suele ser la seccion 9 u 11.
PASO 3. Solo si los dos pasos anteriores dan positivo, es_valido = true.

LO QUE NO DEBES EXIGIR — LEE ESTO ANTES DE RECHAZAR:
1. NO exijas la PLACA ni el VIN ni el numero de chasis. La ficha homologa un MODELO, no un vehiculo concreto: es normal que en OBSERVACIONES ponga "NUMERO VIN: N/A". Su ausencia NO es motivo de rechazo, y no debes inventarlos ni tomarlos de otro documento del archivo.
2. NO exijas que los recuadros de emisiones tengan numeros. Un vehiculo DIESEL deja vacios los campos de gasolina y solo rellena "% DE OPACIDAD"; uno de gasolina deja vacia la opacidad. Que la mitad de la tabla este en blanco es lo NORMAL y NO es motivo de rechazo: lo que se exige es que la SECCION EXISTA.
3. UN VEHICULO ELECTRICO DEJA LA SECCION DE EMISIONES ENTERAMENTE VACIA, y eso es CORRECTO: no quema combustible, luego no hay CO, HC, NOx ni opacidad que medir. Lo reconoceras porque el combustible dice "ELECTRICO", la cilindrada es 0 o esta vacia, y a veces una observacion aclara que no tiene disposicion de cilindros. Su ficha es EXACTAMENTE el documento que el tramite pide y es_valido va en true. Rechazarla por no traer valores de emisiones seria rechazar el documento correcto. En ese caso pon combustible = "ELECTRICO" y deja los campos de emisiones vacios.
4. NO exijas firmas ni sellos.

DOCUMENTO ESCANEADO Y CASI SIEMPRE GIRADO:
Estas fichas se escanean apaisadas y las paginas suelen venir GIRADAS 90 GRADOS. Leelas igual, girando la lectura; que la pagina este de lado NO es motivo de rechazo. La mayoria no trae capa de texto.

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos, identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el documento solicitado (ej: [1,2]). Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro, NO lo invalida y NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.
LA PROPORCION NO CUENTA: una sola hoja con la ficha basta para que es_valido sea true.

LAS EMISIONES — ES EL DATO POR EL QUE SE PIDE EL DOCUMENTO:
Pon tiene_seccion_emisiones en true solo si VES el apartado de emisiones, aunque sus casillas esten vacias.
Rellena los valores que encuentres, dejando vacios los que no aparezcan:
- emisiones_co_ralenti: "% POR VOLUMEN DE MONOXIDO DE CARBONO" en prueba estatica o ralenti
- emisiones_hc_ralenti: "PARTES POR MILLON DE HIDROCARBUROS" en prueba estatica o ralenti
- emisiones_co_dinamica, emisiones_hc_dinamica, emisiones_nox_dinamica: los gr/km de la prueba dinamica
- opacidad_diesel: el "% DE OPACIDAD" de los motores diesel (ACPM)
- tiene_canister: true/false segun la casilla CANISTER; deja false si no aparece
NO conviertas ni calcules nada: transcribe el numero tal cual.

EL VEHICULO — CADA DATO EN SU CASILLA:
La ficha reparte los datos del vehiculo en secciones numeradas, y confundirlas es el error mas facil:
- vehiculo_marca sale de "MARCA" dentro de "CARACTERISTICAS GENERALES (CHASIS)". Es una marca de vehiculo (FOTON, HINO, KIA, CHEVROLET...). NUNCA pongas ahi la referencia ni un codigo alfanumerico: si lo que ibas a escribir lleva digitos y guiones, NO es la marca.
- vehiculo_referencia sale de "REFERENCIA" en esa misma seccion, y es lo que el tramite llama linea: una cadena alfanumerica larga tipo "BJ1186VLPHN-5A" o "NHR55E". Copiala caracter a caracter, con sus guiones, sin corregirla ni completarla.
- OJO: la ficha trae VARIAS marcas mas abajo —la del MOTOR, la de los EJES, la de la DIRECCION, la de los FRENOS, la de la CARROCERIA— y NO son la marca del vehiculo. Si lees CUMMINS, WABCO, ZF o similares, ese es un componente.

LA CILINDRADA — LEELA DIGITO A DIGITO:
El "DESPLAZAMIENTO" del motor, en cm3, es lo que el tramite llama cilindraje. Esta dentro de la seccion "MOTOR", no en la de la carroceria ni en la de los pesos, que estan llenas de numeros de cuatro cifras parecidos (capacidades en Kg, longitudes en mm).
Estos escaneos son de mala calidad y el PRIMER digito es el que mas se pierde. Antes de darla por buena, mira el numero completo carácter a carácter y comprueba que el valor tiene sentido como cilindrada: los turismos y camionetas van de 1000 a 4000 cm3 y los camiones de 4000 a 13000. Si dudas de un digito, di el numero que ves; no lo redondees ni lo completes.

EL MODELO de la ficha es el año homologado y puede NO coincidir con el año del vehiculo del tramite. Transcribe el que ves y no lo ajustes.

SI NO PUEDES LEERLO, DILO — NO LO ADIVINES:
Estos documentos llegan escaneados y a veces la calidad no da. Antes de responder, comprueba si de
verdad estas LEYENDO los campos o si estas completando con lo que suele haber en un documento asi.
- legibilidad = "buena" si distingues con claridad el texto de los campos que devuelves.
- legibilidad = "parcial" si el grueso se lee pero hay campos concretos que no distingues con seguridad.
- legibilidad = "mala" si la imagen esta tan borrosa, tan oscura o tan torcida que no puedes leer los
  datos: en ese caso deja los campos VACIOS y no propongas ninguno.
Un campo que no puedes leer va VACIO. Vacio es una respuesta correcta y util; un valor plausible pero
inventado no lo es, porque nadie podra distinguirlo de uno bueno.

EXTRAER:
- legibilidad: "buena" | "parcial" | "mala"
- tipo_documento: "ficha_homologacion" | "certificado_emisiones" | "check_list_concesionario" | "camara_comercio" | "factura" | "prenda" | "impronta" | "aduana" | "documento_en_blanco" | "otro"
- es_valido: true/false
- paginas_documento: [paginas], total_paginas: numero
- numero_ficha: el numero de ficha (ej "A00201725"), vacio si no lo trae
- fecha_ficha (YYYY-MM-DD)
- tipo_homologacion: lo que diga el campo "TIPO DE HOMOLOGACION" (ej "CHASIS", "VEHICULO", "CARROCERIA")
- clase_vehiculo, tipo_carroceria, servicio
- vehiculo_marca, vehiculo_referencia, vehiculo_modelo
- motor_marca, cilindrada, combustible, numero_ejes, numero_sillas
- capacidad: la capacidad o carga util, en kg, solo digitos
- tiene_seccion_emisiones: true/false
- emisiones_co_ralenti, emisiones_hc_ralenti, emisiones_co_dinamica, emisiones_hc_dinamica, emisiones_nox_dinamica, opacidad_diesel
- tiene_canister: true/false
- certificado_por: quien firma que las caracteristicas coinciden (ensamblador, importador o carrozador)
- observaciones: si es_valido es false, explica en una frase QUE es el documento y por que no sirve

JSON valido sin markdown:
{"legibilidad":"buena","tipo_documento":"ficha_homologacion","es_valido":true,"paginas_documento":[1,2],"total_paginas":2,"numero_ficha":"","fecha_ficha":"","tipo_homologacion":"","clase_vehiculo":"","tipo_carroceria":"","servicio":"","vehiculo_marca":"","vehiculo_referencia":"","vehiculo_modelo":"","motor_marca":"","cilindrada":"","combustible":"","numero_ejes":"","numero_sillas":"","capacidad":"","tiene_seccion_emisiones":true,"emisiones_co_ralenti":"","emisiones_hc_ralenti":"","emisiones_co_dinamica":"","emisiones_hc_dinamica":"","emisiones_nox_dinamica":"","opacidad_diesel":"","tiene_canister":false,"certificado_por":"","observaciones":""}
""";

    private const string MandatoConfig =
"""
Analiza este documento. Debe ser un CONTRATO PRIVADO DE MANDATO, poder o autorizacion a un apoderado
para tramites de transito en Colombia (FLIT / organismos de transito / uniones temporales).

NO es valido si es: FUR, SOAT, RTM, factura, cedula, compraventa, solicitud virtual sola.

Heuristica de plantilla sugerida (suggestedTemplateCode):
- "sabaneta" si menciona UT-SETSA, SETSA o Sabaneta como mandatario institucional
- "bello" si menciona UT-MAB, MAB o Bello como mandatario institucional / union temporal
- "generico" en cualquier otro caso (mandatario persona natural)

IMPORTANTE — DOCUMENTO MULTIPAGINA:
Si el PDF contiene MULTIPLES documentos (factura + FUR + improntas + etc.), identifica SOLO las paginas que corresponden al tipo solicitado.
- paginas_documento: array con los numeros de pagina donde esta el documento solicitado (ej: [1,2] o [3] o [1]). Base 1.
- total_paginas: total de paginas del PDF
Si el documento solicitado NO esta en el PDF, paginas_documento debe ser un array vacio [].

ALCANCE DE LAS VALIDACIONES — REGLA CRITICA:
Que el archivo sea un EXPEDIENTE COMPLETO de tramite, con otros documentos dentro (FUR, mandato,
poder, licencia de transito, declaracion de importacion, factura, escrituras...), NO lo invalida y
NO es motivo de rechazo. Las VALIDACIONES del principio se aplican SOLO a las paginas que pusiste
en paginas_documento, NUNCA al archivo entero. Si localizas el documento solicitado dentro del
expediente, es_valido va en true aunque el resto del archivo sea otra cosa. Devuelve es_valido en
false unicamente cuando el documento solicitado NO aparece en ninguna pagina del archivo.

EXTRAER JSON (sin markdown):
- paginas_documento: [paginas], total_paginas: numero
- suggestedTemplateCode: "generico" | "sabaneta" | "bello"
- requiresForNaturalPerson: true si el texto implica que aplica tambien a persona natural; si no false
- mandataryFamily: "organismo_transito" si el mandatario es una UT/organismo; "individuo" si es una persona
- institutionalMandataryName: razon social del mandatario institucional (si aplica)
- institutionalMandataryNit: NIT del mandatario institucional (si aplica)
- chamberCity: ciudad de la Camara de Comercio mencionada (si aparece)
- mandatarySigla: sigla tipo UT-SETSA / UT-MAB (si aparece)
- notes: breve observacion

JSON valido sin markdown:
{"suggestedTemplateCode":"generico","requiresForNaturalPerson":false,"mandataryFamily":"individuo","institutionalMandataryName":"","institutionalMandataryNit":"","chamberCity":"","mandatarySigla":"","notes":"","paginas_documento":[1],"total_paginas":1}
""";
}
