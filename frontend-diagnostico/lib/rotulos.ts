// Punto 5 (ola 2026-11-11): ningún código interno visible al usuario. Helper único para pintar cualquier código (alerta,
// fuente, tipo de trámite, familia, estado, motivo de N/A, tipo de identificador…). Regla de pintado (decisión 5.1):
//   1. la etiqueta que manda la API (`etiqueta`, `fuente_etiqueta`, `estado_etiqueta`… según el contrato);
//   2. si falta, el diccionario local de respaldo;
//   3. si tampoco hay, «Novedad sin clasificar» —nunca el código— y un console.warn técnico (contexto y código, sin datos
//      personales).
// Los códigos siguen viajando igual como valores; esto es solo presentación. Este módulo no importa nada de lib/ para que
// cualquier módulo de dominio pueda usarlo sin ciclos.

/** Texto neutro cuando un código no tiene rótulo (AC 4). */
export const ROTULO_SIN_CLASIFICAR = "Novedad sin clasificar";

export type Respaldo = Readonly<Record<string, string>> | ((codigo: string) => string | null | undefined);

export interface Rotular {
  /** Dónde se pinta (para el aviso técnico): «alerta del dictamen», «fuente», «estado del lote»… */
  contexto: string;
  codigo: string | null | undefined;
  /** Etiqueta de la API. Vacía o igual al código cuenta como ausente. */
  etiqueta?: string | null;
  respaldo?: Respaldo;
}

const valida = (texto: string | null | undefined, codigo: string): texto is string => Boolean(texto && texto.trim() && texto.trim() !== codigo);

export function rotular({ contexto, codigo, etiqueta, respaldo }: Rotular): string {
  const c = codigo ?? "";
  if (valida(etiqueta, c)) return etiqueta.trim();
  if (respaldo && c) {
    const local = typeof respaldo === "function" ? respaldo(c) : Object.prototype.hasOwnProperty.call(respaldo, c) ? respaldo[c] : undefined;
    // El diccionario local es de confianza: puede coincidir con el código (VIN).
    if (local && local.trim()) return local;
  }
  console.warn(`[rotulos] Código sin rótulo en «${contexto}»: ${c || "(vacío)"}`);
  return ROTULO_SIN_CLASIFICAR;
}

/** Código interno con forma de enum (MAYÚSCULAS con guion bajo): nunca se muestra como mensaje. */
export function pareceCodigo(texto: string | null | undefined): boolean {
  return Boolean(texto && /^[A-Z][A-Z0-9]*(_[A-Z0-9]+)+$/.test(texto.trim()));
}

/** Rótulos aprobados de las alertas del dictamen (decisión 5.2), respaldo de `AlertaDictamen.etiqueta`. */
export const ALERTAS_DICTAMEN: Readonly<Record<string, string>> = {
  SOAT_NO_VIGENTE: "SOAT no vigente",
  RTM_NO_VIGENTE: "Revisión técnico-mecánica no vigente",
  ESTADO_VEHICULO: "Vehículo no activo en el RUNT",
  SIMIT_VEHICULO: "Multas del vehículo",
  SIMIT_PROPIETARIO: "Multas del propietario",
  SIMIT_LOCATARIO: "Multas del locatario",
  SIMIT_SIN_VERIFICAR: "No se pudo verificar multas",
  SIMIT_LICENCIA: "Licencia suspendida o cancelada (SIMIT)",
  LICENCIA_CONDUCTOR: "Licencia de conducción con novedad",
  SIMIT_ACUERDO_PAGO: "Acuerdo de pago vigente",
  LIMITACION: "Limitación a la propiedad",
  GRAVAMEN: "Gravamen registrado",
  GARANTIA_MOBILIARIA: "Garantía mobiliaria registrada",
  PRENDA: "Prenda registrada",
  INFRACCION_RUNT: "Infracciones registradas en el RUNT",
  IMPUESTO: "Impuesto vehicular pendiente",
};

