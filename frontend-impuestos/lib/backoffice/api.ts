// Backoffice del Super Admin (contracts/openapi/impuestos.v1.yaml 1.0.0, tag Backoffice; plan §5.1): compañías con su
// aprovisionamiento, carga de credenciales del proveedor (nunca se devuelven) y reintento. Las rutas van por tenantId en
// la ruta, no por ?empresa=. Etiquetas y literales: x-flit-etiquetas / x-flit-literales del contrato.
import { useEffect, useState } from "react";
import type { StatusBadgeTone } from "@flit/ui/StatusBadge";
import { RUTAS, apiFetch } from "@/lib/api";
import { rotular } from "@/lib/rotulos";

export type EstadoAprovisionamiento =
  | "PENDIENTE"
  | "LISTO"
  | "LISTO_SIN_WEBHOOK"
  | "ERROR"
  | "INCIERTO"
  | "REQUIERE_CREDENCIALES"
  | "REQUIERE_REVISION_NIT"
  | "SUSPENDIDO";

export interface AprovisionamientoResumen {
  estado: EstadoAprovisionamiento | string;
  motivo: string | null;
  accion: string | null;
  firmaConfirmada: boolean;
}

export interface EmpresaBackoffice {
  id: string;
  nombre: string;
  nit: string | null;
  aprovisionamiento: AprovisionamientoResumen;
  suspendida: boolean;
}

export interface PaginaEmpresasBackoffice {
  items: EmpresaBackoffice[];
  total: number;
  page: number;
  pageSize: number;
}

export interface TransicionAprovisionamiento {
  desde: string | null;
  hacia: string;
  motivo: string | null;
  actorTipo: string;
  en: string;
}

export interface AprovisionamientoDetalle extends AprovisionamientoResumen {
  modoRespaldoFirma: boolean;
  actualizadoEn: string;
  historial: TransicionAprovisionamiento[];
}

export interface EstadoCredenciales {
  tieneApiKey: boolean;
  tieneWebhookSecret: boolean;
  origen: string | null;
  apiKeyPrefijo: string | null;
  actualizadoEn: string | null;
}

/** Cuerpo de PUT …/credenciales: un valor vacío viaja como null. Nunca se registra ni se guarda en el navegador. */
export interface CargaCredenciales {
  apiKey: string | null;
  webhookSecret: string | null;
}

export const RUTAS_BACKOFFICE = {
  empresas: RUTAS.empresas,
  aprovisionamiento: (tenantId: string) => `${RUTAS.empresas}/${encodeURIComponent(tenantId)}/aprovisionamiento`,
  reintentar: (tenantId: string) => `${RUTAS.empresas}/${encodeURIComponent(tenantId)}/aprovisionamiento/reintentar`,
  credenciales: (tenantId: string) => `${RUTAS.empresas}/${encodeURIComponent(tenantId)}/credenciales`,
  configuracionContractual: (tenantId: string) => `${RUTAS.empresas}/${encodeURIComponent(tenantId)}/configuracion-contractual`,
} as const;

/** x-flit-etiquetas de EstadoAprovisionamiento. */
export const ETIQUETA_ESTADO_APROVISIONAMIENTO: Readonly<Record<string, string>> = {
  PENDIENTE: "Pendiente",
  LISTO: "Listo",
  LISTO_SIN_WEBHOOK: "Listo sin webhook",
  ERROR: "Error",
  INCIERTO: "Incierto",
  REQUIERE_CREDENCIALES: "Requiere credenciales",
  REQUIERE_REVISION_NIT: "Requiere revisión del NIT",
  SUSPENDIDO: "Suspendido",
};

/** Tono del chip por estado (spec-visual §1.5). PENDIENTE va con EtiquetaEnProceso, no con un tono. */
export const TONO_ESTADO_APROVISIONAMIENTO: Readonly<Record<string, StatusBadgeTone>> = {
  LISTO: "success",
  LISTO_SIN_WEBHOOK: "success",
  ERROR: "danger",
  INCIERTO: "purple",
  REQUIERE_CREDENCIALES: "warning",
  REQUIERE_REVISION_NIT: "warning",
  SUSPENDIDO: "neutral",
};

