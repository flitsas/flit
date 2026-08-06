"use client";

// Las piezas visuales que comparten las dos consolas de consultas —la del organismo y la de la
// empresa gestora—.
//
// Salieron del kit del módulo OT cuando la consola dejó de ser suya. No es una carpeta de «cosas
// genéricas»: es lo que las dos pantallas tienen que ver IGUAL. Un panel, un botón primario o un
// aviso de error con dos estilos distintos según el módulo se lee como dos productos, y son el
// mismo.

import type { ReactNode } from "react";

/**
 * Si se ofrece la descarga en CSV, además del Excel.
 *
 * Está OCULTA, no eliminada: el generador de CSV, sus pruebas y el camino de descarga siguen
 * enteros, y volver a ofrecerla es poner esto en `true`. Se apagó porque el Excel es donde estos
 * informes acaban de verdad —fechas como fechas y números sumables—, y dos botones de descarga
 * obligan a elegir entre formatos a quien solo quería el archivo.
 *
 * El tipo es `boolean` y no el literal `false` a propósito: así apagarla no vuelve muerto, a ojos
 * del compilador, todo el código que cuelga de ella.
 */
export const CSV_EXPORT_VISIBLE: boolean = false;

export function Section({
  title,
  testId,
  hint,
  actions,
  children,
}: {
  title: string;
  testId: string;
  /** Una línea que explica qué mide la sección. Sin ella, el título tiene que cargar solo con todo. */
  hint?: string;
  actions?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section
      className="flex flex-col gap-3 rounded-xl border border-[#DFE5ED] p-4 dark:border-white/10"
      data-testid={testId}
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 className="text-sm font-semibold">{title}</h3>
          {hint && <p className="mt-0.5 text-[11px] text-[#6B7280] dark:text-white/50">{hint}</p>}
        </div>
        {actions}
      </div>
      {children}
    </section>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="text-xs text-[#6B7280] dark:text-white/50">{children}</p>;
}

export function ErrorNotice({ message }: { message: string }) {
  return (
    <p
      role="alert"
      className="rounded-xl bg-red-50 px-4 py-3 text-xs text-red-700 dark:bg-red-500/10 dark:text-red-300"
    >
      {message}
    </p>
  );
}

/** Botón primario: el degradado azul→cian es la acción principal en todo FLIT. */
export function PrimaryButton({
  children,
  onClick,
  disabled,
  type = "button",
}: {
  children: ReactNode;
  onClick?: () => void;
  disabled?: boolean;
  type?: "button" | "submit";
}) {
  return (
    <button
      type={type === "submit" ? "submit" : "button"}
      onClick={onClick}
      disabled={disabled}
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white transition disabled:opacity-60"
      style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
    >
      {children}
    </button>
  );
}

export const FIELD_CLS =
  "rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF] disabled:opacity-60";

// El formateo vive en `./format`, que no es un módulo de cliente: lo importan las definiciones de
// columnas, que son ficheros de datos y no componentes.
export { formatInt, plural } from "./format";
