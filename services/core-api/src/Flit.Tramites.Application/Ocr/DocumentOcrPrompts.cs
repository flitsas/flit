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
            // Solo extract de Plataforma → Mandatos; el lote de trámites NO lo solicita.
            "mandato_config",
        };

    /// <summary>true si <paramref name="tipo"/> tiene prompt OCR asociado.</summary>
    public static bool IsSupported(string? tipo) => tipo is not null && SupportedTipos.Contains(tipo);

    /// <summary>Devuelve el prompt del tipo, o null si el tipo no está soportado.</summary>
    public static string? PromptFor(string tipo) => tipo switch
    {
        "factura" => Factura,
        "aduana" => Aduana,
        "impronta" => Impronta,
        "soat" => Soat,
        "rtm" => Rtm,
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

LECTURA DEL VIN Y EL NUMERO DE MOTOR:
Leelos caracter por caracter, exactamente como aparecen. NO completes, NO corrijas, NO deduzcas.
Presta maxima atencion a los caracteres que se confunden: 0 vs O vs D, 1 vs I vs l, 5 vs S, 8 vs B vs
6, 2 vs Z, 6 vs G. El VIN tiene EXACTAMENTE 17 caracteres y nunca contiene las letras I, O ni Q. Si
lees mas o menos de 17, vuelve a mirar.

EXTRAER:
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
{"tipo_documento":"factura_electronica","es_factura_valida":true,"paginas_documento":[1],"total_paginas":1,"numero_factura":"","fecha":"","resolucion_dian":"","emisor_nit":"","emisor_nombre":"","emisor_direccion":"","emisor_ciudad":"","comprador_nombre":"","comprador_documento":"","comprador_tipo_doc":"CC","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","vehiculo_color":"","vehiculo_vin":"","vehiculo_motor":"","vehiculo_clase":"","vehiculo_cilindraje":"","vehiculo_placa":"","subtotal":0,"iva":0,"total":0,"forma_pago":"","cufe":"","observaciones":""}
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

JSON valido sin markdown:
{"tipo_documento":"declaracion_importacion","es_valido":true,"paginas_documento":[1],"total_paginas":1,"numero_documento":"","fecha":"","aduana":"","importador_nombre":"","importador_nit":"","importador_direccion":"","importador_ciudad":"","agente_aduana":"","agente_aduana_nit":"","pais_origen":"","pais_procedencia":"","puerto_entrada":"","subpartida_arancelaria":"","tipo_vehiculo":"automovil","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","vehiculo_vin":"","vehiculo_motor":"","vehiculo_chasis":"","vehiculo_cilindraje":"","vehiculo_color":"","vehiculo_clase":"","vehiculo_combustible":"","vehiculo_pasajeros":"","vehiculo_peso_bruto":"","cantidad":1,"ampara_multiples_vehiculos":false,"valor_fob_usd":0,"valor_flete_usd":0,"valor_seguro_usd":0,"valor_cif_usd":0,"valor_cif_cop":0,"tasa_cambio":0,"arancel_porcentaje":0,"arancel_valor":0,"iva_porcentaje":0,"iva_valor":0,"total_tributos":0,"regimen":"","observaciones":""}
""";

    private const string Impronta =
"""
Analiza este documento. Determina si contiene un CERTIFICADO DE IMPRONTAS, HOJA DE IMPRONTAS DIGITALES, o ACTA DE IMPRONTAS de un vehiculo o motocicleta en Colombia.

VALIDACIONES:
1. DEBE ser uno de estos documentos de identificacion vehicular: Certificado de Improntas Digitales, Hoja de Improntas Digitales del Vehiculo, Acta de Improntas, Informe Pericial de Identificacion Vehicular, o Fotoimpronta certificada
2. NO es valido si es: una factura de venta, un FUR (Formulario Unico de Registro), una declaracion de importacion, una poliza SOAT, un certificado de revision tecnico-mecanica RTM (a menos que incluya seccion de improntas), un recibo de pago, un contrato
3. DEBE contener al menos UNO de estos numeros de identificacion del vehiculo: numero de motor, numero de chasis, VIN, o numero de serie
4. DEBE contener datos del vehiculo (marca, modelo como minimo)
5. Para ser valido el documento debe tener origen en: CDA (Centro de Diagnostico Automotor), VUS (Ventanilla Unica de Servicios), organismo de transito, DIJIN, o entidad certificada

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

JSON valido sin markdown:
{"tipo_documento":"certificado_improntas","es_valido":true,"paginas_documento":[1],"total_paginas":1,"numero_certificado":"","fecha":"","entidad_emisora":"","entidad_nit":"","entidad_ciudad":"","inspector_nombre":"","inspector_documento":"","vehiculo_placa":"","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","vehiculo_color":"","vehiculo_clase":"","vehiculo_servicio":"","vehiculo_vin":"","vehiculo_vin_datos":"","vehiculo_motor":"","vehiculo_motor_datos":"","vehiculo_chasis":"","vehiculo_chasis_datos":"","vehiculo_serie":"","estado_motor":"no_verificado","estado_chasis":"no_verificado","estado_vin":"no_verificado","estado_serie":"no_verificado","tiene_qr":false,"tiene_hash":false,"hash_valor":"","resolucion_referencia":"","alertas":[],"observaciones":""}
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

EXTRAER:
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
{"tipo_documento":"soat","es_valido":true,"paginas_documento":[1],"total_paginas":1,"numero_poliza":"","aseguradora":"","fecha_expedicion":"","fecha_inicio":"","fecha_vencimiento":"","estado_poliza":"no_determinado","vehiculo_placa":"","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","vehiculo_clase":"","vehiculo_vin":"","tomador_nombre":"","tomador_documento":"","valor_prima":0,"observaciones":""}
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

EXTRAER:
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
{"tipo_documento":"rtm","es_valido":true,"paginas_documento":[1],"total_paginas":1,"numero_certificado":"","cda_expide":"","cda_nit":"","fecha_expedicion":"","fecha_vigencia":"","fecha_vencimiento":"","estado":"no_determinado","resultado":"no_determinado","vehiculo_placa":"","vehiculo_marca":"","vehiculo_linea":"","vehiculo_modelo":"","vehiculo_clase":"","vehiculo_vin":"","observaciones":""}
""";

    /// <summary>
    /// Extract de config de mandato para Plataforma → Mandatos. NO se usa en el lote de trámites.
    /// El PDF subido solo aporta datos; el documento oficial se regenera con diseño FLIT.
    /// </summary>
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
