'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  ConsolidadoLotesApiError,
  MENSAJE_NO_SE_PUDO_CANCELAR,
  MENSAJE_REINTENTAR_DESCARGA,
  consolidadoLotesClient,
} from '@/lib/api/consolidado-lotes-client';
import type {
  EstadoLoteConsolidados,
  LoteConsolidados,
} from '@/lib/api/types-consolidado-lotes';

/**
 * HU #13382 (épica #13216, Feature #13306) — seguimiento global del lote de descarga masiva de
 * consolidados (diseño D7: polling al lote «actual» desde el Shell).
 *
 * <p>Consulta `GET /api/v1/consolidados/lotes/actual` al montar y cada 4 s mientras el lote no es
 * terminal (AC1). Un 204 no pinta nada y no repite (AC5). Un fallo de red o un 5xx conserva el último
 * estado y reintenta a los 4 s sin bloquear (AC5). Al ver un lote pasar de activo a terminal con
 * partes, descarga sola la parte 1 una única vez entre pestañas: candado en `localStorage` + aviso
 * por `BroadcastChannel` (AC3). Un lote que ya llega terminal (recarga) no se autodescarga (AC4), y
 * pasadas 24 h desde el fin cuenta como expirado aunque la purga aún no haya corrido (AC4).</p>
 *
 * <p>`mostrarLote` fija un lote concreto (el recién creado, el del 409 `lote_activo` o el que elija
 * la bandeja del OT): a partir de ahí se consulta por id.</p>
 *
 * <p>HU #13388 — `cancelar()` manda `POST …/{id}/cancelacion` con un clic. `cancelado` es terminal
 * (detiene el polling, nunca autodescarga) y no cuenta como expirado aunque el servidor fije
 * `expiraEn` = instante de cancelación. El aviso del lote cancelado se oculta solo a los
 * {@link OCULTAR_CANCELADO_MS} contados desde `terminadoEn` del servidor: una recarga o una segunda
 * pestaña no lo vuelven a mostrar, sin estado nuevo en el backend (AC3/AC4).</p>
 */

export const INTERVALO_POLLING_LOTE_MS = 4_000;
/** Retención de las partes desde el fin del lote (`expiraEn` = `terminadoEn` + 24 h). */
export const RETENCION_LOTE_MS = 24 * 60 * 60 * 1000;
/** Espera entre escribir el candado y releerlo: decide quién gana si dos pestañas lo piden a la vez. */
export const VENTANA_CANDADO_MS = 150;
export const CANAL_LOTES_CONSOLIDADOS = 'flit:consolidado-lotes';
/** HU #13388 AC3/AC4 — el aviso «Descarga cancelada» se ve este tiempo desde `terminadoEn`. */
export const OCULTAR_CANCELADO_MS = 8_000;

const PREFIJO_CANDADO = 'flit:consolidado-lotes:autodescarga:';
/** Candados más viejos que esto se limpian (el lote ya expiró de todas formas). */
const VIDA_CANDADO_MS = 2 * RETENCION_LOTE_MS;
/** Tope de `setTimeout` (2^31 - 1 ms). */
const MAX_TIMEOUT_MS = 2_147_483_647;

export const claveCandadoAutodescarga = (loteId: string) => `${PREFIJO_CANDADO}${loteId}`;

const TERMINALES: ReadonlySet<EstadoLoteConsolidados> = new Set([
  'completado',
  'completado_con_omitidos',
  'fallido',
  'cancelado',
  'expirado',
]);

export function esEstadoTerminal(estado: EstadoLoteConsolidados): boolean {
  return TERMINALES.has(estado);
}

/**
 * Instante (ms) en que expiran las partes, o `null` si el lote no ha terminado. Un lote cancelado no
 * tiene partes que expiren (HU #13388): su aviso se rige por {@link OCULTAR_CANCELADO_MS}.
 */
function instanteExpiracion(lote: LoteConsolidados): number | null {
  if (!esEstadoTerminal(lote.estado) || lote.estado === 'cancelado') return null;
  const expira = lote.expiraEn ? Date.parse(lote.expiraEn) : NaN;
  if (!Number.isNaN(expira)) return expira;
  const fin = lote.terminadoEn ? Date.parse(lote.terminadoEn) : NaN;
  return Number.isNaN(fin) ? null : fin + RETENCION_LOTE_MS;
}

