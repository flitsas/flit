// Mock del servidor de Consultas (contrato §4) mientras S1 entrega el backend. Responde por `fetch`, así que las pruebas
// pasan también por el cliente real (`fuenteComparendos` → `apiFetch`). Errores con la forma del servicio: `{ code, error }`.
import { vi } from "vitest";
import type {
  ComparendoQueryRow,
  ConsultaProgramada,
  GuardarProgramacion,
  QueryDefinition,
  QueryField,
  QueryResult,
  SavedQuery,
} from "@/lib/api/consultas-personalizadas";

export function fila(parcial: Partial<ComparendoQueryRow> & { id: string; numero: string }): ComparendoQueryRow {
  return {
    placa: null, nitMonitoreado: null, titular: null, empresaId: null, empresaNombre: null, codigoInfraccion: null, descripcionInfraccion: null, esFotomulta: null,
    tipoVehiculo: null, servicioVehiculo: null, organismoId: null, organismoNombre: null, departamento: null, municipio: null,
    fechaImposicion: null, fechaNotificacion: null, fechaIngreso: null, fechaLimiteGestion: null, fechaFinDescuento50: null,
    fechaFinDescuento25: null, fechaCaducidad: null, fechaPrescripcion: null, valorCapital: null, valorAdicional: null,
    valorTotal: null, activo: null, estadoLegal: null, ventanaDescuento: null, enCobroCoactivo: null, estadoGestion: null,
    gestionado: null, pagado: null, tieneDp: null, dpEstado: null, dpRadicado: null, dpTipologia: null, dpEnvioAutomatico: null,
    dpFechaRadicacion: null, dpSentido: null, dpFechaRespuesta: null, estadoNotificacionConductor: null,
    notificadoFisicamente: null, estadoCambioContraventor: null, estadoConsultaContraventor: null, pagoTipo: null,
    pagoValor: null, pagoFecha: null,
    ...parcial,
  };
}

export const CAMPOS: QueryField[] = [
  { id: "numero", label: "No. comparendo", kind: "texto", group: "Identificación", operators: ["es_alguno", "no_es_ninguno"], options: [], hint: null, admiteLista: true },
  { id: "placa", label: "Placa", kind: "texto", group: "Identificación", operators: ["es_alguno"], options: [], hint: null, admiteLista: true },
  {
    id: "estado_legal", label: "Estado legal", kind: "opcion", group: "Estado", operators: ["es_alguno"], hint: null, admiteLista: true,
    // Etiquetas distintas de las de respaldo de `lib/etiquetas`: así las pruebas demuestran que salen de `/fields`.
    options: [{ value: "EnTiempo", label: "Dentro del plazo" }, { value: "Vencido", label: "Plazo vencido" }],
  },
  {
    id: "estado_gestion", label: "Estado de gestión", kind: "opcion", group: "Estado", operators: ["es_alguno"], hint: null, admiteLista: true,
    options: [{ value: "SinGestion", label: "Todavía sin gestión" }, { value: "DpEnviado", label: "DP radicado" }],
  },
];

export const FILAS: ComparendoQueryRow[] = [
  fila({ id: "c1", numero: "11001000000041234567", placa: "ABC123", codigoInfraccion: "C02", organismoNombre: "Bogotá D.C.", fechaImposicion: "2026-10-09", valorTotal: 604100, estadoLegal: "EnTiempo", estadoGestion: "SinGestion", dpEstado: null }),
  fila({ id: "c2", numero: "05001000000012345678", placa: "XYZ987", codigoInfraccion: "D02", organismoNombre: "Medellín", fechaImposicion: "2026-10-20", valorTotal: 1208200, estadoLegal: "Vencido", estadoGestion: "DpEnviado", dpEstado: "Enviado" }),
];

