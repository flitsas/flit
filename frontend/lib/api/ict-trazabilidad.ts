// Cliente tipado de la Trazabilidad ICT (Feature #11814).
// Consume los endpoints de SOLO LECTURA de core-ict a través del proxy/Gateway:
//   GET /api/v1/ict/trazabilidad/tramites
//   GET /api/v1/ict/trazabilidad/tramites/{numero}/recorrido
//   GET /api/v1/ict/trazabilidad/tramites/{numero}/consultas-fuente
import { apiFetch } from "./client";
import type { EstadoIct } from "@/lib/ict/trazabilidad";

/** Una fila de la bandeja: un pre-trámite, nunca una petición HTTP. */
export interface TramiteIct {
  id: string;
  numero: number;
  referenciaCliente: string | null;
  placa: string;
  vin: string | null;
  tipoTramiteId: number;
  tipoTramite: string | null;
  operacionId: number;
  operacion: string | null;
  /**
   * Tenant de la FILA, no el de la sesión. Cualquier pantalla que liste trámites de varias empresas
   * lo necesita para abrir el detalle: sin él, la vista de trámite responde «Falta header
   * X-Tenant-Id». Es la misma lección del LOG QX (Feature #11784).
   */
  clientTenantId: string;
  compania: string | null;
  radicador: string;
  estado: EstadoIct;
  /** Minutos sin avanzar, calculados en servidor. Null en los estados terminales. */
  minutosEsperando: number | null;
  pausado: boolean;
  sinAdjuntos: boolean;
  tieneTramiteFlit: boolean;
  recibidoEn: string;
}

export interface PaginaTramitesIct {
  items: TramiteIct[];
  total: number;
  page: number;
  pageSize: number;
  /** Conteo de los siete estados. Ignora el filtro de estado y respeta los demás. */
  conteoPorEstado: Record<string, number>;
}

export interface FiltrosTramitesIct {
  numero?: number;
  /** Placas y/o VIN separados por coma. El backend los normaliza. */
  placas?: string;
  compania?: string;
  tipo?: number;
  operacion?: number;
  estado?: string;
  desde?: string;
  hasta?: string;
  page?: number;
  pageSize?: number;
}

export function fetchTramitesIct(
  filtros: FiltrosTramitesIct = {},
  signal?: AbortSignal,
): Promise<PaginaTramitesIct> {
  return apiFetch<PaginaTramitesIct>("/api/v1/ict/trazabilidad/tramites", {
    query: {
      numero: filtros.numero,
      placas: filtros.placas,
      compania: filtros.compania,
      tipo: filtros.tipo,
      operacion: filtros.operacion,
      estado: filtros.estado,
      desde: filtros.desde,
      hasta: filtros.hasta,
      page: filtros.page,
      pageSize: filtros.pageSize,
    },
    signal,
  });
}

/** Resultado de una etapa del recorrido. */
export type ResultadoHito = "ok" | "error" | "espera" | "anulado" | "pendiente";

export interface HitoTrazabilidad {
  etapa: string;
  titulo: string;
  /** Null cuando la etapa aún no se alcanzó, o cuando el hito es el cierre «sin avanzar». */
  ocurrido: string | null;
  segundosDesdeAnterior: number | null;
  resultado: ResultadoHito;
  /** Lo decide el servidor para que pantalla y exportación señalen el mismo tramo. */
  esTramoMasLento: boolean;
  mensaje: string | null;
}

export interface TiemposRecorrido {
  segundosTotal: number | null;
  segundosHastaActivar: number | null;
  segundosHastaCrearBorrador: number | null;
  segundosSinAvanzar: number | null;
}

export interface RecorridoTramiteIct {
  id: string;
  numero: number;
  referenciaCliente: string | null;
  placa: string;
  vin: string | null;
  tipoTramite: string | null;
  operacion: string | null;
  clientTenantId: string;
  compania: string | null;
  estado: EstadoIct;
  hitos: HitoTrazabilidad[];
  tiempos: TiemposRecorrido;
  mensajeNovedad: string | null;
  procedureInstanceId: string | null;
  codigoOrganismoTransito: string | null;
  organismoTransito: string | null;
}

export function fetchRecorridoIct(numero: number, signal?: AbortSignal): Promise<RecorridoTramiteIct> {
  return apiFetch<RecorridoTramiteIct>(
    `/api/v1/ict/trazabilidad/tramites/${numero}/recorrido`,
    { signal },
  );
}

export interface ConsultaFuenteIct {
  id: string;
  nivelActor: string;
  nivelActorEtiqueta: string;
  tipoConsulta: string;
  tipoConsultaEtiqueta: string;
  /** Documento (enmascarado), placa o VIN. */
  identificador: string | null;
  consultada: boolean;
  valida: boolean;
  intentos: number;
  /** La consulta que impide avanzar. Lo decide el servidor. */
  bloquea: boolean;
  creadaEn: string;
  /** JSON de la fuente, ya enmascarado. Null cuando la consulta nunca se resolvió. */
  respuesta: string | null;
}

export function fetchConsultasFuenteIct(
  numero: number,
  signal?: AbortSignal,
): Promise<ConsultaFuenteIct[]> {
  return apiFetch<ConsultaFuenteIct[]>(
    `/api/v1/ict/trazabilidad/tramites/${numero}/consultas-fuente`,
    { signal },
  );
}
