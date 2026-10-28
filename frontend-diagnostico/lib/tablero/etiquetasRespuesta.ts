// Respuesta de la fuente en español (HU #14087, página del detalle en pestaña nueva). La API no cambia: este módulo decide qué
// claves de `respuesta_fuente` (proto-JSON de los *Response de core-consultas) o del `resultado` resumido se pintan, cómo se
// llaman en pantalla y cómo se leen sus valores internos. Cubre todos los campos de los mensajes de respuesta de
// contracts/proto/flit/consultas/v1 (runt_intempo, resultado, certificaciones y ConsultarAvaluosResponse) y los del resumen
// del DTO de detalle; el test de contrato (etiquetasRespuesta.test.ts) falla si aparece un campo nuevo sin traducir.

type Objeto = Record<string, unknown>;

const esObjeto = (v: unknown): v is Objeto => v !== null && typeof v === "object" && !Array.isArray(v);

/**
 * Auditoría del proveedor y campos internos: nunca se pintan, en ningún nivel. Además se ocultan todos los `id_*` (internos del
 * RUNT), `campos` cuando es la lista de ResultadoConsulta, `resultado` cuando no es objeto (enum del RUNT Intempo) y, dentro de
 * los chequeos, `clave` y `fuente`.
 */
export const CLAVES_OCULTAS: ReadonlySet<string> = new Set([
  "proveedor",
  "semaforo",
  "desde_cache",
  "consultado_en",
  "respuesta_cruda",
  "http_status",
  "duracion_ms",
  "errores",
  "tipo_error",
  "mensaje_error",
  "serial_version_uid",
  "serialVersionUID",
  "id",
  "id_tipo_documento",
  "id_departamento",
  "id_ciudad",
  "id_prenda",
  "id_vehiculo_prenda",
]);

/** Ocultas solo dentro de un chequeo de ResultadoConsulta. */
export const CLAVES_OCULTAS_EN_CHEQUEOS: ReadonlySet<string> = new Set(["clave", "fuente"]);

/**
 * Objetos que se aplanan (su contenido sale directo en el padre): `resultado` (ResultadoConsulta que envuelven Conductor, Multas
 * y RUES) y el mapa libre `campos` de AccidenteIntempo. Si no son objeto, se ocultan.
 */
export const CLAVES_APLANADAS: ReadonlySet<string> = new Set(["resultado", "campos"]);

