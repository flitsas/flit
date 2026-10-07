"use client";

import { useId, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { AlertCircle, CheckCircle2, Check, ChevronDown, Info, Lock, MinusCircle } from "lucide-react";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";

// Piezas visuales compartidas por las pestañas de configuración de la compañía (Trámites, Configuración
// Empresa). Todo deriva de los tokens de globals.css: fondo de tarjeta blanco, borde #DFE5ED, sombra de card,
// radio 18px en contenedores y 14px en tarjetas internas, navy #162744 para títulos y gris #59677D para ayuda.

/** Superficie de contenedor (tarjeta blanca con borde y sombra de card). */
export const CARD_SURFACE =
  "rounded-[18px] border border-[#DFE5ED] bg-white shadow-[0_8px_24px_rgba(22,39,68,0.08)] dark:border-white/10 dark:bg-[#0B0F14]";

/** Texto de ayuda (gris del token). */
export const HELP_TEXT = "text-xs text-[#59677D] dark:text-white/70";

/** Foco visible común. */
export const FOCUS_RING =
  "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]";

/** Input/select FLIT único: 48px de alto, radio 10px, label real arriba (lo pone el llamador). */
export const FIELD_CLASS =
  "h-12 w-full rounded-[10px] border bg-white px-3 text-xs text-[#162744] focus-visible:outline focus-visible:outline-2 focus-visible:outline-[#557EFF] disabled:opacity-60 dark:bg-[#0B0F14] dark:text-white";

/** Etiqueta de campo (por debajo del título de bloque). */
export const FIELD_LABEL = "mb-1.5 block text-xs font-semibold text-[#162744] dark:text-white";

export type ChipTone = StatusTone;

/** Chip de estado tintado con icono + texto (nunca solo color). */
export function StateChip({ text, tone }: { text: string; tone: ChipTone }) {
  const Icon = tone === "success" ? CheckCircle2 : tone === "warning" || tone === "danger" ? AlertCircle : MinusCircle;
  return (
    <StatusBadge
      tone={tone}
      ariaLabel={`Estado: ${text}`}
      label={
        <span className="inline-flex items-center gap-1">
          <Icon className="h-3.5 w-3.5" aria-hidden />
          {text}
        </span>
      }
    />
  );
}

/** Pista corta con icono info (consecuencia o valor por defecto de una opción). */
export function Hint({ children }: { children: ReactNode }) {
  return (
    <p className={`flex items-start gap-1.5 ${HELP_TEXT}`}>
      <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden />
      <span>{children}</span>
    </p>
  );
}

const CLAMP_THRESHOLD = 110;

/**
 * Texto de ayuda a dos líneas con «Ver más». El texto completo queda SIEMPRE en el DOM (solo se recorta
 * visualmente), así no se pierde información normativa ni lectores de pantalla.
 */
export function ClampedText({ text, className = "" }: { text: string; className?: string }) {
  const [expanded, setExpanded] = useState(false);
  // «Ver más» solo si el texto realmente se corta a 2 líneas con el ancho actual. Sin layout (SSR, jsdom)
  // se asume recorte para textos largos, así el control nunca desaparece por no poder medir.
  const [overflows, setOverflows] = useState(true);
  const long = text.length > CLAMP_THRESHOLD;
  const id = useId();
  const ref = useRef<HTMLParagraphElement>(null);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el || !long || expanded) return;
    const measure = () => {
      if (el.clientHeight > 0) setOverflows(el.scrollHeight > el.clientHeight + 1);
    };
    measure();
    if (typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(measure);
    observer.observe(el);
    return () => observer.disconnect();
  }, [long, expanded, text]);

  return (
    <div className={className}>
      <p ref={ref} id={id} className={`${HELP_TEXT} max-w-2xl ${long && !expanded ? "line-clamp-2" : ""}`}>
        {text}
      </p>
      {long && (expanded || overflows) && (
        <button
          type="button"
          aria-expanded={expanded}
          aria-controls={id}
          onClick={() => setExpanded((v) => !v)}
          className={`mt-0.5 rounded text-xs font-semibold text-[#557EFF] hover:underline ${FOCUS_RING}`}
        >
          {expanded ? "Ver menos" : "Ver más"}
        </button>
      )}
    </div>
  );
}

/** Tarjeta de bloque: título claro + una línea de ayuda + contenido (gap-4 dentro). */
export function ConfigCard({
  id,
  title,
  description,
  ariaLabel,
  children,
}: {
  id: string;
  /** Si se omite, la tarjeta se nombra con `ariaLabel` (p. ej. cuando el contenido trae su propio título). */
  title?: string;
  description?: string;
  ariaLabel?: string;
  children: ReactNode;
}) {
  const titleId = `${id}-title`;
  return (
    <section
      id={id}
      tabIndex={-1}
      aria-labelledby={title ? titleId : undefined}
      aria-label={title ? undefined : ariaLabel}
      className={`scroll-mt-24 p-6 ${CARD_SURFACE} focus-visible:outline focus-visible:outline-2 focus-visible:outline-[#557EFF]`}
    >
      {title && (
        <header className="mb-4">
          <h3 id={titleId} className="text-base font-semibold text-[#162744] dark:text-white">
            {title}
          </h3>
          {description && <ClampedText text={description} className="mt-1" />}
        </header>
      )}
      <div className="space-y-4">{children}</div>
    </section>
  );
}