/** HU #13388 — instante (ms) en que se oculta el aviso del lote cancelado; `null` si no aplica. */
function instanteOcultarCancelado(lote: LoteConsolidados, vistoEnMs: number | null): number | null {
  if (lote.estado !== 'cancelado') return null;
  const fin = lote.terminadoEn ? Date.parse(lote.terminadoEn) : NaN;
  // Sin `terminadoEn` (no debería: el contrato lo fija al cancelar) se cuenta desde que se vio aquí.
  const desde = Number.isNaN(fin) ? vistoEnMs : fin;
  return desde === null ? null : desde + OCULTAR_CANCELADO_MS;
}

/** AC4 — expirado por estado o porque ya pasaron 24 h desde el fin. */
export function loteExpirado(lote: LoteConsolidados, ahoraMs: number): boolean {
  if (lote.estado === 'expirado') return true;
  const expira = instanteExpiracion(lote);
  return expira !== null && ahoraMs >= expira;
}

const descargable = (lote: LoteConsolidados) =>
  (lote.estado === 'completado' || lote.estado === 'completado_con_omitidos') && lote.partes.length > 0;

// ── Almacenamiento: todo acceso va en try/catch (modo privado, cuota, política del navegador) ──────

function purgarCandadosViejos(ahora: number): void {
  try {
    const ls = window.localStorage;
    for (let i = ls.length - 1; i >= 0; i--) {
      const clave = ls.key(i);
      if (!clave?.startsWith(PREFIJO_CANDADO)) continue;
      const marca = Number.parseInt((ls.getItem(clave) ?? '').split('-')[0] ?? '', 10);
      if (!Number.isFinite(marca) || ahora - marca > VIDA_CANDADO_MS) ls.removeItem(clave);
    }
  } catch {
    /* sin storage: nada que purgar */
  }
}

/**
 * Lotes vistos activos en esta pestaña (sobrevive a que el Shell se vuelva a montar al cambiar de
 * ruta, no a una recarga). Solo un lote visto activo y luego terminal cuenta como transición (AC3/AC4).
 */
const vistosActivosEnPestana = new Set<string>();

/**
 * HU #13419 AC6 — nombre de la hija de cada lote de red acotado creado en esta pestaña (el contrato
 * no lo trae). Sobrevive a que el Shell se vuelva a montar, no a una recarga: ahí el aviso dice «Red».
 */
const nombresHijaEnPestana = new Map<string, string>();

/** HU #13419 — datos que solo conoce quien crea el lote. */
export interface OpcionesMostrarLote {
  /** Nombre de la compañía hija de un lote de red acotado («Red · {nombre}»). */
  nombreHija?: string | null;
}

interface MensajeCanal {
  tipo: 'autodescarga';
  loteId: string;
  token: string;
}

const esperar = (ms: number) => new Promise<void>((r) => setTimeout(r, ms));

export interface UseConsolidadoLoteActualOptions {
  /** Solo con `consolidado-masivo.download`: sin permiso no se consulta nada. */
  habilitado?: boolean;
}

export interface UseConsolidadoLoteActual {
  /** Lote activo, o el último terminal retenido; `null` si no hay (204) o aún no se sabe. */
  lote: LoteConsolidados | null;
  /** La última consulta falló (red / 5xx): se muestra el último estado y se reintenta. */
  errorConsulta: boolean;
  /** AC4 — expirado por estado o por reloj: «Descarga expirada», sin botones. */
  expirado: boolean;
  /** HU #13388 — el aviso del lote cancelado ya cumplió su tiempo visible: no se pinta. */
  oculto: boolean;
  /** Sigue un lote concreto (objeto ya leído, o su id). HU #13419: con el nombre de la hija. */
  mostrarLote: (lote: LoteConsolidados | string, opciones?: OpcionesMostrarLote) => void;
  /** HU #13419 AC6 — nombre de la hija del lote mostrado, si se conoce. */
  nombreHija: string | null;
  descargarParte: (numero: number) => Promise<void>;
  /** Número de la parte que se está descargando, o `null`. */
  descargandoParte: number | null;
  errorDescarga: string | null;
  /** HU #13388 — cancela el lote en curso (un clic, sin confirmación). */
  cancelar: () => Promise<void>;
  /** HU #13388 AC2 — la cancelación está en curso: el botón va deshabilitado. */
  cancelando: boolean;
  /** HU #13388 AC6 — 404/403 al cancelar: «No se pudo cancelar la descarga». */
  errorCancelacion: string | null;
}