/** Nombres de campo en español (snake_case del .proto y json_name explícitos). */
export const ETIQUETAS_RESPUESTA: Readonly<Record<string, string>> = {
  // Genéricos y del resumen del DTO de detalle
  placa: "Placa",
  vin: "VIN",
  servicio: "Servicio",
  clase: "Clase",
  organismo: "Organismo",
  propietarios: "Propietarios",
  locatarios: "Locatarios",
  tipo_documento: "Tipo de documento",
  tipo_propiedad: "Tipo de propiedad",
  numero_documento: "Número de documento",
  cantidad_comparendos: "Cantidad de comparendos",
  comparendos: "Comparendos",
  comparendos_truncados: "Comparendos recortados",
  numero: "Número",
  fecha: "Fecha",
  valor: "Valor",
  estado: "Estado",
  sin_valor_comercial: "Sin valor comercial",
  valor_sugerido: "Valor sugerido",
  fuente_principal: "Fuente principal",
  fuentes: "Fuentes",
  fuente: "Fuente",
  muestras: "Muestras",
  monto_total: "Monto total",
  vigencias: "Vigencias",
  anio: "Año",
  pagada: "Pagada",
  inscrito: "Inscrito",
  estado_conductor: "Estado del conductor",
  licencias: "Licencias",
  categoria: "Categoría",
  vigente_hasta: "Vigente hasta",
  encontrada: "Encontrada",
  estado_matricula: "Estado de la matrícula",
  ultima_renovacion: "Última renovación",
  persona: "Persona",
  nombre: "Nombre",
  nombre_completo: "Nombre completo",
  departamento: "Departamento",
  municipio: "Municipio",
  ciudad: "Ciudad",
  direccion: "Dirección",
  direcciones: "Direcciones",
  mensaje: "Mensaje",
  // RUNT Intempo: ConsultarVehiculoIntempoResponse, ErrorIntempo y VehiculoIntempo
  vehiculo: "Vehículo",
  code: "Código",
  description: "Descripción",
  plate: "Placa",
  document_type: "Tipo de documento",
  document_number: "Número de documento",
  informacion_general: "Información general",
  datos_tecnicos: "Datos técnicos",
  soat: "SOAT",
  tecno_mecanica: "Revisión técnico-mecánica",
  garantias_favor_de: "Garantías a favor de",
  garantias_mobiliarias: "Garantías mobiliarias",
  informacion_blindaje: "Información de blindaje",
  limitacion_propiedad: "Limitaciones a la propiedad",
  normalizacion_saneamiento: "Normalización y saneamiento",
  polizas_responsabilidad_civil: "Pólizas de responsabilidad civil",
  solicitudes: "Solicitudes",
  tarjeta_operacion: "Tarjeta de operación",
  propietarios_actuales: "Propietarios actuales",
  historico_propietarios: "Histórico de propietarios",
  owner: "Propietarios",
  lessee: "Locatarios",
  accident_rate: "Siniestralidad",
  infracciones: "Infracciones",
  // InformacionGeneralIntempo
  cilidraje: "Cilindraje",
  clase_vehiculo: "Clase de vehículo",
  clasificacion: "Clasificación",
  color: "Color",
  linea: "Línea",
  marca: "Marca",
  modelo: "Modelo",
  fecha_matricula: "Fecha de matrícula",
  no_chasis: "N.º de chasis",
  no_licencia_transito: "N.º de licencia de tránsito",
  no_motor: "N.º de motor",
  no_placa: "N.º de placa",
  no_vin: "N.º de VIN",
  organismo_transito: "Organismo de tránsito",
  puertas: "Puertas",
  tipo_carroceria: "Tipo de carrocería",
  tipo_combustible: "Tipo de combustible",
  tipo_servicio: "Tipo de servicio",
  es_regrabado_chasis: "Chasis regrabado",
  es_regrabado_motor: "Motor regrabado",
  es_regrabado_serie: "Serie regrabada",
  es_regrabado_vin: "VIN regrabado",
  prendas: "Prendas",
  tiene_gravamenes: "Tiene gravámenes",
  repotenciado: "Repotenciado",
  clasico_antiguo: "Clásico o antiguo",
  estado_del_vehiculo: "Estado del vehículo",
  mostrar_solicitudes: "Mostrar solicitudes",
  seguridad_estado: "Seguridad del Estado",
  tarjeta_servicio: "Tarjeta de servicio",
  ver_valida_dian: "Ver validación DIAN",
  verValidaDIAN: "Ver validación DIAN",
  validacion_dian: "Validación DIAN",
  validacionDIAN: "Validación DIAN",
  tiene_lt_importacion: "Tiene licencia de tránsito de importación",
  tieneLTImportacion: "Tiene licencia de tránsito de importación",
  // DatosTecnicosIntempo
  capacidad_carga: "Capacidad de carga",
  no_ejes: "N.º de ejes",
  peso_bruto_vehicular: "Peso bruto vehicular",
  pasajeros_sentados: "Pasajeros sentados",
  alto: "Alto",
  ancho: "Ancho",
  largo: "Largo",
  rodaje: "Rodaje",
  // SoatIntempo y TecnoMecanicaIntempo
  no_poliza: "N.º de póliza",
  fecha_expedicion: "Fecha de expedición",
  fecha_vigencia: "Fecha de vigencia",
  fecha_vencimiento: "Fecha de vencimiento",
  entidad_expide_soat: "Entidad que expide el SOAT",
  nro_certificado: "N.º de certificado",
  tipo_revision: "Tipo de revisión",
  fecha_vigente: "Vigente hasta",
  cda_expide: "CDA que expide",
  vigente: "Vigente",
  numero_placa: "Número de placa",
  informacion_consistente: "Información consistente",
  url: "URL",
  // Garantías
  tipo_documento_acreedor: "Tipo de documento del acreedor",
  numero_documento_acreedor: "Número de documento del acreedor",
  acreedor: "Acreedor",
  fecha_inscripcion: "Fecha de inscripción",
  confecamaras: "Confecámaras",
  fecha_registro: "Fecha de registro",
  tipo_documento_entidad: "Tipo de documento de la entidad",
  numero_documento_entidad: "Número de documento de la entidad",
  entidad: "Entidad",
  // InformacionBlindajeIntempo
  blindado: "Blindado",
  nivel_blindaje: "Nivel de blindaje",
  nivel_blindaje_numero: "Número del nivel de blindaje",
  fecha_blindaje: "Fecha de blindaje",
  numero_resolucion: "Número de resolución",
  tipo_blindaje_nombre: "Tipo de blindaje",
  fecha_expedicion_certificado: "Fecha de expedición del certificado",
  fecha_expedicion_certificado_formato_ws: "Fecha de expedición del certificado (formato del servicio)",
  fechaExpedicionCertificadoFormatoWS: "Fecha de expedición del certificado (formato del servicio)",
  autorizacion: "Autorización",
  // LimitacionPropiedadIntempo y TarjetaOperacionIntempo
  tipo_limitacion: "Tipo de limitación",
  no_documento: "N.º de documento",
  entidad_juridica: "Entidad jurídica",
  tipo_documento_demandante: "Tipo de documento del demandante",
  no_documento_demandante: "N.º de documento del demandante",
  fecha_radicacion: "Fecha de radicación",
  modalidad_servicio: "Modalidad de servicio",
  // Propietarios, direcciones, locatarios e histórico
  detalle_propiedad: "Detalle de la propiedad",
  telefono: "Teléfono",
  email: "Correo electrónico",
  fecha_actualizacion: "Fecha de actualización",
  tipo: "Tipo",
  tdoc: "Tipo de documento",
  ndoc: "Número de documento",
  nombre_razon_social: "Nombre o razón social",
  nombre_completo_propietario: "Nombre completo del propietario",
  fecha_inicio_propiedad: "Fecha de inicio de la propiedad",
  // PersonaIntempo
  name: "Nombre",
  first_name: "Primer nombre",
  second_name: "Segundo nombre",
  first_last_name: "Primer apellido",
  second_last_name: "Segundo apellido",
  lessee_type: "Tipo de locatario",
  // InfraccionIntempo
  nro_comparendo: "N.º de comparendo",
  infraccion: "Infracción",
  descripcion: "Descripción",
  valor_infraccion: "Valor de la infracción",
  reporta_fuga: "Reporta fuga",
  accidente: "Accidente",
  secretaria_transito: "Secretaría de tránsito",
  // ResultadoConsulta, Chequeo, Comparendo, Dato y Avaluo
  chequeos: "Chequeos",
  certificaciones: "Certificaciones",
  etiqueta: "Etiqueta",
  datos: "Datos",
  avaluos: "Avalúos",
  // Certificaciones
  rtm: "RTM",
  matriculas_mercantiles: "Matrículas mercantiles",
  numero_poliza: "Número de póliza",
  aseguradora: "Aseguradora",
  expedicion: "Expedición",
  vigente_desde: "Vigente desde",
  numero_certificado: "Número de certificado",
  cda: "CDA",
  nit: "NIT",
  razon_social: "Razón social",
  numero_matricula: "Número de matrícula",
  matriculada_en: "Matriculada en",
  renovada_en: "Renovada en",
  camara_comercio: "Cámara de comercio",
  representantes_legales: "Representantes legales",
  cargo: "Cargo",
  facultades: "Facultades",
  crudo: "Texto original",
};

