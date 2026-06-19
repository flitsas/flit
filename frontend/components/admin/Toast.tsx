"use client";

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { CheckCircle2, X, XCircle } from "lucide-react";

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
        className="fixed bottom-4 right-4 z-[100] flex flex-col gap-2"
        aria-live="polite"
        aria-atomic="true"
        role="status"
      >
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className="flex items-start gap-2 rounded-xl border bg-white px-4 py-3 text-xs shadow-lg dark:bg-[#0B0F14]"
            style={{ borderColor: toast.type === "success" ? "#00DBD5" : "#FF4E00", minWidth: 260 }}
          >
            {toast.type === "success" ? (
              <CheckCircle2 className="h-4 w-4 shrink-0" style={{ color: "#00DBD5" }} />
            ) : (
              <XCircle className="h-4 w-4 shrink-0" style={{ color: "#FF4E00" }} />
            )}
            <p className="flex-1 font-medium">{toast.message}</p>
            <button type="button" aria-label="Cerrar notificación" onClick={() => dismiss(toast.id)}>
              <X className="h-3.5 w-3.5 opacity-60" />
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