const AHORA = "2026-11-01T15:00:00Z";
/** #1 de fábrica tras el cambio de S0: plazo de gestión que vence en los próximos 30 días. */
export const DE_FABRICA_PLAZO: SavedQuery = {
  id: "c0000000-0000-4000-8000-000000000001",
  nombre: "Vence el plazo de gestión en los próximos 30 días",
  descripcion: null,
  deFabrica: true,
  definition: { fechas: { campo: "limite_gestion", preset: "proximos_30" }, condiciones: [], columnas: [], sortBy: "fecha_limite_gestion", descending: false },
  createdAt: "2026-11-01T15:00:00Z",
  updatedAt: null,
};

/** Ids de las de fábrica según S1: `c0000000-0000-4000-8000-00000000000{1..8}`, en el orden de §5. */
export const ID_FABRICA_SIN_GESTION = "c0000000-0000-4000-8000-000000000003";
export const DE_FABRICA: SavedQuery = {
  id: ID_FABRICA_SIN_GESTION,
  nombre: "Sin gestión",
  descripcion: "Activos, sin pagar y sin gestión",
  deFabrica: true,
  definition: {
    fechas: { campo: "imposicion", preset: "anio_actual" },
    condiciones: [{ fieldId: "estado_gestion", operator: "es_alguno", values: ["SinGestion"] }],
    columnas: ["numero", "placa", "fechaLimiteGestion", "estadoGestion"],
  },
  createdAt: AHORA,
  updatedAt: null,
};
export const PROPIA: SavedQuery = {
  id: "mia-1",
  nombre: "Mis placas de flota",
  descripcion: null,
  deFabrica: false,
  definition: { fechas: { campo: "imposicion", preset: "ultimos_90" }, condiciones: [{ fieldId: "placa", operator: "es_alguno", values: ["ABC123"] }], columnas: [] },
  createdAt: AHORA,
  updatedAt: null,
};

export interface OpcionesMock {
  /** Responder 422 CONSULTA_DEMASIADO_AMPLIA a `/run`. */
  demasiadoAmplia?: string;
  cobertura?: QueryResult<ComparendoQueryRow>["cobertura"];
  totalPeriodoAnterior?: number | null;
  /** Envíos ya programados al arrancar. */
  programaciones?: ConsultaProgramada[];
  /** Responder este error a POST /programaciones. */
  errorProgramar?: { status: number; code: string; error: string };
}

export const PROGRAMADA: ConsultaProgramada = {
  id: "prog-1",
  consultaId: "mia-1",
  periodicidad: "semanal",
  diaSemana: 1,
  diaMes: null,
  hora: "07:00:00",
  destinatarios: ["flota@empresa.com", "gerencia@empresa.com", "jefe@empresa.com"],
  activa: true,
  ultimaEjecucion: "2026-11-17T12:00:00Z",
  proximaEjecucion: "2026-11-24T12:00:00Z",
  ultimoResultado: "Enviado a 3 destinatarios",
};

export interface Peticion {
  metodo: string;
  ruta: string;
  query: URLSearchParams;
  cuerpo: unknown;
}

const json = (status: number, cuerpo: unknown) =>
  new Response(cuerpo === undefined ? null : JSON.stringify(cuerpo), { status, headers: { "Content-Type": "application/json" } });

