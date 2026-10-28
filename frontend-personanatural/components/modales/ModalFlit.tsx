"use client";

import { X, type LucideIcon } from "lucide-react";
import { useEffect, useId, useRef, type ReactNode } from "react";
import { boton } from "@/components/landing/estilos";

interface Props {
  abierto: boolean;
  onCerrar: () => void;
  titulo: string;
  descripcion?: ReactNode;
  icono: LucideIcon;
  ancho?: "4xl" | "2xl";
  /** Hay una petición en curso que no debe duplicarse: no se puede cerrar con Escape, clic fuera ni «Cerrar». */
  ocupado?: boolean;
  /** Deja que el contenido (una lista desplegable) sobresalga del modal en pantallas anchas, como el diseño de cotizar. */
  desborde?: boolean;
  children: ReactNode;
}

/**
 * Modal del diseño (patrón de §4.4): <dialog> nativo, con foco atrapado, Escape y retorno del foco al botón que lo abrió.
 * El contenido solo existe mientras está abierto, así que al cerrar se descarta todo lo que el ciudadano escribió en él.
 */
export function ModalFlit({ abierto, onCerrar, titulo, descripcion, icono: Icono, ancho = "4xl", ocupado = false, desborde = false, children }: Props) {
  const ref = useRef<HTMLDialogElement>(null);
  const idTitulo = useId();
  const idDescripcion = useId();

  useEffect(() => {
    const dialogo = ref.current;
    if (!dialogo) return;
    if (abierto && !dialogo.open) {
      if (typeof dialogo.showModal === "function") dialogo.showModal();
      else dialogo.setAttribute("open", "");
    }
    if (!abierto && dialogo.open) {
      if (typeof dialogo.close === "function") dialogo.close();
      else dialogo.removeAttribute("open");
    }
  }, [abierto]);

  const cerrar = () => {
    if (!ocupado) onCerrar();
  };

  return (
    <dialog
      ref={ref}
      aria-labelledby={idTitulo}
      aria-describedby={descripcion ? idDescripcion : undefined}
      aria-busy={ocupado}
      // Escape: el estado manda; el navegador no cierra por su cuenta.
      onCancel={(e) => {
        e.preventDefault();
        cerrar();
      }}
      onClose={() => {
        if (abierto && !ocupado) onCerrar();
      }}
      onClick={(e) => {
        if (e.target === ref.current) cerrar();
      }}
      className={`m-auto max-h-[90dvh] w-[calc(100%-2rem)] ${ancho === "4xl" ? "max-w-4xl" : "max-w-2xl"} overflow-y-auto ${desborde ? "sm:overflow-visible" : ""} rounded-[9px] border border-border bg-card p-6 text-foreground backdrop:bg-primary/50 backdrop:backdrop-blur-sm`}
    >
      {abierto && (
        <>
          <button type="button" aria-label="Cerrar" disabled={ocupado} onClick={cerrar} className={boton("ghost", "absolute right-2 top-2 h-11 w-11 p-0", "none")}>
            <X aria-hidden />
          </button>
          <div className="flex flex-col items-center gap-2 px-8 text-center">
            <span aria-hidden className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-primary/10 text-primary">
              <Icono className="h-5 w-5" />
            </span>
            <h2 id={idTitulo} className="text-xl font-bold text-primary">{titulo}</h2>
          </div>
          {descripcion && <p id={idDescripcion} className="mt-1 text-center text-xs text-foreground/70">{descripcion}</p>}
          {children}
        </>
      )}
    </dialog>
  );
}