/** Valores internos (enums del .proto) en español; el resto de textos de la fuente se queda tal cual. */
const VALORES: Readonly<Record<string, string>> = {
  SEMAFORO_UNSPECIFIED: "Sin especificar",
  SEMAFORO_VERDE: "Verde",
  SEMAFORO_AMARILLO: "Amarillo",
  SEMAFORO_ROJO: "Rojo",
  ESTADO_CHEQUEO_UNSPECIFIED: "Sin especificar",
  ESTADO_CHEQUEO_OK: "OK",
  ESTADO_CHEQUEO_WARN: "Advertencia",
  ESTADO_CHEQUEO_FAIL: "Falla",
  ESTADO_CHEQUEO_UNKNOWN: "Desconocido",
  ESTADO_CHEQUEO_ERROR: "Error",
  ESTADO_AVALUO_UNSPECIFIED: "Sin especificar",
  ESTADO_AVALUO_OK: "OK",
  ESTADO_AVALUO_SIN_DATOS: "Sin datos",
  ESTADO_AVALUO_ERROR: "Error",
  RESULTADO_RUNT_INTEMPO_UNSPECIFIED: "Sin especificar",
  RESULTADO_RUNT_INTEMPO_ENCONTRADO: "Encontrado",
  RESULTADO_RUNT_INTEMPO_NO_ENCONTRADO: "No encontrado",
  RESULTADO_RUNT_INTEMPO_ERROR: "Error",
  TIPO_ERROR_FUENTE_UNSPECIFIED: "Sin error",
  TIPO_ERROR_FUENTE_TIMEOUT: "Tiempo de espera agotado",
  TIPO_ERROR_FUENTE_HTTP_NO_EXITOSO: "Respuesta HTTP no exitosa",
  TIPO_ERROR_FUENTE_SOAP_FAULT: "Falla SOAP",
  TIPO_ERROR_FUENTE_RESPUESTA_INVALIDA: "Respuesta inválida",
  TIPO_ERROR_FUENTE_RED: "Error de red",
  TIPO_ERROR_FUENTE_AUTENTICACION: "Credenciales rechazadas",
  TIPO_ERROR_FUENTE_ESTADO_NO_OK: "Estado no OK",
  TIPO_ERROR_FUENTE_EXCEPCION: "Excepción",
  VIGENCIA_UNSPECIFIED: "Sin especificar",
  VIGENCIA_VIGENTE: "Vigente",
  VIGENCIA_VENCIDO: "Vencido",
  VIGENCIA_NO_APLICA: "No aplica",
};

