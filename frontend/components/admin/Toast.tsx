"use client";

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { Icono } from "@flit/ui/Icono";

// Toast mínimo sin dependencias externas (HU #10194). Éxito #00DBD5, error #FF4E00.
// Región aria-live para accesibilidad (WCAG 2.1 AA).
export type ToastType = "success" | "error";

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
      <div
        className="fixed right-4 top-[84px] z-[100] flex flex-col gap-2 max-[640px]:top-[72px]"
        aria-live="polite"
        aria-atomic="true"
        role="status"
      >
        {toasts.map((toast) => (
          <div
            key={toast.id}
            // Aviso del Manual UI Kit 2026 (`.aviso`): borde de 1 px y relleno al 10 % del color del tono, disco del
            // paquete a la izquierda, sombra del kit; aparece arriba a la derecha, bajo la barra.
            className="flex max-w-[min(380px,calc(100vw-32px))] items-center gap-2.5 rounded-[9px] border py-2.5 pl-3 pr-4 text-sm text-[color:var(--di-text-primary)] shadow-[var(--kit-sombra)]"
            style={{
              borderColor: toast.type === "success" ? "var(--kit-verde)" : "var(--kit-rojo)",
              background: `color-mix(in srgb, ${toast.type === "success" ? "var(--kit-verde)" : "var(--kit-rojo)"} 10%, var(--di-surface-elevated))`,
              minWidth: 260,
            }}
          >
            {/* eslint-disable-next-line @next/next/no-img-element -- disco estático del kit */}
            <img src={toast.type === "success" ? "/assets/kit/aviso-ok.svg" : "/assets/kit/aviso-error.svg"} alt="" aria-hidden="true" className="h-[30px] w-[30px] shrink-0" />
            <p className="flex-1 font-normal">{toast.message}</p>
            <button type="button" aria-label="Cerrar notificación" onClick={() => dismiss(toast.id)}>
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
