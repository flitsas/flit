// Capa de datos de liquidaciones (contracts/openapi/impuestos.v1.yaml 1.0.0, tags Liquidaciones y Recibos). Todo pasa
// por el BFF (/api/v1/impuestos/*) con la sesión del producto; la compañía elegida por el Super Admin viaja como
// ?empresa= (conEmpresa). Los PDF y la plantilla se bajan por el mismo proxy: el navegador nunca habla con el proveedor.
"use client";

import { useCallback, useEffect, useEffectEvent, useRef, useState } from "react";
import { isReauthenticating, reauthenticate } from "@flit/auth/client";
import { API_PREFIX, ApiError, apiFetch } from "@/lib/api";
import { conEmpresa } from "@/lib/empresaSeleccionada";
import type {
  AnalisisCarga,
  EstadoItem,
  FiltrosLiquidaciones,
  ItemDetalle,
  Item,
  Liquidacion,
  LiquidacionConItems,
  Pagina,
  ResumenLiquidacion,
} from "./tipos";

const id = (v: string) => encodeURIComponent(v);

/** Rutas del contrato, relativas a API_PREFIX (/api/v1/impuestos). */
export const RUTAS_LIQUIDACIONES = {
  listado: "/liquidaciones",
  individual: "/liquidaciones/individual",
  plantilla: "/liquidaciones/plantilla",
  analizar: "/liquidaciones/masiva/analizar",
  masiva: "/liquidaciones/masiva",
  liquidacion: (l: string) => `/liquidaciones/${id(l)}`,
  items: (l: string) => `/liquidaciones/${id(l)}/items`,
  item: (l: string, i: string) => `/liquidaciones/${id(l)}/items/${id(i)}`,
  cerrar: (l: string) => `/liquidaciones/${id(l)}/cerrar`,
  relanzar: (l: string, i: string) => `/liquidaciones/${id(l)}/items/${id(i)}/relanzar`,
  recibo: (l: string, i: string, r: string) => `/liquidaciones/${id(l)}/items/${id(i)}/recibos/${id(r)}`,
} as const;

interface Opciones {
  empresa?: string | null;
  signal?: AbortSignal;
}

/** Query del grid; sin los vacíos. */
export function queryListado(f: Partial<FiltrosLiquidaciones>): string {
  const p = new URLSearchParams();
  if (f.tipo) p.set("tipo", f.tipo);
  if (f.estado) p.set("estado", f.estado);
  if (f.desde) p.set("desde", f.desde);
  if (f.hasta) p.set("hasta", f.hasta);
  if (f.placa) p.set("placa", f.placa);
  if (f.page) p.set("page", String(f.page));
  if (f.pageSize) p.set("pageSize", String(f.pageSize));
  return p.toString();
}

export function listarLiquidaciones(f: FiltrosLiquidaciones, o: Opciones = {}): Promise<Pagina<ResumenLiquidacion>> {
  return apiFetch(`${RUTAS_LIQUIDACIONES.listado}?${queryListado(f)}`, o);
}

export function crearIndividual(placa: string, o: Opciones = {}): Promise<LiquidacionConItems> {
  return apiFetch(RUTAS_LIQUIDACIONES.individual, { ...o, method: "POST", body: { placa } });
}

function conArchivo(archivo: File): FormData {
  const datos = new FormData();
  datos.append("archivo", archivo);
  return datos;
}

export function analizarCarga(archivo: File, o: Opciones = {}): Promise<AnalisisCarga> {
  return apiFetch(RUTAS_LIQUIDACIONES.analizar, { ...o, method: "POST", body: conArchivo(archivo) });
}

export function crearMasiva(archivo: File, o: Opciones = {}): Promise<Liquidacion> {
  return apiFetch(RUTAS_LIQUIDACIONES.masiva, { ...o, method: "POST", body: conArchivo(archivo) });
}

export function obtenerLiquidacion(l: string, o: Opciones = {}): Promise<Liquidacion> {
  return apiFetch(RUTAS_LIQUIDACIONES.liquidacion(l), o);
}

export function listarItems(
  l: string,
  f: { estado?: EstadoItem | null; placa?: string; page: number; pageSize: number },
  o: Opciones = {},
): Promise<Pagina<Item>> {
  const p = new URLSearchParams();
  if (f.estado) p.set("estado", f.estado);
  if (f.placa) p.set("placa", f.placa);
  p.set("page", String(f.page));
  p.set("pageSize", String(f.pageSize));
  return apiFetch(`${RUTAS_LIQUIDACIONES.items(l)}?${p.toString()}`, o);
}

export function obtenerItem(l: string, i: string, o: Opciones = {}): Promise<ItemDetalle> {
  return apiFetch(RUTAS_LIQUIDACIONES.item(l, i), o);
}

export function cerrarLiquidacion(l: string, motivo: string | null, o: Opciones = {}): Promise<Liquidacion> {
  return apiFetch(RUTAS_LIQUIDACIONES.cerrar(l), { ...o, method: "POST", body: { motivo } });
}

export function relanzarItem(l: string, i: string, o: Opciones = {}): Promise<LiquidacionConItems> {
  return apiFetch(RUTAS_LIQUIDACIONES.relanzar(l, i), { ...o, method: "POST", body: { confirmarPosibleDuplicado: true } });
}

// ── Descargas por el BFF (blob) ──────────────────────────────────────────────────────────────────────────────────────

