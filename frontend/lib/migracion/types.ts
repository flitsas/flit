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
 * Los estados que devuelve el motor, en castellano.
 *
 * Vienen de tres enums de C# (`LoadStatus`, `AttachmentLoadStatus`, `SnapshotLoadStatus`) que se
 * serializan con `ToString()`, así que llegan en inglés y en PascalCase. En el reporte de consola
 * eso pasa desapercibido; en una pantalla se lee como algo a medio traducir.
 *
 * Lo que no esté en el mapa se muestra tal cual: añadir un estado al motor no debe dejar la
 * pantalla en blanco.
 */
export const ETIQUETA_ESTADO_INSTANCIA: Record<string, string> = {
  Migrated: "Migrado",
  Materialized: "Generado",
  Simulated: "Simulado",
  Skipped: "Ya estaba",
  Quarantined: "En cuarentena",
  NotMigrated: "Sin migrar",
  NoAttachments: "Sin adjuntos",
  NotFoundInV1: "No está en V1",
  Failed: "Falló",
};

/**
 * Las claves de los conteos, en castellano.
 *
 * Se traducen y no se muestran crudas: son claves de un JSON (`eventosHistorial`,
 * `imagenesEnLaCarta`) y en pantalla delatan la tubería por la que llegaron. Que coincidan con el
 * reporte de consola no compensa: quien mira esto está mirando una interfaz, no una terminal.
 */
export const ETIQUETA_CONTEO: Record<string, string> = {
  campos: "Campos",
  actores: "Actores",
  eventosHistorial: "Eventos de historial",
  copiados: "Copiados",
  yaMigrados: "Ya migrados",
  fallidos: "Fallidos",
  excluidos: "Excluidos",
  imagenesEnLaCarta: "Imágenes en la carta",
  materializados: "Generados",
  yaMaterializados: "Ya generados",
  yaVenianComoAdjunto: "Ya venían como adjunto",
  identidadesMarcadas: "Identidades acreditadas",
  identidadesYaMarcadas: "Identidades ya acreditadas",
};

/**
 * Etiqueta legible de una clave de conteo. Si no está en el mapa, se parte el camelCase en
 * palabras — feo, pero legible, y mejor que un hueco cuando el motor añada un contador nuevo.
 */
export function etiquetaConteo(clave: string): string {
  return (
    ETIQUETA_CONTEO[clave] ??
    clave.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, (c) => c.toUpperCase())
  );
}

export function etiquetaEstadoInstancia(estado: string): string {
  return ETIQUETA_ESTADO_INSTANCIA[estado] ?? estado;
}

/**
 * Nombre de negocio del tipo de trámite tal como viene en la respuesta.
 *
 * Comprobado contra el host real: `origen.tramite` NO es el `CliName` que viaja en la URL
 * (`transfer`), sino `V1ProcedureKind.Nombre` —«traspaso», «matrícula inicial»—, que ya está en
 * castellano y escrito para leerse. Lo único que le falta es la mayúscula inicial, porque en el
 * reporte de consola va dentro de una frase y aquí encabeza un dato.
 *
 * Se sigue aceptando el slug por si alguna respuesta lo trae: es el mismo dato con dos nombres
 * según por dónde entre, y equivocarse aquí solo cuesta una etiqueta fea.
 */
export function etiquetaTramite(valor: string): string {
  const porSlug = ETIQUETA_TRAMITE[valor as TipoTramite];
  if (porSlug) {
    return porSlug;
  }
  return valor.charAt(0).toUpperCase() + valor.slice(1);
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
