"use client";

// Mejora sobre la marcha (sin HU) — Tarifas/Cobro en dos pasos en lugar de dos tablas apiladas: 1) los precios y
// 2) los medios de pago donde se cobran. Sirve a Plataforma > Persona Natural > Tarifas y al hub del OT > Cobro.
// «Definir precio» (paso 2) abre el formulario de precio del trámite y, al cerrarlo, devuelve a Medios de pago con
// los montos recargados. Los dos pasos quedan montados: cada tabla conserva su página y sus filtros.
import { useCallback, useState, type ReactNode } from "react";
import { SectionTabs, type SectionTab } from "@/components/atom/SectionTabs";
import type { SolicitudPrecio } from "./TarifasTab";

type PasoId = "precios" | "medios";

const ANCLA_TITULO_PRECIOS = "pn-tarifas-titulo";

export interface CobroEnPasosProps {
  /** Cómo se llama el paso 1 en esta pantalla: «Precios» en el OT, «Tarifas» en Plataforma. */
  nombrePrecios: string;
  /** Aviso a quien lo monta (p. ej. refrescar el estado de configuración) cuando cambian los precios. */
  onCambio?: () => void;
  precios: (p: {
    onCambio: () => void;
    solicitudPrecio: SolicitudPrecio | null;
    onSolicitudCerrada: () => void;
  }) => ReactNode;
  medios: (p: {
    onIrAPrecios: () => void;
    onDefinirPrecio: (tipoTramiteId: string) => void;
    recargar: number;
  }) => ReactNode;
}

export function CobroEnPasos({ nombrePrecios, onCambio, precios, medios }: CobroEnPasosProps) {
  const [activo, setActivo] = useState<PasoId>("precios");
  const [recargar, setRecargar] = useState(0);
  const [solicitud, setSolicitud] = useState<SolicitudPrecio | null>(null);

  const irAPrecios = useCallback(() => {
    setActivo("precios");
    // El panel se muestra en el siguiente cuadro: ahí se lleva el foco al título de la sección.
    requestAnimationFrame(() => {
      const titulo = document.getElementById(ANCLA_TITULO_PRECIOS);
      if (!titulo) return;
      titulo.setAttribute("tabindex", "-1");
      titulo.scrollIntoView?.({ block: "start" });
      titulo.focus({ preventScroll: true });
    });
  }, []);

  // El formulario vive en el paso de precios: se muestra ese paso con el trámite elegido y se vuelve al cerrarlo.
  const definirPrecio = useCallback((tipoTramiteId: string) => {
    setSolicitud({ tipoTramiteId, nonce: Date.now() });
    setActivo("precios");
  }, []);

  const solicitudCerrada = useCallback(() => {
    setSolicitud(null);
    setActivo("medios");
  }, []);

  const preciosCambiaron = useCallback(() => {
    setRecargar((n) => n + 1);
    onCambio?.();
  }, [onCambio]);

  const pasos: SectionTab<PasoId>[] = [
    {
      id: "precios",
      label: `1 · ${nombrePrecios}`,
      title: "Cuánto cuesta cada trámite",
      content: precios({ onCambio: preciosCambiaron, solicitudPrecio: solicitud, onSolicitudCerrada: solicitudCerrada }),
    },
    {
      id: "medios",
      label: "2 · Medios de pago",
      title: "En qué medios se cobra cada valor",
      content: medios({ onIrAPrecios: irAPrecios, onDefinirPrecio: definirPrecio, recargar }),
    },
  ];

  return <SectionTabs tabs={pasos} active={activo} onChange={setActivo} ariaLabel="Pasos del cobro del portal" />;
}