/** x-flit-etiquetas de MotivoAprovisionamiento (el mismo texto que muestra el admin de compañías). */
export const ETIQUETA_MOTIVO_APROVISIONAMIENTO: Readonly<Record<string, string>> = {
  REINTENTO_PROGRAMADO: "No se pudo contactar al proveedor; se reintentará automáticamente.",
  SIN_WEBHOOK_SECRET: "Falta el secreto del webhook: los resultados se consultan al proveedor.",
  ALTA_DATOS_RECHAZADOS: "El proveedor rechazó los datos del alta de la compañía.",
  RAZON_SOCIAL_LARGA: "La razón social supera los 200 caracteres que admite el proveedor.",
  AMBIENTE_NO_CONFIGURADO: "La integración con el proveedor no está configurada en este ambiente.",
  PROVEEDOR_INALCANZABLE: "No se pudo contactar al proveedor después de varios intentos.",
  ALTA_SIN_CONFIRMAR: "El proveedor no confirmó el alta: puede haberla recibido. Reintenta para verificar.",
  EMPRESA_YA_REGISTRADA: "La empresa ya existe en el proveedor: rota las credenciales en la consola del API y cárgalas en Impuestos.",
  CREDENCIAL_RECHAZADA: "El proveedor rechazó la API key de la compañía: carga credenciales nuevas en Impuestos.",
  NIT_AUSENTE: "La compañía no tiene NIT.",
  NIT_AMBIGUO: "No se puede separar el dígito de verificación del NIT.",
  NIT_IGUAL_A_CABEZA: "El NIT es el mismo de su compañía cabeza.",
  PRODUCTO_DESACTIVADO: "Impuestos está desactivado para la compañía.",
};

/** x-flit-etiquetas del actor del historial. */
export const ETIQUETA_ACTOR: Readonly<Record<string, string>> = { SISTEMA: "Sistema", SUPER_ADMIN: "Super Admin", PLATAFORMA: "Plataforma" };

/** x-flit-etiquetas de OrigenCredencial. */
export const ETIQUETA_ORIGEN_CREDENCIAL: Readonly<Record<string, string>> = {
  ALTA: "Alta automática",
  COPIA_DIAGNOSTICO: "Copiada de Diagnóstico",
  CARGA_MANUAL: "Cargada por el Super Admin",
};

/** x-flit-literales.pantalla usados por el backoffice. */
export const LITERALES_BACKOFFICE = {
  credencialConfigurada: "Configurada",
  credencialNoConfigurada: "Sin configurar",
  avisoRotacionCredenciales:
    "La empresa ya existe en el proveedor: rota las credenciales en la consola del API y cárgalas aquí. Rotarlas puede afectar a Diagnóstico si comparte la empresa.",
} as const;

/** x-flit-literales.errores de las rutas del backoffice. */
export const ERRORES_BACKOFFICE: Readonly<Record<string, string>> = {
  CREDENCIAL_INVALIDA: "El proveedor rechazó la API key: revisa el valor e intenta de nuevo.",
  CREDENCIAL_REQUERIDA: "Escribe la API key o el secreto del webhook.",
  ESTADO_NO_ADMITE_CARGA: "En el estado actual de la compañía no se pueden cargar credenciales.",
  ESTADO_NO_REINTENTABLE: "En el estado actual de la compañía no se puede reintentar.",
  PROVEEDOR_NO_DISPONIBLE: "El proveedor no responde. Intenta más tarde.",
  EMPRESA_NO_ENCONTRADA: "La compañía no existe o no tiene Impuestos.",
  PAGINACION_INVALIDA: "La paginación no es válida.",
  FILTRO_INVALIDO: "Un filtro no es válido.",
};

/** Estados desde los que se puede reintentar (contrato: ERROR, INCIERTO o REQUIERE_REVISION_NIT). */
export const ESTADOS_REINTENTABLES: ReadonlySet<string> = new Set(["ERROR", "INCIERTO", "REQUIERE_REVISION_NIT"]);

