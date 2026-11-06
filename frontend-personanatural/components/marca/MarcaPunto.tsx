"use client";

// HU #14379 — La marca del organismo llega por contexto a todo el flujo del punto (entrada, wizard y cierre). Fuera de
// un `ProveedorMarca` (web de persona, otros flujos) no hay marca y todo se dibuja con la imagen FLIT.
import { createContext, useContext, type ReactNode } from "react";
import { ASSETS } from "@/lib/assets";
import type { MarcaAplicable } from "@/lib/marca/marca";

const ContextoMarca = createContext<MarcaAplicable | null>(null);

export function ProveedorMarca({ marca, children }: { marca: MarcaAplicable | null; children: ReactNode }) {
  return <ContextoMarca.Provider value={marca}>{children}</ContextoMarca.Provider>;
}

export const useMarca = (): MarcaAplicable | null => useContext(ContextoMarca);

/** Logo del portal: el del organismo si lo tiene; si no, el de FLIT. Con nombre de marca, este va al lado como texto plano. */
export function LogoPortal({ alt, className = "h-9 w-auto" }: { alt: string; className?: string }) {
  const marca = useMarca();
  const nombre = marca?.nombre ?? null;
  return (
    <span className="flex items-center gap-3">
      {/* eslint-disable-next-line @next/next/no-img-element -- ruta del propio BFF, sin optimizador de Next */}
      <img src={marca?.logoUrl ?? ASSETS.logo} alt={nombre ?? alt} className={className} />
      {nombre && (
        <span className="max-w-[16rem] truncate text-base font-bold text-primary" data-testid="nombre-marca">
          {nombre}
        </span>
      )}
    </span>
  );
}

/** Banner del organismo en la entrada del punto. Sin banner propio no se dibuja nada (el punto no tenía banner). */
export function BannerMarca() {
  const marca = useMarca();
  if (!marca?.bannerUrl) return null;
  return (
    <div className="dashboard-banner overflow-hidden rounded-[9px] border border-border bg-card">
      {/* eslint-disable-next-line @next/next/no-img-element -- ruta del propio BFF, sin optimizador de Next */}
      <img src={marca.bannerUrl} alt={marca.nombre ? `Banner de ${marca.nombre}` : "Banner del organismo"} className="h-full w-full object-contain" />
    </div>
  );
}