/** Código de la regla «consulta pendiente»: su rótulo lleva el nombre de la fuente. */
export const ALERTA_FUENTE_PENDIENTE = "FUENTE_PENDIENTE";

/** Efecto de una regla del dictamen (BLOQUEA / ADVIERTE). Propuesto por el implementador (decisión 5.3), a revisar en el PR. */
export const EFECTOS_DICTAMEN: Readonly<Record<string, string>> = {
  BLOQUEA: "Bloquea el trámite",
  ADVIERTE: "Advertencia",
};

/** Familias de trámite (respaldo de `familia_etiqueta`). Propuesto (5.3), a revisar en el PR. */
export const FAMILIAS_TRAMITE: Readonly<Record<string, string>> = {
  TRASPASO: "Traspaso",
};

/** Tipos de trámite (respaldo de `tipo_tramite_etiqueta`). Propuesto (5.3), a revisar en el PR. */
export const TIPOS_TRAMITE: Readonly<Record<string, string>> = {
  TRASPASO_STANDARD: "Traspaso estándar",
  TRASPASO_TRANSFERENCIA_DE_DOMINIO: "Traspaso por transferencia de dominio",
  TRASPASO_UNILATERAL: "Traspaso unilateral",
};

/** Tipo de identificador del vehículo (respaldo de `tipo_identificador_etiqueta`). */
export const TIPOS_IDENTIFICADOR: Readonly<Record<string, string>> = {
  PLACA_CARRO: "Placa de carro",
  PLACA_MOTO: "Placa de moto",
  PLACA_REMOLQUE: "Placa de remolque",
  PLACA_MAQUINARIA: "Placa de maquinaria",
  PLACA_MOTOCARRO: "Placa de motocarro",
  VIN: "VIN",
};

/** Rol de una persona en el vehículo. */
export const ROLES_PERSONA: Readonly<Record<string, string>> = {
  PROPIETARIO: "Propietario",
  LOCATARIO: "Locatario",
};

/** Nombre del rol de una persona; desconocido, «Novedad sin clasificar». */
export function nombreRol(rol: string | null | undefined): string {
  return rotular({ contexto: "rol de la persona", codigo: rol, respaldo: ROLES_PERSONA });
}

/**
 * Motivos de «Crear trámite» (Punto 4): por qué una placa no es elegible (grilla y encolado) y por qué se omitió o falló al
 * ejecutar (rechazos del rpc CrearBorradorDesdeDiagnostico de Trámites). Propuestos (5.3), a revisar en el PR.
 */
export const MOTIVOS_DERIVACION: Readonly<Record<string, string>> = {
  NO_TERMINADO: "Las consultas de la placa no han terminado",
  NO_VIABLE: "El dictamen no es viable",
  YA_TIENE_TRAMITE: "La placa ya tenía trámite",
  EN_CURSO: "Ya se está creando el trámite de esta placa",
  TIPO_NO_DISPONIBLE: "El tipo de trámite no está disponible para la compañía",
  FAMILIA_BLOQUEADA: "La compañía tiene bloqueado este tipo de trámite",
  USUARIO_SIN_PERMISO: "No tienes permiso para crear este trámite en Trámites",
  OT_NO_HABILITADO: "OT no habilitado para la compañía",
  REGLA_COMPANIA: "Una regla de la compañía impide crear el trámite",
  TRAMITE_ACTIVO_DUPLICADO: "La placa ya tiene otro trámite activo en Trámites",
  EMPRESA_INACTIVA: "La compañía no está activa en Trámites",
  REFERENCIA_DE_OTRO_ORIGEN: "La placa ya está asociada a un trámite creado por otra vía",
  DATOS_INVALIDOS: "Faltan datos de la placa o no son válidos para crear el trámite",
  TRAMITES_NO_DISPONIBLE: "Trámites no está disponible en este momento",
  RECHAZO_TRAMITES: "Trámites no aceptó crear el trámite",
};
