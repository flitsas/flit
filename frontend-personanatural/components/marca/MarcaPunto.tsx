"use client";

// HU #14379 — La marca del organismo llega por contexto a todo el flujo del punto (entrada, wizard y cierre). Fuera de
// un `ProveedorMarca` (web de persona, otros flujos) no hay marca y todo se dibuja con la imagen FLIT.
import Link from "next/link";
import { usePathname } from "next/navigation";
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

/**
 * Logo con enlace al inicio. En el enlace del punto (`/o/{codigo}`) vuelve a ese punto, para no sacar a la persona de su
 * organismo; en la web de persona sigue yendo a la raíz.
 */
export function LogoInicio({ alt, etiqueta }: { alt: string; etiqueta: string }) {
  const marca = useMarca();
  const punto = /^\/o\/[^/]+/.exec(usePathname() ?? "")?.[0];
  const ariaLabel = punto ? `${marca?.nombre ?? alt} — Ir al inicio del punto` : etiqueta;
  return (
    <Link href={punto ?? "/"} aria-label={ariaLabel}>
      <LogoPortal alt={alt} />
    </Link>
  );
}

/**
 * Banner en la entrada del punto. Una marca publicada sin banner propio usa el de FLIT (regla 12 de la épica); sin marca
 * publicada no se dibuja nada, porque el punto sin marca sigue como hoy.
 */
export function BannerMarca() {
  const marca = useMarca();
  if (!marca) return null;
  return (
    <div className="dashboard-banner overflow-hidden rounded-[9px] border border-border bg-card">
      {/* eslint-disable-next-line @next/next/no-img-element -- ruta del propio BFF, sin optimizador de Next */}
      <img
        src={marca.bannerUrl ?? ASSETS.banners[0].src}
        alt={marca.bannerUrl ? (marca.nombre ? `Banner de ${marca.nombre}` : "Banner del organismo") : ASSETS.banners[0].alt}
        className="h-full w-full object-contain"
      />
    </div>
  );
}
