"use client";

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { Icono } from "@flit/ui/Icono";
import { IconoAviso } from "@flit/ui/IconoAviso";

// Toast mínimo sin dependencias externas (HU #10194). Colores de la propuesta (HU #14486): éxito con el azul del
// check, advertencia y error con el naranja de las marcas de estado.
// Región aria-live para accesibilidad (WCAG 2.1 AA).
export type ToastType = "success" | "warning" | "error";

const COLOR_TOAST: Record<ToastType, string> = { success: "var(--di-success-dot)", warning: "var(--di-alerta-punto)", error: "var(--di-alerta-punto)" };

/** Cierre del toast: el ícono es de 14 px; el ::after extiende el toque a 40×40 sin mover nada (iPhone/iPad). */
export const CERRAR_TOAST_CLS =
  "relative after:absolute after:left-1/2 after:top-1/2 after:size-10 after:-translate-x-1/2 after:-translate-y-1/2 after:content-['']";

interface ToastItem {
  id: number;
  type: ToastType;
  message: string;
}

interface ToastContextValue {
  show: (message: string, type?: ToastType) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

let nextId = 1;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((t) => t.id !== id));
  }, []);

  const show = useCallback(
    (message: string, type: ToastType = "success") => {
      const id = nextId++;
      setToasts((current) => [...current, { id, type, message }]);
      // Auto-cierre a los 5s (no se usa Date; setTimeout es válido en cliente).
      setTimeout(() => dismiss(id), 5000);
    },
    [dismiss],
  );

  const value = useMemo(() => ({ show }), [show]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      {/* Sobre la safe-area de iOS; bajo 1024 se sube por encima del lanzador del dock móvil (bottom-5) para no taparlo. */}
      <div
        className="fixed bottom-[calc(1rem+env(safe-area-inset-bottom))] right-[calc(1rem+env(safe-area-inset-right))] z-[200] max-w-[calc(100vw-2rem)] flex flex-col gap-2 max-lg:bottom-[calc(5.5rem+env(safe-area-inset-bottom))]"
        aria-live="polite"
        aria-atomic="true"
        role="status"
      >
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className="flex items-start gap-2 rounded-xl border bg-card px-4 py-3 text-xs shadow-[var(--kit-sombra)]"
            style={{ borderColor: COLOR_TOAST[toast.type], minWidth: 260 }}
          >
            {toast.type === "success" ? (
              <IconoAviso tipo="ok" className="h-4 w-4 shrink-0" />
            ) : toast.type === "warning" ? (
              <IconoAviso tipo="alerta" className="h-4 w-4 shrink-0" />
            ) : (
              <IconoAviso tipo="error" className="h-4 w-4 shrink-0" />
            )}
            <p className="flex-1 font-medium">{toast.message}</p>
            {/* Área táctil de 40×40 con un ::after centrado: el botón, el ícono y su anillo de foco miden lo mismo que antes. */}
            <button type="button" aria-label="Cerrar notificación" onClick={() => dismiss(toast.id)} className={CERRAR_TOAST_CLS}>
              <Icono nombre="cerrar" className="h-3.5 w-3.5 opacity-60" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (!ctx) {
    throw new Error("useToast debe usarse dentro de <ToastProvider>.");
  }
  return ctx;
}