/** Texto de un valor: Sí/No, «—» para vacío o null, «Sin registros» para listas vacías, enums traducidos; el resto tal cual. */
export function textoValor(v: unknown): string {
  if (v === null || v === undefined || v === "") return "—";
  if (v === true) return "Sí";
  if (v === false) return "No";
  if (Array.isArray(v)) return v.length === 0 ? "Sin registros" : v.map(textoValor).join(", ");
  if (typeof v === "string") return VALORES[v] ?? v;
  if (typeof v === "object") return Object.keys(v).length === 0 ? "Sin registros" : JSON.stringify(v);
  return String(v);
}

/** «numero_de_siniestros» / «fechaUltimoReporte» → «Numero de siniestros» / «Fecha ultimo reporte». */
function humanizar(clave: string): string {
  const texto = clave
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .trim()
    .toLowerCase();
  return texto ? texto[0].toUpperCase() + texto.slice(1) : clave;
}

/** Nombre en español de un campo; si no está en el diccionario (p. ej. claves libres de un mapa), humanizado. */
export function etiquetaCampo(clave: string): string {
  return ETIQUETAS_RESPUESTA[clave] ?? humanizar(clave);
}

const oculta = (clave: string, enChequeo: boolean) =>
  CLAVES_OCULTAS.has(clave) || clave.startsWith("id_") || (enChequeo && CLAVES_OCULTAS_EN_CHEQUEOS.has(clave));

/**
 * Pares [clave, valor] que se pintan de un objeto de la respuesta, en orden: sin las claves ocultas y con `resultado` y
 * `campos` aplanados cuando son objeto (ocultos si no). Una clave repetida tras aplanar se queda con la primera aparición.
 */
export function camposVisibles(objeto: Objeto, enChequeo = false): [string, unknown][] {
  const salida = new Map<string, unknown>();
  const agregar = (o: Objeto) => {
    for (const [clave, valor] of Object.entries(o)) {
      if (oculta(clave, enChequeo)) continue;
      if (CLAVES_APLANADAS.has(clave)) {
        if (esObjeto(valor)) agregar(valor);
        continue;
      }
      if (!salida.has(clave)) salida.set(clave, valor);
    }
  };
  agregar(objeto);
  return [...salida];
}