export function useConsolidadoLoteActual({
  habilitado = true,
}: UseConsolidadoLoteActualOptions = {}): UseConsolidadoLoteActual {
  const [lote, setLote] = useState<LoteConsolidados | null>(null);
  const [errorConsulta, setErrorConsulta] = useState(false);
  const [fijado, setFijado] = useState<{ id: string | null; version: number }>({ id: null, version: 0 });
  const [ahora, setAhora] = useState(() => Date.now());
  const [descargandoParte, setDescargandoParte] = useState<number | null>(null);
  const [errorDescarga, setErrorDescarga] = useState<string | null>(null);
  const [expiradoPorServidor, setExpiradoPorServidor] = useState<string | null>(null);
  const [cancelando, setCancelando] = useState(false);
  const [errorCancelacion, setErrorCancelacion] = useState<string | null>(null);
  /** Lote cancelado que llegó sin `terminadoEn`: instante local en que se vio. */
  const [canceladoVisto, setCanceladoVisto] = useState<{ id: string; en: number } | null>(null);

  const loteRef = useRef<LoteConsolidados | null>(null);
  const vistosActivos = useRef(new Set<string>());
  const canalRef = useRef<BroadcastChannel | null>(null);
  /** Tokens de candado que anunciaron otras pestañas, por lote. */
  const tokensAjenos = useRef(new Map<string, string[]>());
  const vivoRef = useRef(true);
  const cancelandoRef = useRef(false);
  /** Detiene el ciclo de polling en curso (lo publica el efecto del polling). */
  const pararPollingRef = useRef<() => void>(() => undefined);

  useEffect(() => {
    vivoRef.current = true;
    return () => {
      vivoRef.current = false;
    };
  }, []);

  // Canal entre pestañas (opcional: sin BroadcastChannel queda solo el candado de localStorage).
  useEffect(() => {
    if (!habilitado || typeof BroadcastChannel === 'undefined') return;
    let canal: BroadcastChannel;
    try {
      canal = new BroadcastChannel(CANAL_LOTES_CONSOLIDADOS);
    } catch {
      return;
    }
    const alRecibir = (e: MessageEvent) => {
      const m = e.data as Partial<MensajeCanal> | null;
      if (!m || m.tipo !== 'autodescarga' || typeof m.loteId !== 'string' || typeof m.token !== 'string') return;
      const lista = tokensAjenos.current.get(m.loteId) ?? [];
      lista.push(m.token);
      tokensAjenos.current.set(m.loteId, lista);
    };
    canal.addEventListener('message', alRecibir);
    canalRef.current = canal;
    return () => {
      canal.removeEventListener('message', alRecibir);
      canal.close();
      if (canalRef.current === canal) canalRef.current = null;
    };
  }, [habilitado]);

  /**
   * AC3 — candado de autodescarga única entre pestañas. Con `localStorage`: quien no encuentra
   * candado lo escribe, espera la ventana y lo relee; gana el último en escribir (los dos releen el
   * mismo valor). Sin `localStorage`: decide el token menor de los anunciados por el canal.
   */
  const tomarCandado = useCallback(async (loteId: string): Promise<boolean> => {
    if ((tokensAjenos.current.get(loteId) ?? []).length > 0) return false;
    const clave = claveCandadoAutodescarga(loteId);
    const ahoraMs = Date.now();
    const token = `${ahoraMs}-${Math.random().toString(36).slice(2)}`;
    let conStorage = true;
    try {
      if (window.localStorage.getItem(clave) !== null) return false;
      window.localStorage.setItem(clave, token);
    } catch {
      conStorage = false;
    }
    try {
      canalRef.current?.postMessage({ tipo: 'autodescarga', loteId, token } satisfies MensajeCanal);
    } catch {
      /* canal cerrado: queda el storage */
    }
    await esperar(VENTANA_CANDADO_MS);
    if (conStorage) {
      try {
        const gana = window.localStorage.getItem(clave) === token;
        if (gana) purgarCandadosViejos(ahoraMs);
        return gana;
      } catch {
        /* el storage dejó de responder: cae al desempate por canal */
      }
    }
    return (tokensAjenos.current.get(loteId) ?? []).every((ajeno) => token < ajeno);
  }, []);

  const descargar = useCallback(async (objetivo: LoteConsolidados, numero: number) => {
    const parte = objetivo.partes.find((p) => p.numero === numero);
    if (!parte) return;
    setDescargandoParte(numero);
    setErrorDescarga(null);
    try {
      await consolidadoLotesClient.descargarParte(objetivo.id, parte);
    } catch (err) {
      if (!vivoRef.current) return;
      if (err instanceof ConsolidadoLotesApiError && err.status === 410) {
        setExpiradoPorServidor(objetivo.id);
      } else {
        setErrorDescarga(MENSAJE_REINTENTAR_DESCARGA);
      }
    } finally {
      if (vivoRef.current) setDescargandoParte(null);
    }
  }, []);

  /** Aplica una lectura del servidor: estado, transición a terminal y autodescarga. */
  const aplicar = useCallback(
    (leido: LoteConsolidados | null) => {
      if (leido && loteRef.current?.id !== leido.id) {
        setErrorDescarga(null);
        setErrorCancelacion(null);
      }
      loteRef.current = leido;
      setLote(leido);
      if (!leido) return;
      if (leido.estado === 'cancelado' && !leido.terminadoEn) {
        setCanceladoVisto((v) => (v?.id === leido.id ? v : { id: leido.id, en: Date.now() }));
      }
      if (!esEstadoTerminal(leido.estado)) {
        vistosActivos.current.add(leido.id);
        vistosActivosEnPestana.add(leido.id);
        return;
      }
      const transicion = vistosActivos.current.has(leido.id) || vistosActivosEnPestana.has(leido.id);
      vistosActivos.current.delete(leido.id);
      vistosActivosEnPestana.delete(leido.id);
      if (!transicion || !descargable(leido) || loteExpirado(leido, Date.now())) return;
      void tomarCandado(leido.id).then((gana) => {
        if (gana && vivoRef.current) void descargar(leido, 1);
      });
    },
    [descargar, tomarCandado],
  );

  // Polling: al montar, al fijar un lote y cada 4 s mientras no sea terminal.
  useEffect(() => {
    if (!habilitado) return;
    let vivo = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const programar = () => {
      timer = setTimeout(() => void ciclo(), INTERVALO_POLLING_LOTE_MS);
    };
    const ciclo = async () => {
      try {
        const leido = fijado.id
          ? await consolidadoLotesClient.obtenerLote(fijado.id)
          : await consolidadoLotesClient.obtenerLoteActual();
        if (!vivo) return;
        setErrorConsulta(false);
        aplicar(leido);
        if (leido && !esEstadoTerminal(leido.estado)) programar();
      } catch (err) {
        if (!vivo) return;
        const status = err instanceof ConsolidadoLotesApiError ? err.status : 0;
        if (status === 404 && fijado.id) {
          // El lote fijado ya no existe o no es del usuario: vuelve al «actual».
          setFijado((f) => ({ id: null, version: f.version + 1 }));
          return;
        }
        if (status === 403 || status === 404) {
          // Sin permiso (retirado en caliente) o sin lote: no se pinta nada ni se insiste.
          aplicar(null);
          return;
        }
        // AC5 — red o 5xx: se conserva el último estado y se reintenta sin bloquear.
        setErrorConsulta(true);
        programar();
      }
    };
    const parar = () => {
      vivo = false;
      if (timer) clearTimeout(timer);
    };
    pararPollingRef.current = parar;
    void ciclo();
    return parar;
  }, [habilitado, fijado, aplicar]);

  // AC4 — con la app abierta, al cumplirse las 24 h el aviso pasa a «Descarga expirada».
  const expiracion = lote ? instanteExpiracion(lote) : null;
  useEffect(() => {
    if (expiracion === null) return;
    // Si ya pasó (lote leído con un reloj viejo), el temporizador corre de inmediato.
    const falta = Math.max(0, expiracion - Date.now());
    const t = setTimeout(() => setAhora(Date.now()), Math.min(falta + 1, MAX_TIMEOUT_MS));
    return () => clearTimeout(t);
  }, [expiracion]);

  // HU #13388 AC3 — a los 8 s de `terminadoEn` el aviso del lote cancelado se oculta solo.
  const ocultarCanceladoEn = lote
    ? instanteOcultarCancelado(lote, canceladoVisto?.id === lote.id ? canceladoVisto.en : null)
    : null;
  useEffect(() => {
    if (ocultarCanceladoEn === null) return;
    const falta = Math.max(0, ocultarCanceladoEn - Date.now());
    const t = setTimeout(() => setAhora(Date.now()), Math.min(falta, MAX_TIMEOUT_MS));
    return () => clearTimeout(t);
  }, [ocultarCanceladoEn]);

  const mostrarLote = useCallback(
    (objetivo: LoteConsolidados | string, opciones?: OpcionesMostrarLote) => {
      const id = typeof objetivo === 'string' ? objetivo : objetivo.id;
      const nombreHija = opciones?.nombreHija?.trim();
      if (nombreHija) nombresHijaEnPestana.set(id, nombreHija);
      if (typeof objetivo !== 'string') aplicar(objetivo);
      setFijado((f) => ({ id, version: f.version + 1 }));
    },
    [aplicar],
  );

  /** Vuelve a leer el lote por id (AC5/AC6: estado real tras un 409/404/403). */
  const refrescar = useCallback((id: string) => {
    setFijado((f) => ({ id, version: f.version + 1 }));
  }, []);

  const cancelar = useCallback(async () => {
    const objetivo = loteRef.current;
    if (!objetivo || esEstadoTerminal(objetivo.estado) || cancelandoRef.current) return;
    cancelandoRef.current = true;
    setCancelando(true);
    setErrorCancelacion(null);
    try {
      const leido = await consolidadoLotesClient.cancelar(objetivo.id);
      if (!vivoRef.current) return;
      // AC3 — 202 `cancelado`: terminal, se corta el polling (una lectura en vuelo ya no se aplica).
      if (esEstadoTerminal(leido.estado)) pararPollingRef.current();
      aplicar(leido);
    } catch (err) {
      if (!vivoRef.current) return;
      const status = err instanceof ConsolidadoLotesApiError ? err.status : 0;
      if (status === 409) {
        // AC5 — `lote_terminado`: se muestra el estado real (con sus partes si completó).
        refrescar(objetivo.id);
      } else if (status === 403 || status === 404) {
        setErrorCancelacion(MENSAJE_NO_SE_PUDO_CANCELAR);
        refrescar(objetivo.id);
      }
      // AC6 — red (o 5xx): el botón se rehabilita y el lote sigue como estaba; el polling continúa.
    } finally {
      cancelandoRef.current = false;
      if (vivoRef.current) setCancelando(false);
    }
  }, [aplicar, refrescar]);

  const descargarParte = useCallback(
    async (numero: number) => {
      const actual = loteRef.current;
      if (actual) await descargar(actual, numero);
    },
    [descargar],
  );

  const expirado = lote
    ? expiradoPorServidor === lote.id || loteExpirado(lote, ahora)
    : false;

  const canceladoVencido = ocultarCanceladoEn !== null && ahora >= ocultarCanceladoEn;

  return {
    lote,
    errorConsulta,
    expirado,
    oculto: lote !== null && canceladoVencido,
    mostrarLote,
    nombreHija: lote ? (nombresHijaEnPestana.get(lote.id) ?? null) : null,
    descargarParte,
    descargandoParte,
    errorDescarga,
    cancelar,
    cancelando,
    errorCancelacion,
  };
}
