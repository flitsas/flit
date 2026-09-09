/**
 * Catálogo de las **once** condiciones especiales de traspaso de los arts. 5.3.2.3 a 5.3.2.13
 * (anexo normativo `docs/plantilla-transferencia-dominio.md` §4.0), que alimenta el control
 * «Régimen aplicable a la operación» (CF-24) y cuya declaración bloquea con `VB-07`.
 *
 * Los códigos son los mismos del backend (`TransferSpecialRegime`): el frontend no inventa
 * identificadores y el servidor rechaza el payload aunque este control se omita.
 *
 * > **El art. 5.3.2.14 no está aquí y no puede estarlo.** La expedición de la nueva licencia de
 * > tránsito es el paso final común a *todo* traspaso, no una condición especial —lo dice la nota
 * > de alcance del anexo—. Si se ofreciera como opción, el usuario podría declararla y bloquearía
 * > un trámite perfectamente ordinario.
 */
export interface RegimenEspecialCondicion {
  /** Código estable que viaja en `condicionesDeclaradas`. */
  codigo: string;
  /** Artículo de la Resolución 20233040017145 de 2023 que la regula. */
  articulo: string;
  /** Enunciado, tal como lo nombra el anexo §4.0. */
  titulo: string;
  /** Soporte que exige la norma y que este documento **no** acredita. */
  soporte: string;
}

export const REGIMEN_ESPECIAL_CONDICIONES: readonly RegimenEspecialCondicion[] = [
  {
    codigo: "SERVICIO_PUBLICO_PASAJEROS_O_MIXTO",
    articulo: "art. 5.3.2.3",
    titulo: "Vehículo de servicio público de pasajeros o mixto",
    soporte:
      "Contrato de cesión del derecho de vinculación suscrito por cedente y cesionario, con la aceptación de la empresa.",
  },
  {
    codigo: "ASEGURADORA_POR_HURTO",
    articulo: "art. 5.3.2.4",
    titulo: "Traspaso a compañía de seguros por hurto del vehículo",
    soporte: "Régimen de exenciones propio: el organismo de tránsito exceptúa SOAT, RTM e improntas.",
  },
  {
    codigo: "ASEGURADORA_POR_PERDIDA_PARCIAL",
    articulo: "art. 5.3.2.5",
    titulo: "Traspaso a compañía de seguros por pérdida o destrucción parcial",
    soporte: "Peritaje de la aseguradora que determina la pérdida o destrucción parcial.",
  },
  {
    codigo: "VEHICULO_BLINDADO",
    articulo: "art. 5.3.2.6",
    titulo: "Vehículo blindado",
    soporte:
      "Resolución de la Superintendencia de Vigilancia y Seguridad Privada y certificación de la empresa blindadora registrada.",
  },
  {
    codigo: "DECISION_JUDICIAL_O_ADMINISTRATIVA",
    articulo: "art. 5.3.2.7",
    titulo: "Traspaso producto de decisión judicial o administrativa",
    soporte: "Sentencia judicial o acto administrativo de adjudicación, con la autoridad que lo profirió.",
  },
  {
    codigo: "SUCESION",
    articulo: "art. 5.3.2.8",
    titulo: "Traspaso por sucesión",
    soporte: "Sentencia o escritura pública que acredita el derecho.",
  },
  {
    codigo: "IMPORTACION_TEMPORAL_SUSTITUCION_IMPORTADOR",
    articulo: "art. 5.3.2.9",
    titulo: "Importación temporal por sustitución del importador",
    soporte:
      "Declaración de importación modificatoria con el nuevo importador autorizado por la DIAN; la licencia se expide provisional.",
  },
  {
    codigo: "DECOMISO_DIAN_O_ADJUDICACION_NACION",
    articulo: "art. 5.3.2.10",
    titulo: "Decomiso por la DIAN o adjudicación a favor de la Nación",
    soporte: "Acto administrativo o providencia de adjudicación de la entidad.",
  },
  {
    codigo: "COMISO_FISCALIA",
    articulo: "art. 5.3.2.11",
    titulo: "Comiso por la Fiscalía General de la Nación",
    soporte: "Acto administrativo con la orden de comiso.",
  },
  {
    codigo: "DECLARATORIA_DE_ABANDONO",
    articulo: "art. 5.3.2.12",
    titulo: "Vehículo enajenado por declaratoria de abandono",
    soporte:
      "Acto administrativo de adjudicación, certificación del fabricante o improntas, y registro previo de la declaratoria en el RUNT.",
  },
  {
    codigo: "CARGA_PBV_SUPERIOR_10500_KG",
    articulo: "art. 5.3.2.13",
    titulo: "Vehículo de carga con peso bruto vehicular superior a 10.500 kg",
    soporte:
      "Validación en el RUNT de la autorización de registro inicial del Ministerio de Transporte.",
  },
] as const;

/** Valor de la opción «ninguna de las anteriores aplica»: la única que permite generar. */
export const REGIMEN_NINGUNA_APLICA = "NINGUNA";

/** Selección del control: sin responder (`""`), «ninguna aplica» o un código del catálogo. */
export type RegimenSeleccion = "" | typeof REGIMEN_NINGUNA_APLICA | string;

export function condicionPorCodigo(codigo: string): RegimenEspecialCondicion | undefined {
  return REGIMEN_ESPECIAL_CONDICIONES.find((c) => c.codigo === codigo);
}

/**
 * ¿La selección permite generar? Solo «ninguna aplica». El silencio **no** es un sí: el gate del
 * anexo §4.0 es previo a elegir escenario y el backend lo rechaza igual con `VB-07`.
 */
export function permiteGenerar(seleccion: RegimenSeleccion): boolean {
  return seleccion === REGIMEN_NINGUNA_APLICA;
}
