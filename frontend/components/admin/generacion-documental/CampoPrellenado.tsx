"use client";

/**
 * Campo de texto con estado de hidratación — HU #12209 (Feature #12201, CF-25 / CF-22).
 *
 * <p>Tres cosas que la accesibilidad exige y que aquí no son opcionales:</p>
 * <ul>
 *   <li>El estado de hidratación <b>no se comunica solo por color</b>: lleva icono <i>y</i> texto
 *       («Dato de RUNT · bloqueado»), y el texto va enlazado al input por `aria-describedby`.</li>
 *   <li>El bloqueo se hace con `readOnly`, no con `disabled`: un `disabled` sale del orden de
 *       tabulación y el lector de pantalla deja de anunciar el valor, que es justo lo que el
 *       usuario necesita leer antes de decidir si lo libera.</li>
 *   <li>La discrepancia es un `role="status"`, no un `role="alert"`: no es un error del usuario,
 *       es una diferencia entre lo que él capturó y lo que dice la fuente.</li>
 * </ul>
 */
import { Lock, LockOpen, TriangleAlert } from "lucide-react";
import type { TransferValidationIssue } from "@/lib/api/types-generacion-documental";
import type { CampoHidratado, DiscrepanciaPrellenado } from "./prefill-hidratacion";
import { etiquetaFuente } from "./useEncadenamientoPrefill";

export const FIELD_CLASS =
  "w-full rounded-xl border px-3 py-2 text-xs focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]";

export interface CampoPrellenadoProps {
  id: string;
  label: string;
  value: string;
  onChange: (valor: string) => void;
  issues?: TransferValidationIssue[];
  hidratado?: CampoHidratado;
  discrepancia?: DiscrepanciaPrellenado;
  onLiberar?: () => void;
  onAdoptarValorFuente?: () => void;
  onDescartarDiscrepancia?: () => void;
  /** Transforma lo tecleado (p. ej. la placa a mayúsculas). */
  transformar?: (valor: string) => string;
  type?: "text" | "date";
}

export function CampoPrellenado({
  id,
  label,
  value,
  onChange,
  issues = [],
  hidratado,
  discrepancia,
  onLiberar,
  onAdoptarValorFuente,
  onDescartarDiscrepancia,
  transformar,
  type = "text",
}: CampoPrellenadoProps) {
  const bloqueado = Boolean(hidratado?.bloqueado);
  const describedBy = [
    issues.length ? `${id}-error` : null,
    hidratado ? `${id}-hidratacion` : null,
    discrepancia ? `${id}-discrepancia` : null,
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-[11px] font-medium">
        {label}
      </label>

      <input
        id={id}
        type={type}
        className={FIELD_CLASS}
        value={value}
        readOnly={bloqueado}
        aria-readonly={bloqueado || undefined}
        data-hidratado={hidratado ? "true" : undefined}
        data-bloqueado={bloqueado ? "true" : undefined}
        aria-describedby={describedBy || undefined}
        onChange={(e) => onChange(transformar ? transformar(e.target.value) : e.target.value)}
      />

      {hidratado ? (
        <p
          id={`${id}-hidratacion`}
          data-testid={`${id}-hidratacion`}
          className="mt-1 flex items-center gap-1 text-[10px]"
          style={{ color: "#5E6A7B" }}
        >
          {bloqueado ? (
            <Lock className="h-3 w-3 shrink-0" aria-hidden="true" />
          ) : (
            <LockOpen className="h-3 w-3 shrink-0" aria-hidden="true" />
          )}
          <span>
            Dato de {etiquetaFuente(hidratado.fuente)} · {bloqueado ? "bloqueado" : "editable"}
          </span>
          {bloqueado && onLiberar ? (
            <button
              type="button"
              onClick={onLiberar}
              className="ml-1 rounded px-1 font-semibold underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
              style={{ color: "#557EFF" }}
            >
              Liberar {label.toLocaleLowerCase("es-CO")}
            </button>
          ) : null}
        </p>
      ) : null}

      {discrepancia ? (
        <div
          id={`${id}-discrepancia`}
          data-testid={`${id}-discrepancia`}
          role="status"
          className="mt-1 flex flex-wrap items-center gap-1 rounded-lg border border-amber-300 bg-amber-50 px-2 py-1 text-[10px] text-amber-900"
        >
          <TriangleAlert className="h-3 w-3 shrink-0" aria-hidden="true" />
          <span>
            {etiquetaFuente(discrepancia.fuente)} reporta «{discrepancia.valorFuente}». Se conservó
            el valor que escribiste.
          </span>
          {onAdoptarValorFuente ? (
            <button
              type="button"
              onClick={onAdoptarValorFuente}
              className="rounded px-1 font-semibold underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
            >
              Usar el de {etiquetaFuente(discrepancia.fuente)}
            </button>
          ) : null}
          {onDescartarDiscrepancia ? (
            <button
              type="button"
              onClick={onDescartarDiscrepancia}
              className="rounded px-1 font-semibold underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
            >
              Mantener el mío
            </button>
          ) : null}
        </div>
      ) : null}

      {issues.map((issue) => (
        <p
          key={`${issue.code}-${issue.field}`}
          id={`${id}-error`}
          role="alert"
          className="mt-1 text-[11px] text-red-700"
        >
          <span className="font-semibold">{issue.code}</span> — {issue.message}
        </p>
      ))}
    </div>
  );
}