export const etiquetaEstadoAprovisionamiento = (estado: string | null | undefined) =>
  rotular({ contexto: "estado de aprovisionamiento", codigo: estado, respaldo: ETIQUETA_ESTADO_APROVISIONAMIENTO });

export const etiquetaMotivoAprovisionamiento = (motivo: string | null | undefined) =>
  motivo ? rotular({ contexto: "motivo de aprovisionamiento", codigo: motivo, respaldo: ETIQUETA_MOTIVO_APROVISIONAMIENTO }) : null;

export const TAMANO_PAGINA_EMPRESAS = 50;

export function listarEmpresasBackoffice(filtros: { q: string; page: number; pageSize?: number }, signal?: AbortSignal): Promise<PaginaEmpresasBackoffice> {
  const params = new URLSearchParams();
  if (filtros.q.trim()) params.set("q", filtros.q.trim());
  params.set("page", String(filtros.page));
  params.set("pageSize", String(filtros.pageSize ?? TAMANO_PAGINA_EMPRESAS));
  return apiFetch<PaginaEmpresasBackoffice>(`${RUTAS_BACKOFFICE.empresas}?${params.toString()}`, { signal });
}

/** Tope de páginas que se recorren al buscar una compañía por id (defensa ante un `total` inconsistente). */
const MAX_PAGINAS = 100;

/**
 * Nombre y NIT de una compañía del backoffice. El contrato no los trae en el detalle del aprovisionamiento ni en
 * /usuarios, así que se buscan en la lista paginada (la misma que lee el selector). Null si no aparece.
 */
export async function buscarEmpresaBackoffice(tenantId: string, signal?: AbortSignal): Promise<EmpresaBackoffice | null> {
  let vistas = 0;
  for (let page = 1; page <= MAX_PAGINAS; page++) {
    const pagina = await listarEmpresasBackoffice({ q: "", page, pageSize: 200 }, signal);
    const items = pagina?.items ?? [];
    const hallada = items.find((e) => e.id === tenantId);
    if (hallada) return hallada;
    vistas += items.length;
    if (items.length === 0 || typeof pagina.total !== "number" || vistas >= pagina.total) break;
  }
  return null;
}

export const obtenerAprovisionamiento = (tenantId: string, signal?: AbortSignal) =>
  apiFetch<AprovisionamientoDetalle>(RUTAS_BACKOFFICE.aprovisionamiento(tenantId), { signal });

export const reintentarAprovisionamiento = (tenantId: string) => apiFetch<void>(RUTAS_BACKOFFICE.reintentar(tenantId), { method: "POST" });

export const obtenerEstadoCredenciales = (tenantId: string, signal?: AbortSignal) =>
  apiFetch<EstadoCredenciales>(RUTAS_BACKOFFICE.credenciales(tenantId), { signal });

export const cargarCredenciales = (tenantId: string, carga: CargaCredenciales) =>
  apiFetch<void>(RUTAS_BACKOFFICE.credenciales(tenantId), { method: "PUT", body: carga });

/**
 * Compañía elegida por el Super Admin (nombre y NIT) para cabeceras y migas. `null` mientras llega, si no hay id o si
 * no aparece; nunca un nombre genérico. Ver `buscarEmpresaBackoffice`.
 */
export function useEmpresaBackoffice(tenantId: string | null): EmpresaBackoffice | null {
  const [hallada, setHallada] = useState<{ id: string; empresa: EmpresaBackoffice | null } | null>(null);
  useEffect(() => {
    if (!tenantId) return;
    const controller = new AbortController();
    buscarEmpresaBackoffice(tenantId, controller.signal).then(
      (empresa) => {
        if (!controller.signal.aborted) setHallada({ id: tenantId, empresa });
      },
      () => {
        if (!controller.signal.aborted) setHallada({ id: tenantId, empresa: null });
      },
    );
    return () => controller.abort();
  }, [tenantId]);
  return tenantId && hallada?.id === tenantId ? hallada.empresa : null;
}
