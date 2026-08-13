'use client';

import { useId, useState, type ReactNode } from 'react';
import { ChevronDown } from 'lucide-react';

/**
 * Acordeón del wizard, en el lenguaje visual de la propuesta: tarjeta blanca de radio amplio,
 * cabecera con el título en azul de marca, badge opcional a la derecha y chevron que rota.
 *
 * Existe porque cada panel del wizard traía su propio colapsable —desde un ChevronRight rotado
 * hasta flechas de texto `▾/▸` con estilos inline— y el conjunto se leía como piezas de
 * productos distintos. Es SOLO presentación: no decide qué se muestra ni cuándo, eso sigue en
 * cada panel.
 */
export interface WizardAccordionProps {
  title: string;
  children: ReactNode;
  /** Abierto de entrada. Para paneles que el gestor consulta siempre. */
  defaultOpen?: boolean;
  /** Chip/estado a la derecha del título (p. ej. "Aprobado", "3 pendientes"). */
  badge?: ReactNode;
  /** Icono a la izquierda del título. */
  icon?: ReactNode;
  /** Nombre accesible de la región desplegada; por defecto usa `title`. */
  regionLabel?: string;
  /**
   * Modo controlado. Necesario para los paneles que cargan sus datos en diferido al abrirse
   * (p. ej. avisos de correo): el estado tiene que vivir donde está el efecto de carga, no aquí.
   * Sin `open` el acordeón se gestiona solo.
   */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  /** Se traslada al contenedor, para no perder los anclajes de prueba al adoptar el acordeón. */
  testId?: string;
  className?: string;
}

export function WizardAccordion({
  title,
  children,
  defaultOpen = false,
  badge,
  icon,
  regionLabel,
  open: openProp,
  onOpenChange,
  testId,
  className = '',
}: WizardAccordionProps) {
  const [openState, setOpenState] = useState(defaultOpen);
  const controlado = openProp !== undefined;
  const open = controlado ? openProp : openState;
  const setOpen = (next: boolean) => {
    if (!controlado) setOpenState(next);
    onOpenChange?.(next);
  };
  const panelId = useId();

  return (
    <div
      data-testid={testId}
      className={`overflow-hidden rounded-2xl border bg-white dark:bg-[#0B0F14] ${className}`}
    >
      <button
        type="button"
        onClick={() => setOpen(!open)}
        aria-expanded={open}
        aria-controls={panelId}
        className="flex w-full items-center justify-between gap-3 rounded-2xl px-4 py-3 text-left transition hover:bg-[#557EFF]/[0.04] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF]"
      >
        <span className="flex min-w-0 items-center gap-2">
          {icon}
          <span className="truncate text-xs font-semibold" style={{ color: '#557EFF' }}>
            {title}
          </span>
        </span>
        <span className="flex shrink-0 items-center gap-2">
          {badge}
          <ChevronDown
            className={`h-4 w-4 opacity-60 transition-transform ${open ? 'rotate-180' : ''}`}
            aria-hidden="true"
          />
        </span>
      </button>
      {open ? (
        <div
          id={panelId}
          role="region"
          aria-label={regionLabel ?? title}
          className="border-t px-4 pb-4 pt-3"
        >
          {children}
        </div>
      ) : null}
    </div>
  );
}