/** Sub-bloque dentro de una tarjeta (título de grupo más discreto que el de la tarjeta). */
export function SubBlock({
  title,
  description,
  separated = false,
  children,
}: {
  title: string;
  description?: string;
  /** Línea superior que lo separa del bloque anterior. */
  separated?: boolean;
  children: ReactNode;
}) {
  const id = useId();
  return (
    <div
      role="group"
      aria-labelledby={id}
      className={`space-y-3 ${separated ? "border-t border-[#DFE5ED] pt-4 dark:border-white/10" : ""}`}
    >
      <div>
        <h4 id={id} className="text-sm font-semibold text-[#162744] dark:text-white">
          {title}
        </h4>
        {description && <ClampedText text={description} className="mt-0.5" />}
      </div>
      {children}
    </div>
  );
}

/**
 * Acordeón FLIT: cabecera con chevron + título + subtítulo y, a la derecha, un chip-resumen. Lo comparten las
 * familias de Trámites, «Improntas» y las «Opciones avanzadas» para que se vean como una misma familia.
 */
export function Accordion({
  id,
  title,
  subtitle,
  badge,
  open,
  onToggle,
  keepMounted = false,
  flat = false,
  bodyClassName = "grid gap-4 lg:grid-cols-2 lg:[&>*:last-child:nth-child(odd)]:col-span-2",
  dataFamily,
  children,
}: {
  id?: string;
  title: string;
  subtitle?: string;
  badge?: ReactNode;
  open: boolean;
  onToggle: () => void;
  /** Mantiene el contenido montado (oculto) al plegar: conserva valores y permite validar campos plegados. */
  keepMounted?: boolean;
  /** Variante sin sombra para anidar dentro de otra tarjeta. */
  flat?: boolean;
  bodyClassName?: string;
  dataFamily?: string;
  children: ReactNode;
}) {
  const panelId = useId();
  const headerId = useId();
  const summaryId = useId();
  return (
    <section id={id} aria-labelledby={headerId} className={`overflow-hidden ${flat ? "rounded-[14px] border border-[#DFE5ED] dark:border-white/10" : CARD_SURFACE}`}>
      <div className="flex items-center gap-3 pr-4 transition hover:bg-[rgba(85,126,255,0.04)]">
        <button
          type="button"
          id={headerId}
          aria-expanded={open}
          aria-controls={panelId}
          aria-describedby={badge ? summaryId : undefined}
          onClick={onToggle}
          className="flex min-h-[56px] min-w-0 flex-1 items-center gap-3 px-5 py-3 text-left focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-[#557EFF]"
        >
          <ChevronDown
            className="h-4 w-4 shrink-0 text-[#162744] transition-transform dark:text-white"
            style={{ transform: open ? "rotate(0deg)" : "rotate(-90deg)" }}
            aria-hidden
          />
          <span className="min-w-0">
            <span className={`block font-semibold text-[#162744] dark:text-white ${flat ? "text-sm" : "text-base"}`}>{title}</span>
            {subtitle && <span className={`mt-0.5 block ${HELP_TEXT}`}>{subtitle}</span>}
          </span>
        </button>
        {badge && (
          <span id={summaryId} className="shrink-0">
            {badge}
          </span>
        )}
      </div>
      {(open || keepMounted) && (
        <div
          id={panelId}
          role="region"
          aria-labelledby={headerId}
          data-family={dataFamily}
          hidden={!open}
          className={`border-t border-[#DFE5ED] px-5 py-4 dark:border-white/10 ${open ? bodyClassName : ""}`}
        >
          {children}
        </div>
      )}
    </section>
  );
}

/**
 * Tarjeta de opción (patrón SelectCard del proyecto): seleccionada en verde tintado, no seleccionada blanca
 * con borde, marca estilizada (círculo/casilla con check). El control nativo queda `sr-only` para conservar
 * rol (`radio`/`checkbox`), `aria-checked`, teclado y foco. `locked` = marcada y bloqueada con «Obligatorio».
 */
export function OptionCard({
  type,
  name,
  label,
  description,
  checked,
  disabled = false,
  locked = false,
  onChange,
}: {
  type: "radio" | "checkbox";
  name?: string;
  label: string;
  description?: string;
  checked: boolean;
  disabled?: boolean;
  locked?: boolean;
  onChange: (checked: boolean) => void;
}) {
  const inactive = disabled && !locked;
  const mark = type === "radio" ? "rounded-full" : "rounded-md";
  return (
    <label
      className={`flex min-h-[48px] items-start gap-3 rounded-[14px] border px-4 py-3 transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-[#557EFF] has-[:focus-visible]:ring-offset-2 ${
        inactive ? "cursor-not-allowed opacity-60" : locked ? "cursor-not-allowed" : "cursor-pointer hover:bg-[rgba(85,126,255,0.04)]"
      }`}
      style={checked ? { borderColor: "#8CC63F", background: "rgba(140,198,63,0.12)" } : { borderColor: "#DFE5ED", background: "#FFFFFF" }}
    >
      <input
        type={type}
        name={name}
        checked={checked}
        disabled={disabled || locked}
        aria-checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        className="sr-only"
      />
      <span
        aria-hidden
        className={`mt-0.5 grid h-5 w-5 shrink-0 place-items-center border-2 ${mark}`}
        style={checked ? { borderColor: "#8CC63F", background: "#8CC63F", color: "#FFFFFF" } : { borderColor: "#59677D", background: "#FFFFFF" }}
      >
        {checked && <Check className="h-3 w-3" strokeWidth={3} />}
      </span>
      <span className="min-w-0 flex-1">
        <span
          className="block text-sm font-semibold text-[#162744]"
          style={checked ? { color: "var(--flit-success-ink, #162744)" } : undefined}
        >
          {label}
        </span>
        {description && <span className={`mt-0.5 block ${HELP_TEXT}`}>{description}</span>}
      </span>
      {locked && (
        <span className="inline-flex shrink-0 items-center gap-1 text-xs font-semibold text-[#162744]">
          <Lock className="h-3.5 w-3.5" aria-hidden />
          Obligatorio
        </span>
      )}
    </label>
  );
}
