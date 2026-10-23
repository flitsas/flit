"use client";

import { ExternalLink, Mail, Phone } from "lucide-react";
import {
  DR_FLIT_SUPPORT_CASE_URL,
  DR_FLIT_SUPPORT_EMAIL,
  DR_FLIT_SUPPORT_PHONE,
} from "./dr-flit-intents";

export function DrFlitSupportPanel({
  onOpenCase,
  onOpenCaseForm,
}: {
  /** Abre el formulario web oficial (pestaña nueva). Se conserva como alternativa visible. */
  onOpenCase: (href: string) => void;
  /** HU #12930 — abre el formulario del caso dentro del chat. Sin él, el botón va al formulario web. */
  onOpenCaseForm?: () => void;
}) {
  return (
    <div
      className="rounded-[var(--dr-flit-radius-card)] border p-4 space-y-4"
      style={{
        borderColor: "var(--dr-flit-border)",
        background: "var(--dr-flit-card-bg)",
        boxShadow: "var(--dr-flit-shadow-card)",
      }}
      aria-label="Canales de soporte"
    >
      <div>
        <p
          className="text-[11px] font-semibold uppercase tracking-[0.14em]"
          style={{ color: "var(--dr-flit-text-muted)" }}
        >
          Canales de comunicación
        </p>
        <ul className="mt-3 space-y-3 list-none m-0 p-0">
          <li>
            <a
              href={`mailto:${DR_FLIT_SUPPORT_EMAIL}`}
              className="flex items-center gap-3 rounded-lg px-2 py-1.5 transition-colors hover:bg-[var(--dr-flit-bubble)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)]"
            >
              <Mail
                className="h-4 w-4 shrink-0"
                style={{ color: "var(--dr-flit-brand-blue)" }}
                aria-hidden
              />
              <span className="min-w-0">
                <span
                  className="block text-xs font-medium"
                  style={{ color: "var(--dr-flit-text-muted)" }}
                >
                  Correo
                </span>
                <span
                  className="block text-sm font-semibold"
                  style={{ color: "var(--dr-flit-text)" }}
                >
                  {DR_FLIT_SUPPORT_EMAIL}
                </span>
              </span>
            </a>
          </li>
          {DR_FLIT_SUPPORT_PHONE ? (
            <li>
              <a
                href={`tel:${DR_FLIT_SUPPORT_PHONE.replace(/\s/g, "")}`}
                className="flex items-center gap-3 rounded-lg px-2 py-1.5 transition-colors hover:bg-[var(--dr-flit-bubble)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)]"
              >
                <Phone
                  className="h-4 w-4 shrink-0"
                  style={{ color: "var(--dr-flit-brand-blue)" }}
                  aria-hidden
                />
                <span className="min-w-0">
                  <span
                    className="block text-xs font-medium"
                    style={{ color: "var(--dr-flit-text-muted)" }}
                  >
                    Línea de atención
                  </span>
                  <span
                    className="block text-sm font-semibold"
                    style={{ color: "var(--dr-flit-text)" }}
                  >
                    {DR_FLIT_SUPPORT_PHONE}
                  </span>
                </span>
              </a>
            </li>
          ) : null}
        </ul>
      </div>

      <div
        className="border-t pt-4"
        style={{ borderColor: "var(--dr-flit-border)" }}
      >
        <p
          className="text-[11px] font-semibold uppercase tracking-[0.14em] mb-3"
          style={{ color: "var(--dr-flit-text-muted)" }}
        >
          Radicar caso
        </p>
        {onOpenCaseForm ? (
          <>
            <button
              type="button"
              onClick={onOpenCaseForm}
              className="flex w-full items-center justify-center gap-2 rounded-full px-4 py-3 text-sm font-semibold text-white transition-opacity hover:opacity-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
              style={{ background: "var(--dr-flit-gradient-primary)" }}
            >
              Generar un caso de soporte
            </button>
            <button
              type="button"
              onClick={() => onOpenCase(DR_FLIT_SUPPORT_CASE_URL)}
              className="mt-2 inline-flex w-full items-center justify-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)]"
              style={{ color: "var(--dr-flit-brand-title)" }}
            >
              O usa el formulario web de FLIT SAS
              <ExternalLink className="h-3.5 w-3.5" aria-hidden />
            </button>
          </>
        ) : (
          <>
            <button
              type="button"
              onClick={() => onOpenCase(DR_FLIT_SUPPORT_CASE_URL)}
              className="flex w-full items-center justify-center gap-2 rounded-full px-4 py-3 text-sm font-semibold text-white transition-opacity hover:opacity-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
              style={{ background: "var(--dr-flit-gradient-primary)" }}
            >
              Generar un caso de soporte
              <ExternalLink className="h-4 w-4" aria-hidden />
            </button>
            <p
              className="mt-2 text-center text-[11px]"
              style={{ color: "var(--dr-flit-text-muted)" }}
            >
              Te llevaremos al formulario oficial de FLIT SAS
            </p>
          </>
        )}
      </div>
    </div>
  );
}
