"use client";

import { useEffect, useState } from "react";

function prefiereMenosMovimiento(): boolean {
  return typeof window !== "undefined" && typeof window.matchMedia === "function" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

/** Diapositiva activa de un carrusel con avance automático; con «reducir movimiento» solo cambia a mano. */
export function useCarrusel(cantidad: number, cadaMs: number) {
  const [activa, setActiva] = useState(0);
  useEffect(() => {
    if (cantidad < 2 || prefiereMenosMovimiento()) return;
    const id = setInterval(() => setActiva((v) => (v + 1) % cantidad), cadaMs);
    return () => clearInterval(id);
  }, [cantidad, cadaMs]);
  return { activa, ir: setActiva };
}
