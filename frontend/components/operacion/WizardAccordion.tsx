'use client';

import {
  createContext,
  useContext,
  useId,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { ChevronDown } from 'lucide-react';

/**
 * Acordeón del wizard, en el lenguaje visual de la propuesta: tarjeta blanca de radio amplio,
 * cabecera con el título en azul de marca, badge opcional a la derecha y chevron que rota.
 *
 * Existe porque cada panel del wizard traía su propio colapsable —desde un ChevronRight rotado
 * hasta flechas de texto `▾/▸` con estilos inline— y el conjunto se leía como piezas de
 * productos distintos. Es SOLO presentación: no decide qué se muestra ni cuándo, eso sigue en
 * cada panel.
 *
 * **Fila sincronizada (prototipo Lovable `AccordionRow`):** varias tarjetas hermanas (p. ej.
 * Vendedor | Comprador) deben abrir/cerrar juntas. Envuélvelas en `WizardAccordionRow` —el
 * contexto comparte un solo `open`/`toggle`; cada `WizardAccordion` hijo consume ese estado.
 */

type AccordionRowCtx = { open: boolean; toggle: () => void };

const WizardAccordionRowCtx = createContext<AccordionRowCtx | null>(null);

/** Comparte un único open/toggle entre varios `WizardAccordion` (p. ej. Vendedor | Comprador). */
export function WizardAccordionRow({
  children,
  defaultOpen = false,
}: {
  children: ReactNode;
  defaultOpen?: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const value = useMemo(
    () => ({ open, toggle: () => setOpen((o) => !o) }),
    [open],
  );
  return (
    <WizardAccordionRowCtx.Provider value={value}>{children}</WizardAccordionRowCtx.Provider>
  );
}

export interface WizardAccordionProps {
  title: string;
  children: ReactNode;
  /** Abierto de entrada. Para paneles que el gestor consulta siempre. */
  defaultOpen?: boolean;
  /**
   * Texto bajo el título que permanece en la cabecera al colapsar (prototipo: hint del vendedor).
   * No va en el cuerpo — si no, desaparece al cerrar.
   */
  subtitle?: ReactNode;
  /** Chip/estado a la derecha del título (p. ej. "Aprobado", "3 pendientes"). */
  badge?: ReactNode;
  /** Icono a la izquierda del título. */
  icon?: ReactNode;
  /** Nombre accesible de la región desplegada; por defecto usa `title`. */
  regionLabel?: string;
  /**
   * Modo controlado. Necesario para los paneles que cargan sus datos en diferido al abrirse
   * (p. ej. avisos de correo): el estado tiene que vivir donde está el efecto de carga, no aquí.
   * Sin `open` el acordeón se gestiona solo (o vía `WizardAccordionRow` si hay contexto).
   */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  /** Se traslada al contenedor, para no perder los anclajes de prueba al adoptar el acordeón. */
  testId?: string;
  className?: string;
  /**
   * Nivel semántico del título (consolidación HU auditoría de diseño). `h3` por defecto —el
   * acordeón es una tarjeta más, hermana de las que usan `WizardCardHeader`—; `h4` cuando el
   * acordeón vive DENTRO de una tarjeta que ya tiene su propio `h3` (para no saltar de padre a
   * hijo con el mismo nivel).
   */
  level?: 'h3' | 'h4';
}

export function WizardAccordion({
  title,
  children,
  defaultOpen = false,
  subtitle,
  badge,
  icon,
  regionLabel,
  open: openProp,
  onOpenChange,
  testId,
  className = '',
  level = 'h3',
}: WizardAccordionProps) {
  const HeadingTag = level;
  const rowCtx = useContext(WizardAccordionRowCtx);
  const [openState, setOpenState] = useState(defaultOpen);
  const controlado = openProp !== undefined;
  // Prioridad: props controladas > fila sincronizada (AccordionRow) > estado local.
  const open = controlado ? openProp! : rowCtx ? rowCtx.open : openState;
  const setOpen = (next: boolean) => {
    if (controlado) {
      onOpenChange?.(next);
      return;
    }
    if (rowCtx) {
      if (next !== rowCtx.open) rowCtx.toggle();
      onOpenChange?.(next);
      return;
    }
    setOpenState(next);
    onOpenChange?.(next);
  };
  const panelId = useId();
  const toggle = () => setOpen(!open);

  return (
    <div
      data-testid={testId}
      // `overflow-hidden` solo cerrado: con el panel abierto recorta comboboxes absolutos
      // (p. ej. Secretaría de tránsito en radicación). Cerrado no hay hijos, el clip solo
      // redondea el hover de la cabecera.
      className={`${open ? 'overflow-visible' : 'overflow-hidden'} rounded-2xl border bg-white dark:bg-[#162744] ${className}`}
      style={{ borderColor: '#DFE5ED' }}
    >
      {/* Cabecera al estilo Lovable Card+AccordionRow:
          [ título (+ subtítulo) ] ………… [ badge ] [ chevron ]
          El badge queda FUERA del botón de título para que PN/PJ no colapse; el chevron
          es su propio control (o se puede pulsar el título). */}
      <div className="flex w-full items-start gap-3 px-4 py-3">
        <HeadingTag className="m-0 min-w-0 flex-1">
          <button
            type="button"
            onClick={toggle}
            aria-expanded={open}
            aria-controls={panelId}
            className="w-full rounded-xl text-left transition hover:bg-[#557EFF]/[0.04] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF]"
          >
            <span className="flex min-w-0 items-center gap-2">
              {icon}
              <span className="truncate text-sm font-bold" style={{ color: '#557EFF' }}>
                {title}
              </span>
            </span>
            {subtitle ? (
              <span className="mt-1 block text-xs font-normal leading-snug opacity-70">
                {subtitle}
              </span>
            ) : null}
          </button>
        </HeadingTag>
        <div className="flex shrink-0 items-center gap-2 pt-0.5">
          {badge ? <div className="shrink-0">{badge}</div> : null}
          <button
            type="button"
            onClick={toggle}
            aria-expanded={open}
            aria-controls={panelId}
            aria-label={open ? `Contraer ${title}` : `Expandir ${title}`}
            className="grid h-8 w-8 place-items-center rounded-lg transition hover:bg-[#557EFF]/[0.06] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          >
            <ChevronDown
              className={`h-4 w-4 opacity-70 transition-transform ${open ? 'rotate-180' : ''}`}
              aria-hidden="true"
            />
          </button>
        </div>
      </div>
      {open ? (
        <div
          id={panelId}
          role="region"
          aria-label={regionLabel ?? title}
          className="border-t px-4 pb-4 pt-3"
          style={{ borderColor: '#DFE5ED' }}
        >
          {children}
        </div>
      ) : null}
    </div>
  );
}
