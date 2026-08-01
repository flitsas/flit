// Contrato de la consola de migración V1→V2. Espeja las proyecciones explícitas de
// `tools/Flit.DataMigration.Api/Contracts/` — si cambian allí, cambian aquí.
//
// Los nombres van en español porque el JSON del host de migración va en español: renombrarlos al
// vuelo solo añadiría una tabla de traducción que se desincroniza.

/** Los dos tipos que el migrador sabe mover. Son los `CliName` de `V1ProcedureKind.All`. */
export const TIPOS_TRAMITE = ["transfer", "registration"] as const;
export type TipoTramite = (typeof TIPOS_TRAMITE)[number];

/** Etiquetas en el idioma del negocio; el valor que viaja es el `CliName`. */
export const ETIQUETA_TRAMITE: Record<TipoTramite, string> = {
  transfer: "Traspaso",
  registration: "Matrícula",
};

/**
 * Las tres instancias del migrador. El orden importa y no es negociable —los adjuntos exigen que
 * la data plana exista—, pero el servidor lo reordena solo: aquí es solo el orden de presentación.
 */
export const INSTANCIAS = ["datos", "adjuntos", "documentos"] as const;
export type Instancia = (typeof INSTANCIAS)[number];

export const DESCRIPCION_INSTANCIA: Record<Instancia, string> = {
  datos: "Campos, actores e historial del trámite.",
  adjuntos: "Los archivos que el ciudadano subió en V1.",
  documentos: "Los documentos que V1 generaba al vuelo, más las validaciones de identidad.",
};

export interface DestinoDto {
  v2Id: string;
  tenantId: string;
}

export interface OrigenDto {
  tramite: string;
  tablaV1: string;
  tipoV2: string;
  lote: string;
  baseV1: string;
  baseV2: string;
  v1Id: number;
  dryRun: boolean;
}

export interface YaMigradoDto {
  v2Id: string;
  tenantId: string;
  lote: string;
  estadoFinal: string;
  migradoEl: string;
  avisos: string[];
}

export interface InstanciaDto {
  instancia: string;
  estado: string;
  v2Id: string | null;
  motivo: string | null;
  conProblemas: boolean;
  conteos: Record<string, number>;
  avisos: string[];
}

export interface MigracionRespuesta {
  origen: OrigenDto;
  yaMigrado: YaMigradoDto | null;
  instancias: InstanciaDto[];
  destino: DestinoDto | null;
  conProblemas: boolean;
}

export interface EstadoItemDto {
  v1Id: number;
  migrado: boolean;
  destino: DestinoDto | null;
  lote: string | null;
  estadoFinal: string | null;
  migradoEl: string | null;
  avisos: string[];
}

export interface EstadoRespuesta {
  tramite: string;
  tablaV1: string;
  items: EstadoItemDto[];
}

/**
 * Error normalizado del BFF. `titulo` es el código estable del `ProblemDetails` del host
 * (`migracion.tramite_en_curso`…), que es contra lo que conviene ramificar; `detalle` es la frase
 * para leer.
 */
export interface MigracionError {
  titulo: string;
  detalle: string;
  estado: number;
}

/**
 * Identidad de un trámite dentro de un lote: tipo + id, NUNCA el id solo.
 *
 * Los ids de V1 se repiten entre tipos porque viven en tablas distintas (hay 12.807 ids que
 * existen en las dos), así que una clave con solo el id haría que migrar el traspaso 26350 marcara
 * también la matrícula 26350 como hecha sin haberla tocado.
 */
export function claveFila(fila: { tramite: TipoTramite; v1Id: number }): string {
  return `${fila.tramite}:${fila.v1Id}`;
}

/** Construye el enlace al trámite en V2. El tenant es obligatorio: ver `DestinoDto` en el host. */
export function enlaceTramite(destino: DestinoDto): string {
  return `/tramites/${destino.v2Id}?t=${destino.tenantId}`;
}