/** Nombre del Content-Disposition (`filename*=UTF-8''…` o `filename=…`); si no, el de por defecto. */
export function nombreArchivo(cabecera: string | null, porDefecto: string): string {
  if (!cabecera) return porDefecto;
  const extendido = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(cabecera);
  if (extendido) {
    try {
      return decodeURIComponent(extendido[1].trim());
    } catch {
      // Codificación inválida: se intenta con el nombre simple.
    }
  }
  const simple = /filename\s*=\s*"?([^";]+)"?/i.exec(cabecera);
  return simple?.[1].trim() || porDefecto;
}

function guardar(blob: Blob, nombre: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = nombre;
  a.rel = "noopener";
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

/** Baja un archivo del BFF y lo entrega como descarga. Un error HTTP lanza ApiError con el `code` del cuerpo. */
async function descargar(ruta: string, porDefecto: string, empresa?: string | null): Promise<void> {
  if (isReauthenticating()) return new Promise<void>(() => {});
  const respuesta = await fetch(`${API_PREFIX}${conEmpresa(ruta, empresa)}`, { method: "GET", cache: "no-store" });
  if (!respuesta.ok) {
    if (respuesta.status === 401) reauthenticate({ silent: true });
    let datos: Record<string, unknown> | null = null;
    try {
      const texto = await respuesta.text();
      datos = texto ? (JSON.parse(texto) as Record<string, unknown>) : null;
    } catch {
      datos = null;
    }
    throw new ApiError(respuesta.status, "No se pudo descargar el archivo.", typeof datos?.code === "string" ? datos.code : undefined, datos ?? undefined);
  }
  guardar(await respuesta.blob(), nombreArchivo(respuesta.headers.get("Content-Disposition"), porDefecto));
}

export function descargarRecibo(l: string, i: string, r: string, empresa?: string | null): Promise<void> {
  return descargar(RUTAS_LIQUIDACIONES.recibo(l, i, r), "recibo.pdf", empresa);
}

export function descargarPlantilla(empresa?: string | null): Promise<void> {
  return descargar(RUTAS_LIQUIDACIONES.plantilla, "plantilla-liquidacion-masiva.xlsx", empresa);
}

// ── Carga con sondeo (spec-visual §3.5) ──────────────────────────────────────────────────────────────────────────────

export const SONDEO_INICIAL_MS = 5_000;
export const SONDEO_MAXIMO_MS = 30_000;

export type Carga<T> =
  | { tipo: "cargando" }
  | { tipo: "error"; error: unknown }
  | { tipo: "listo"; dato: T; desactualizadoDesde: Date | null };

/**
 * Carga un recurso y, mientras `sondear(dato)` sea true, lo vuelve a pedir: cada 5 s, el doble si nada cambió (hasta 30 s)
 * y de nuevo 5 s cuando cambia. Un fallo del sondeo no borra lo leído: marca `desactualizadoDesde`. `clave` reinicia todo.
 */
export function useCargaConSondeo<T>(
  clave: string,
  cargar: (signal: AbortSignal) => Promise<T>,
  sondear: (dato: T) => boolean,
): { carga: Carga<T>; reintentar: () => void; reemplazar: (dato: T) => void } {
  const [carga, setCarga] = useState<Carga<T>>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [claveVista, setClaveVista] = useState(clave);
  const ultimo = useRef<string>("");
  const retraso = useRef(SONDEO_INICIAL_MS);
  const [tic, setTic] = useState(0);

  if (clave !== claveVista) {
    setClaveVista(clave);
    setCarga({ tipo: "cargando" });
  }

  const pedir = useEffectEvent((signal: AbortSignal) => cargar(signal));

  // Carga inicial (y al reintentar o cambiar de clave).
  useEffect(() => {
    const control = new AbortController();
    pedir(control.signal).then(
      (dato) => {
        if (control.signal.aborted) return;
        ultimo.current = JSON.stringify(dato);
        retraso.current = SONDEO_INICIAL_MS;
        setCarga({ tipo: "listo", dato, desactualizadoDesde: null });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error });
      },
    );
    return () => control.abort();
  }, [clave, intento]);

  const listo = carga.tipo === "listo" ? carga : null;
  const activo = listo ? sondear(listo.dato) : false;

  // Sondeo: un temporizador por vuelta; se detiene al pasar a terminal.
  useEffect(() => {
    if (!activo) return;
    const control = new AbortController();
    const t = setTimeout(() => {
      pedir(control.signal).then(
        (dato) => {
          if (control.signal.aborted) return;
          const serial = JSON.stringify(dato);
          retraso.current = serial === ultimo.current ? Math.min(retraso.current * 2, SONDEO_MAXIMO_MS) : SONDEO_INICIAL_MS;
          ultimo.current = serial;
          setCarga({ tipo: "listo", dato, desactualizadoDesde: null });
          setTic((n) => n + 1);
        },
        () => {
          if (control.signal.aborted) return;
          setCarga((c) => (c.tipo === "listo" ? { ...c, desactualizadoDesde: c.desactualizadoDesde ?? new Date() } : c));
          retraso.current = Math.min(retraso.current * 2, SONDEO_MAXIMO_MS);
          setTic((n) => n + 1);
        },
      );
    }, retraso.current);
    return () => {
      clearTimeout(t);
      control.abort();
    };
  }, [activo, tic, clave]);

  const reintentar = useCallback(() => {
    setCarga((c) => (c.tipo === "listo" ? c : { tipo: "cargando" }));
    setIntento((n) => n + 1);
  }, []);

  const reemplazar = useCallback((dato: T) => {
    ultimo.current = JSON.stringify(dato);
    setCarga({ tipo: "listo", dato, desactualizadoDesde: null });
  }, []);

  return { carga, reintentar, reemplazar };
}
