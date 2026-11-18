import { useId, type ReactNode } from "react";
import { Icono, type NombreIcono } from "@flit/ui/Icono";

export interface PanelInicioProps {
  titulo: ReactNode;
  /** Línea bajo el título («Pulsa cualquier estado para filtrar la bandeja»). */
  subtitulo?: ReactNode;
  /** Glifo del kit junto al título. */
  icono?: NombreIcono;
  /** Lo que va a la derecha de la cabecera: un enlace «Ver todos», un selector, pestañas, la leyenda. */
  accion?: ReactNode;
  children: ReactNode;
  className?: string;
  "data-testid"?: string;
}

/**
 * Tarjeta de sección del Inicio (rediseño de los 3 dashboards). Es la misma superficie que ya usaban «Distribución
 * General» y «Seguimiento operativo» —borde --di-border, radio 9 px, fondo de tarjeta— con una cabecera de título,
 * subtítulo y acción. La sección queda nombrada por su título (`aria-labelledby`).
 */
export function PanelInicio({ titulo, subtitulo, icono, accion, children, className = "", "data-testid": testId }: PanelInicioProps) {
  const idTitulo = useId();
  return (
    <section
      aria-labelledby={idTitulo}
      data-testid={testId}
      className={`flex min-w-0 flex-col rounded-[9px] border border-[color:var(--di-border)] bg-card px-6 pb-6 pt-4 max-sm:p-4 ${className}`}
    >
      <header className="mb-4 flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
        <div className="min-w-0">
          <h2
            id={idTitulo}
            className="flex items-center gap-2 text-xl font-medium leading-tight text-[color:var(--kit-azul-texto)]"
          >
            {icono && <Icono nombre={icono} size={22} className="shrink-0" />}
            <span className="min-w-0">{titulo}</span>
          </h2>
          {subtitulo && (
            <p className="mt-0.5 text-[0.8125rem] text-[color:var(--di-text-tertiary)]">{subtitulo}</p>
          )}
        </div>
        {accion && <div className="flex shrink-0 flex-wrap items-center gap-2">{accion}</div>}
      </header>
      {children}
    </section>
  );
}
