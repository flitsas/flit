import { AlertTriangle } from "lucide-react";
import type { ReactNode } from "react";
import { boton } from "@/components/landing/estilos";
import type { TonoBadge } from "@/lib/tramite/tonos";

// Piezas locales del wizard del diseño de referencia (transfer-step-trail, tramite-traspaso.tsx): mismas clases, sin cambios
// visuales. Solo se les agregan atributos de accesibilidad (ids, aria-*, role) que no alteran lo que se ve.

export function Card({ title, children, id, labelledBy }: { title?: string; children: ReactNode; id?: string; labelledBy?: string }) {
  const idTitulo = id && title ? `${id}-titulo` : undefined;
  return (
    <section id={id} aria-labelledby={labelledBy ?? idTitulo} className="rounded-[9px] border border-border bg-card p-6">
      {title && (
        <h3 id={idTitulo} className="wizard-section-title mb-4 text-2xl">
          {title}
        </h3>
      )}
      {children}
    </section>
  );
}

export function Field({ label, error, children }: { label: string; error?: string | undefined; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-sm font-medium">{label}</span>
      {children}
      {error && (
        <span role="alert" className="mt-1 flex items-center gap-1 text-xs text-alert">
          <AlertTriangle aria-hidden className="h-3.5 w-3.5" />
          {error}
        </span>
      )}
    </label>
  );
}

const TONOS: Record<TonoBadge, string> = {
  success: "bg-success text-white",
  warning: "bg-warning/10 text-warning",
  info: "bg-primary/10 text-primary",
};

export function Badge({ tone, children }: { tone: TonoBadge; children: ReactNode }) {
  return <span className={`badge-arrow inline-block text-xs font-semibold ${TONOS[tone]}`}>{children}</span>;
}

/** Fila del diagnóstico: se lee «nombre: estado». */
export function Row({ k, v }: { k: string; v: ReactNode }) {
  return (
    <li className="wizard-diag-row grid min-w-0 gap-2">
      <span>{k}</span>
      <span className="sr-only">: </span>
      {v}
    </li>
  );
}

export function Btn({ children, onClick, tone = "primary", disabled, describedBy, type = "button" }: { children: ReactNode; onClick?: (() => void) | undefined; tone?: "primary" | "secondary" | "alert" | "positive"; disabled?: boolean; describedBy?: string; type?: "button" | "submit" }) {
  const variante = tone === "primary" ? "flit" : tone === "secondary" ? "back" : tone;
  return (
    <button type={type} className={boton(variante, "min-h-11 px-6", "none")} onClick={onClick} disabled={disabled} aria-describedby={describedBy}>
      {children}
    </button>
  );
}

export function Nav({ onBack, onNext, nextLabel = "Continuar", positive = false, nextDisabled = false, motivo, idMotivo, ocupado = false }: { onBack: () => void; onNext: () => void; nextLabel?: ReactNode; positive?: boolean; nextDisabled?: boolean; motivo?: string; idMotivo?: string; ocupado?: boolean }) {
  return (
    <div className="flex flex-wrap items-center justify-end gap-3">
      {motivo && (
        <p id={idMotivo} className="mr-auto text-xs font-light text-muted-foreground">
          {motivo}
        </p>
      )}
      <Btn tone="secondary" onClick={onBack} disabled={ocupado}>Atrás</Btn>
      <Btn tone={positive ? "positive" : "primary"} onClick={onNext} disabled={nextDisabled || ocupado} describedBy={nextDisabled && motivo ? idMotivo : undefined}>
        {nextLabel}
      </Btn>
    </div>
  );
}