/** Instala el mock en `fetch` y devuelve el registro de peticiones (para afirmar lo que salió hacia el servidor). */
export function instalarServidor(opciones: OpcionesMock = {}) {
  const peticiones: Peticion[] = [];
  let guardadas: SavedQuery[] = [DE_FABRICA_PLAZO, DE_FABRICA, PROPIA];
  let programaciones: ConsultaProgramada[] = [...(opciones.programaciones ?? [])];

  const fetchMock = vi.fn(async (entrada: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(entrada), "http://127.0.0.1");
    const metodo = init?.method ?? "GET";
    const cuerpo = init?.body ? JSON.parse(String(init.body)) : undefined;
    const ruta = url.pathname.replace("/api/v1/comparendos/reportes/consultas", "");
    peticiones.push({ metodo, ruta, query: url.searchParams, cuerpo });

    if (ruta === "/fields") return json(200, CAMPOS);
    if (ruta === "/saved" && metodo === "GET") return json(200, guardadas);
    if (ruta === "/saved" && metodo === "POST") {
      const entradaGuardar = cuerpo as { id?: string; nombre: string; definition: QueryDefinition };
      if (guardadas.some((q) => !q.deFabrica && q.id !== entradaGuardar.id && q.nombre === entradaGuardar.nombre))
        return json(409, { code: "NOMBRE_REPETIDO", error: `Ya tienes una consulta llamada «${entradaGuardar.nombre}».` });
      const nueva: SavedQuery = { id: entradaGuardar.id ?? `nueva-${guardadas.length}`, nombre: entradaGuardar.nombre, descripcion: null, deFabrica: false, definition: entradaGuardar.definition, createdAt: AHORA, updatedAt: null };
      guardadas = [...guardadas.filter((q) => q.id !== nueva.id), nueva];
      return json(200, nueva);
    }
    if (ruta.startsWith("/saved/") && metodo === "DELETE") {
      const id = decodeURIComponent(ruta.slice("/saved/".length));
      if (guardadas.find((q) => q.id === id)?.deFabrica) return json(409, { code: "CONSULTA_DE_FABRICA", error: "Las consultas del sistema no se borran." });
      guardadas = guardadas.filter((q) => q.id !== id);
      return json(204, undefined);
    }
    if (ruta === "/programaciones" && metodo === "GET") return json(200, programaciones);
    if (ruta === "/programaciones" && metodo === "POST") {
      if (opciones.errorProgramar) return json(opciones.errorProgramar.status, { code: opciones.errorProgramar.code, error: opciones.errorProgramar.error });
      const e = cuerpo as GuardarProgramacion;
      const previa = programaciones.find((x) => x.id === e.id);
      const guardada: ConsultaProgramada = {
        id: e.id ?? `prog-${programaciones.length + 1}`,
        consultaId: e.consultaId, periodicidad: e.periodicidad, diaSemana: e.diaSemana ?? null, diaMes: e.diaMes ?? null,
        hora: `${e.hora}:00`, destinatarios: e.destinatarios, activa: e.activa,
        ultimaEjecucion: previa?.ultimaEjecucion ?? null, proximaEjecucion: e.activa ? "2026-12-01T12:00:00Z" : null, ultimoResultado: previa?.ultimoResultado ?? null,
      };
      programaciones = [...programaciones.filter((x) => x.id !== guardada.id), guardada];
      return json(200, guardada);
    }
    if (ruta.startsWith("/programaciones/") && metodo === "DELETE") {
      programaciones = programaciones.filter((x) => x.id !== decodeURIComponent(ruta.slice("/programaciones/".length)));
      return json(204, undefined);
    }
    if (ruta === "/run") {
      if (opciones.demasiadoAmplia) return json(422, { code: "CONSULTA_DEMASIADO_AMPLIA", error: opciones.demasiadoAmplia });
      const page = Number(url.searchParams.get("page") ?? 1);
      const pageSize = Number(url.searchParams.get("pageSize") ?? 50);
      const resultado: QueryResult<ComparendoQueryRow> = {
        total: FILAS.length,
        page,
        pageSize,
        desde: "2026-10-02",
        hasta: "2026-11-01",
        // S1: null en page > 1 (y cuando el periodo anterior no cabe en el tope).
        totalPeriodoAnterior: page > 1 ? null : (opciones.totalPeriodoAnterior === undefined ? 1 : opciones.totalPeriodoAnterior),
        filas: page === 1 ? FILAS : [],
        cobertura: opciones.cobertura ?? [],
      };
      return json(200, resultado);
    }
    return json(404, { code: "NO_ENCONTRADO", error: "No existe." });
  });

  vi.stubGlobal("fetch", fetchMock);
  return { peticiones, fetchMock };
}