// Respuesta íntegra del proveedor (HU #14154: RUNT de persona y RUES de empresa). Aquí no se oculta ni se aplana nada: el
// renderizador recorre el JSON tal cual (Object.entries), con ids internos, firma y campos técnicos. Solo cambia cómo se
// llaman las claves en pantalla (español si se conocen; el nombre original si no) y cómo se lee un valor vacío.

/** Nombres en español de las claves del JSON de los proveedores de RUNT persona (conductor) y RUES (camelCase). */
export const ETIQUETAS_PROVEEDOR: Readonly<Record<string, string>> = {
  // Comunes y técnicos
  ok: "OK",
  message: "Mensaje",
  observaciones: "Observaciones",
  metadatos: "Metadatos",
  http_status: "Estado HTTP",
  duracion_ms: "Duración (ms)",
  signature: "Firma",
  hash: "Hash",
  dateTime: "Fecha y hora",
  data: "Datos",
  // RUNT de persona (conductor)
  identidad: "Identidad",
  persona: "Persona",
  tipoDocumento: "Tipo de documento",
  documento: "Documento",
  nombres: "Nombres",
  apellidos: "Apellidos",
  nombreCompleto: "Nombre completo",
  primerNombre: "Primer nombre",
  segundoNombre: "Segundo nombre",
  primerApellido: "Primer apellido",
  segundoApellido: "Segundo apellido",
  estadoPersona: "Estado de la persona",
  estadoConductor: "Estado del conductor",
  nroPazYSalvo: "N.º de paz y salvo",
  tieneLicencias: "Tiene licencias",
  tieneMultas: "Tiene multas",
  licencias: "Licencias",
  detalleLicencia: "Detalle de la licencia",
  numeroLicencia: "Número de licencia",
  estadoLicencia: "Estado de la licencia",
  fechaExpedicion: "Fecha de expedición",
  fechaVencimiento: "Fecha de vencimiento",
  restricciones: "Restricciones",
  restricciones_generales: "Restricciones generales",
  multas: "Multas",
  // RUES de empresa
  commercialRegistry: "Registro mercantil",
  NIT: "NIT",
  businessName: "Razón social",
  acronym: "Sigla",
  registrationStatus: "Estado de la matrícula",
  registrationNumber: "Número de matrícula",
  chamberCommerce: "Cámara de comercio",
  chamberCity: "Ciudad de la cámara",
  chamberDepartment: "Departamento de la cámara",
  idRm: "Id del registro mercantil",
  enrollmentDate: "Fecha de matrícula",
  lastRenewedYear: "Último año renovado",
  renewalDate: "Fecha de renovación",
  commercialAddress: "Dirección comercial",
  companyLocation: "Ubicación de la empresa",
  organizationType: "Tipo de organización",
  companyType: "Tipo de sociedad",
  lastUpdatedDate: "Fecha de última actualización",
  reasonForCancellation: "Motivo de cancelación",
  economicActivities: "Actividades económicas",
  category: "Categoría",
  legalRepresentatives: "Representantes legales",
  documentType: "Tipo de documento",
  documentNumber: "Número de documento",
  position: "Cargo",
  faculty: "Facultades",
};

/** Nombre de una clave de la respuesta del proveedor: en español si se conoce; si no, el nombre original (nunca se descarta). */
export function etiquetaCampoProveedor(clave: string): string {
  return ETIQUETAS_PROVEEDOR[clave] ?? ETIQUETAS_RESPUESTA[clave] ?? clave;
}

/** Texto de un valor del proveedor: «—» vacío, null u objeto vacío; «Sin registros» lista vacía; Sí/No; el resto, tal cual. */
export function textoValorProveedor(v: unknown): string {
  if (v === null || v === undefined || v === "") return "—";
  if (v === true) return "Sí";
  if (v === false) return "No";
  if (Array.isArray(v)) return v.length === 0 ? "Sin registros" : v.map(textoValorProveedor).join(", ");
  if (typeof v === "object") return Object.keys(v).length === 0 ? "—" : JSON.stringify(v);
  return String(v);
}
