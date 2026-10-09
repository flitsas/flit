"use client";

import type { ReactNode } from "react";
import { InlineAlert } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { TITULO_CLS } from "@/components/ui/estilos";
import { tienePermiso } from "@/lib/sesion";

/** Encabezado común de una pantalla: título, descripción y acciones a la derecha. */
export function Encabezado({ titulo, descripcion, acciones }: { titulo: string; descripcion?: ReactNode; acciones?: ReactNode }) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-3">
      <div className="flex flex-col gap-1">
        <h1 className={TITULO_CLS}>{titulo}</h1>
        {descripcion && <p className="text-sm text-[#5B6B82] dark:text-slate-300">{descripcion}</p>}
      </div>
      {acciones && <div className="flex flex-wrap items-center gap-2">{acciones}</div>}
    </header>
  );
}

/**
 * Muestra el contenido solo si la sesión lo permite: `superAdmin` exige ser administrador de FLIT; `permiso`, tener ese
 * permiso del producto. Mientras carga la sesión no muestra nada.
 */
export function Requiere({ superAdmin, permiso, children }: { superAdmin?: boolean; permiso?: string; children: ReactNode }) {
  const { sesion, cargando, error } = useSesion();
  if (cargando) return <p className="text-sm opacity-60">Cargando…</p>;
  if (error) return <InlineAlert tone="error">{error}</InlineAlert>;
  if (superAdmin && !sesion?.usuario.esSuperAdmin)
    return <InlineAlert tone="warning" title="No tienes acceso">Esta configuración es solo del administrador de FLIT.</InlineAlert>;
  if (permiso && !tienePermiso(sesion, permiso))
    return <InlineAlert tone="warning" title="No tienes acceso">Tu rol no tiene permiso para esta pantalla. Pídelo a tu administrador.</InlineAlert>;
  return <>{children}</>;
}
