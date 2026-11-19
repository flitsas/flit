import { StatusBadge, type StatusTone } from "@flit/ui";
import type { Etiqueta } from "@/lib/etiquetas";

const DISCO: Record<StatusTone, string> = {
  success: "bg-[var(--badge-success-fg)]",
  warning: "bg-[var(--badge-warning-fg)]",
  danger: "bg-[var(--badge-danger-fg)]",
  info: "bg-[var(--badge-info-fg)]",
  neutral: "bg-[var(--badge-neutral-fg)]",
};

/** Más largo que esto ya no es una etiqueta corta: va como texto con disco (rediseño S1, R2). */
export const MAXIMO_PILL = 22;

/**
 * Estado largo o explicativo: disco del color del tono y texto que parte línea. Nunca se sale de su caja (el pill del kit
 * no parte línea y a ~1290 px «Pendiente: la consulta automática no lo trae» desbordaba la tarjeta).
 */
export function EstadoTexto({ texto, tono, className = "" }: { texto: string; tono: StatusTone; className?: string }) {
  return (
    <span className={`inline-flex min-w-0 max-w-full items-start gap-1.5 break-words text-sm ${className}`} data-estado-texto>
      <span aria-hidden className={`mt-[0.4em] h-2 w-2 shrink-0 rounded-full ${DISCO[tono]}`} />
      <span className="min-w-0">{texto}</span>
    </span>
  );
}

/**
 * La regla de pills del detalle: un estado corto que es LA etiqueta de algo va como pill del kit; uno largo o explicativo,
 * como {@link EstadoTexto}.
 */
export function Estado({ e, title }: { e: Etiqueta; title?: string }) {
  return e.texto.length <= MAXIMO_PILL ? <StatusBadge label={e.texto} tone={e.tono} title={title} /> : <EstadoTexto texto={e.texto} tono={e.tono} />;
}
