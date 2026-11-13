"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  leerTodasNotificaciones,
  listarNotificaciones,
  marcarNotificacionLeida,
  type NotificacionDiagnostico,
} from "@/lib/notificaciones/api";

/** Sondeo de la campana (AC51): cada 30 s con la pestaña visible. */
export const INTERVALO_NOTIFICACIONES_MS = 30_000;

/**
 * Evento de ventana para pedir un sondeo inmediato (AC51). Lo disparan las acciones que lanzan trabajo en segundo
 * plano: pedir el ZIP de evidencias o crear trámites en bloque. Nombre constante para que otros módulos no lo repitan.
 */
export const EVENTO_SONDEAR_NOTIFICACIONES = "dx:sondear-notificaciones";

/** Pide a la campana montada un sondeo inmediato. Sin campana montada no hace nada. */
export function pedirSondeoNotificaciones(): void {
  if (typeof window !== "undefined") window.dispatchEvent(new Event(EVENTO_SONDEAR_NOTIFICACIONES));
}

export type EstadoNotificaciones = "cargando" | "listo" | "error";

export interface Notificaciones {
  estado: EstadoNotificaciones;
  items: NotificacionDiagnostico[];
  noLeidas: number;
  /** Cuenta de sondeos con datos nuevos (200): permite detectar lo que llegó después de la primera carga. */
  version: number;
  marcarLeida: (id: string) => Promise<void>;
  leerTodas: () => Promise<void>;
  reintentar: () => void;
}

const visible = () => typeof document === "undefined" || document.visibilityState !== "hidden";

export function useNotificaciones(): Notificaciones {
  const [estado, setEstado] = useState<EstadoNotificaciones>("cargando");
  const [items, setItems] = useState<NotificacionDiagnostico[]>([]);
  const [noLeidas, setNoLeidas] = useState(0);
  const [version, setVersion] = useState(0);
  const etag = useRef<string | null>(null);
  const temporizador = useRef<ReturnType<typeof setTimeout> | null>(null);
  const enVuelo = useRef<AbortController | null>(null);
  const montado = useRef(true);
  /** El sondeo se reprograma a sí mismo por esta referencia (la regla de inmutabilidad de hooks no admite auto-referencia). */
  const sondearRef = useRef<() => Promise<void>>(async () => {});
  const itemsRef = useRef(items);
  useEffect(() => {
    itemsRef.current = items;
  }, [items]);
  const noLeidasRef = useRef(noLeidas);
  useEffect(() => {
    noLeidasRef.current = noLeidas;
  }, [noLeidas]);

  const programar = useCallback((fn: () => void) => {
    if (temporizador.current) clearTimeout(temporizador.current);
    temporizador.current = visible() ? setTimeout(fn, INTERVALO_NOTIFICACIONES_MS) : null;
  }, []);

  const sondear = useCallback(async () => {
    if (temporizador.current) clearTimeout(temporizador.current);
    temporizador.current = null;
    enVuelo.current?.abort();
    const control = new AbortController();
    enVuelo.current = control;
    try {
      const r = await listarNotificaciones(etag.current, control.signal);
      if (!montado.current || control.signal.aborted) return;
      if (!r.sinCambios) {
        etag.current = r.etag;
        setItems(r.datos.items);
        setNoLeidas(r.datos.no_leidas);
        setVersion((v) => v + 1);
      }
      setEstado("listo");
    } catch {
      if (!montado.current || control.signal.aborted) return;
      setEstado("error");
    }
    if (montado.current && enVuelo.current === control) programar(() => void sondearRef.current());
  }, [programar]);

  useEffect(() => {
    sondearRef.current = sondear;
  }, [sondear]);

  useEffect(() => {
    montado.current = true;
    void sondear();
    const alCambiarVisibilidad = () => {
      if (visible()) void sondear();
      else if (temporizador.current) {
        clearTimeout(temporizador.current);
        temporizador.current = null;
      }
    };
    const alPedir = () => void sondear();
    document.addEventListener("visibilitychange", alCambiarVisibilidad);
    window.addEventListener(EVENTO_SONDEAR_NOTIFICACIONES, alPedir);
    return () => {
      montado.current = false;
      enVuelo.current?.abort();
      if (temporizador.current) clearTimeout(temporizador.current);
      document.removeEventListener("visibilitychange", alCambiarVisibilidad);
      window.removeEventListener(EVENTO_SONDEAR_NOTIFICACIONES, alPedir);
    };
  }, [sondear]);

  /**
   * Observación 1 del code review (HU #14562): si el POST falla, lo marcado en el cliente se revierte (contador y avisos) y
   * el sondeo siguiente va sin If-None-Match. Con el ETag viejo el servidor respondía 304 y el contador quedaba bajo.
   */
  const revertir = useCallback((ids: ReadonlySet<string>, marca: string, descontadas: number) => {
    etag.current = null;
    setItems((lista) => lista.map((i) => (ids.has(i.id) && i.leida_en === marca ? { ...i, leida_en: null } : i)));
    setNoLeidas((n) => n + descontadas);
  }, []);

  const marcarLeida = useCallback(
    async (id: string) => {
      const ahora = new Date().toISOString();
      const sinLeer = itemsRef.current.some((i) => i.id === id && !i.leida_en);
      if (sinLeer) setNoLeidas((n) => Math.max(0, n - 1));
      setItems((lista) => lista.map((i) => (i.id === id && !i.leida_en ? { ...i, leida_en: ahora } : i)));
      try {
        await marcarNotificacionLeida(id);
      } catch {
        if (sinLeer) revertir(new Set([id]), ahora, 1);
      }
      void sondear();
    },
    [revertir, sondear],
  );

  const leerTodas = useCallback(async () => {
    const ahora = new Date().toISOString();
    const marcadas = new Set(itemsRef.current.filter((i) => !i.leida_en).map((i) => i.id));
    const antes = noLeidasRef.current;
    setItems((lista) => lista.map((i) => (i.leida_en ? i : { ...i, leida_en: ahora })));
    setNoLeidas(0);
    try {
      await leerTodasNotificaciones();
    } catch {
      revertir(marcadas, ahora, antes);
    }
    void sondear();
  }, [revertir, sondear]);

  const reintentar = useCallback(() => {
    setEstado("cargando");
    void sondear();
  }, [sondear]);

  return { estado, items, noLeidas, version, marcarLeida, leerTodas, reintentar };
}
