import type { ReactNode } from "react";
import { cn } from "./cn";

/**
 * Avance horizontal del Manual UI Kit 2026 (propuesta-2, `ol.pasos` del asistente y `ol.linea` del seguimiento):
 * círculos de 32 px numerados unidos por una línea de 2 px; los hechos en verde con check blanco, el actual con borde
 * azul y aro --kit-hover, los pendientes en gris. Se desliza en horizontal si no cabe. Solo presentación: quien lo usa
 * decide el estado de cada paso con su propia lógica.
 */
export type EstadoPaso = "hecho" | "actual" | "pendiente";

export interface PasoHorizontal {
  etiqueta: ReactNode;
  estado: EstadoPaso;
  /** Si el paso se puede visitar, el manejador; sin él es solo informativo. */
  onClick?: () => void;
}

export function PasosHorizontal({ pasos, ariaLabel = "Pasos", className }: { pasos: PasoHorizontal[]; ariaLabel?: string; className?: string }) {
  return (
    <ol aria-label={ariaLabel} className={cn("kit-pasos", className)}>
      {pasos.map((p, i) => {
        const cuerpo = (
          <>
            <span className="kit-paso__num" aria-hidden="true">
              {p.estado === "hecho" ? (
                <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round"><path d="m5 12 5 5 9-10" /></svg>
              ) : (
                i + 1
              )}
            </span>
            <span className="kit-paso__etiqueta">{p.etiqueta}</span>
          </>
        );
        return (
          <li key={i} className="kit-paso" data-estado={p.estado} aria-current={p.estado === "actual" ? "step" : undefined}>
            {p.onClick ? (
              <button type="button" onClick={p.onClick} className="kit-paso__boton">
                {cuerpo}
              </button>
            ) : (
              <span className="kit-paso__boton">{cuerpo}</span>
            )}
          </li>
        );
      })}
    </ol>
  );
}
