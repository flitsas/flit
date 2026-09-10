// Cliente de la consola de migración. Habla con el BFF (`/api/migracion/*`), NUNCA con el host de
// migración directamente: la llave vive en el servidor y no debe bajar al navegador.
//
// No reutiliza `apiFetch` de `lib/api/client` a propósito: aquel adjunta el JWT como `Authorization`
// y apunta a `NEXT_PUBLIC_API_BASE_URL`, que es el gateway. Aquí el destino es el propio origen y la
// credencial es la cookie de sesión, que el navegador manda sola.
import type {
  EstadoRespuesta,
  Instancia,
  MigracionError,
  MigracionRespuesta,
  TipoTramite,
} from "./types";

/** Lanzado por todo lo que devuelve este módulo. Conserva el código estable del host. */
export class ErrorMigracion extends Error implements MigracionError {
  readonly titulo: string;
  readonly detalle: string;
  readonly estado: number;

  constructor({ titulo, detalle, estado }: MigracionError) {
    super(detalle);
    this.name = "ErrorMigracion";
    this.titulo = titulo;
    this.detalle = detalle;
    this.estado = estado;
  }
}

async function leerRespuesta<T>(response: Response): Promise<T> {
  const texto = await response.text();
  let cuerpo: unknown = null;
  try {
    cuerpo = texto ? JSON.parse(texto) : null;
  } catch {
    cuerpo = null;
  }

  if (response.ok) {
    return cuerpo as T;
  }

  const problema = (cuerpo ?? {}) as { title?: string; detail?: string };
  throw new ErrorMigracion({
    titulo: problema.title ?? "migracion.error",
    detalle: problema.detail ?? `El servidor respondió ${response.status}.`,
    estado: response.status,
  });
}

export interface PeticionMigrar {
  tramite: TipoTramite;
  v1Id: number;
  /** Vacío o ausente ⇒ las tres, en el orden canónico que impone el host. */
  instancias?: Instancia[];
  dryRun: boolean;
  batchId?: string;
}

/**
 * Migra un trámite. **Sin `AbortSignal`, deliberadamente.**
 *
 * Cancelar desde el navegador no cancelaría nada útil: el BFF tampoco propaga la cancelación al
 * host, así que la migración seguiría escribiendo mientras la consola cree que la detuvo. Dar un
 * botón de cancelar que miente es peor que no darlo. Lo que sí se puede hacer —y hace la consola—
 * es dejar de encolar los siguientes.
 */
export async function migrarTramite(peticion: PeticionMigrar): Promise<MigracionRespuesta> {
  const response = await fetch("/api/migracion/ejecutar", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(peticion),
  });

  return leerRespuesta<MigracionRespuesta>(response);
}

/** Tope de ids por consulta que impone el host; se respeta aquí para trocear sin provocar un 400. */
const MAX_IDS_POR_CONSULTA = 200;

/**
 * Pregunta a la libreta por una lista de ids. Trocea sola: la consola reconcilia CSV completos al
 * cargar, y un archivo de trescientas filas no debería obligar a quien opera a partirlo a mano.
 */
export async function consultarEstado(
  tramite: TipoTramite,
  ids: number[],
): Promise<EstadoRespuesta> {
  const unicos = [...new Set(ids)];
  const items: EstadoRespuesta["items"] = [];
  let tablaV1 = "";

  for (let i = 0; i < unicos.length; i += MAX_IDS_POR_CONSULTA) {
    const lote = unicos.slice(i, i + MAX_IDS_POR_CONSULTA);
    const query = new URLSearchParams({ tramite, ids: lote.join(",") });
    const response = await fetch(`/api/migracion/estado?${query.toString()}`);
    const parcial = await leerRespuesta<EstadoRespuesta>(response);
    items.push(...parcial.items);
    tablaV1 = parcial.tablaV1;
  }

  return { tramite, tablaV1, items };
}
