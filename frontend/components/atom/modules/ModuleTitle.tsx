import type { ReactNode } from "react";

/**
 * Encabezado de módulo unificado (HU #10493). La caja del título es la misma tarjeta
 * blanca con borde de marca en todos los módulos. La acción primaria (botón) se
 * renderiza SIEMPRE FUERA de la caja del título (regla R7 del feedback de diseño),
 * en la misma fila y alineada a la derecha. `right` queda para contenido que sí vive
 * DENTRO de la caja (p. ej. un indicador de estado "En vivo").
 */
export function ModuleTitle({
  title,
  subtitle,
  right,
  action,
}: {
  title: string;
  subtitle?: string;
  /** Contenido dentro de la caja del título (indicadores de estado, no acciones). */
  right?: ReactNode;
  /** Acción primaria (botón) renderizada FUERA de la caja del título. */
  action?: ReactNode;
}) {
  const card = (
    <div className="rounded-2xl px-5 py-3 border border-[#DFE5ED] dark:border-white/10 bg-white dark:bg-[#0B0F14] flex items-center justify-between gap-3 flex-wrap">
      <div>
        <h1 className="text-2xl font-bold leading-tight" style={{ color: "#557eff" }}>
          {title}
        </h1>
        {subtitle && <p className="text-xs opacity-70 mt-0.5">{subtitle}</p>}
      </div>
      {right}
    </div>
  );

  // Con acción: caja del título (≈2/3) + botón FUERA, en una fila con justify-between.
  if (action) {
    return (
      <div className="flex flex-wrap items-start justify-between gap-3 shrink-0">
        <div className="min-w-0 flex-1 md:max-w-[66%]">{card}</div>
        {action}
      </div>
    );
  }

  // Sin acción: caja del título a 2/3 en una grilla de 3 columnas (comportamiento previo).
  return (
    <div className="grid grid-cols-1 gap-3 shrink-0 md:grid-cols-3">
      <div className="md:col-span-2">{card}</div>
    </div>
  );
}
