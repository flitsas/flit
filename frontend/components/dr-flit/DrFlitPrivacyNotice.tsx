"use client";

import { ShieldCheck } from "lucide-react";

/**
 * HU #12931 — aviso de tratamiento de datos (Habeas Data) al abrir DR. FLIT. No bloquea: el chat se
 * usa con normalidad sin tocarlo (AC2), y no es un consentimiento auditable (decisión de alcance,
 * ADR-0060 §10; si Legal lo exige, se reutiliza el patrón de `terms-acceptances`). Se deja de mostrar
 * al pulsar «Entendido» o al escribir el primer mensaje, y no se repite en la misma sesión (AC3).
 */
export function DrFlitPrivacyNotice({ onDismiss }: { onDismiss: () => void }) {
  return (
    <section
      aria-label="Tratamiento de datos"
      className="rounded-[var(--dr-flit-radius-card)] border p-3"
      style={{
        borderColor: "var(--dr-flit-border)",
        background: "var(--dr-flit-card-bg)",
        boxShadow: "var(--dr-flit-shadow-card)",
      }}
    >
      <div className="flex items-start gap-2.5">
        <span
          className="grid h-8 w-8 shrink-0 place-items-center rounded-full"
          style={{ background: "var(--dr-flit-icon-tint)" }}
          aria-hidden="true"
        >
          <ShieldCheck className="h-4 w-4" style={{ color: "var(--dr-flit-brand-blue)" }} />
        </span>
        <div className="min-w-0 flex-1 space-y-1.5 text-xs leading-relaxed" style={{ color: "var(--dr-flit-text-secondary)" }}>
          <p className="text-sm font-semibold" style={{ color: "var(--dr-flit-text)" }}>
            Cómo tratamos tus datos
          </p>
          <p>
            DR. FLIT responde con ayuda de un asistente de inteligencia artificial que usa el manual de FLIT.
            Tus mensajes se procesan solo para responderte; evita escribir datos personales en el chat.
          </p>
          <p>
            Si radicas un caso de soporte, tu nombre, correo y teléfono se envían solo al equipo de soporte de
            FLIT, y únicamente cuando confirmas el caso. Tratamiento conforme a la Ley 1581 de 2012.
          </p>
          <button
            type="button"
            onClick={onDismiss}
            className="mt-1 rounded-full border px-3 py-1.5 text-xs font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
            style={{ borderColor: "var(--dr-flit-brand)", color: "var(--dr-flit-brand)", background: "var(--dr-flit-card-bg)" }}
          >
            Entendido
          </button>
        </div>
      </div>
    </section>
  );
}
