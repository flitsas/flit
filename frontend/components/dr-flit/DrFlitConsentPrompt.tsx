"use client";

import { AlertCircle, ShieldCheck } from "lucide-react";

/**
 * HU #12931 — autorización del tratamiento de datos antes de usar el chat con IA o radicar un caso. Es
 * obligatoria para esas dos acciones (se envían datos a terceros), no para el menú sin IA. «Acepto»
 * registra la aceptación en el backend (usuario, versión, fecha, IP) y continúa lo que se había pedido;
 * «Ahora no» vuelve al menú. El texto debe revisarlo Legal; su versión la fija el backend.
 */
export function DrFlitConsentPrompt({
  accepting,
  error,
  onAccept,
  onDecline,
}: {
  accepting: boolean;
  error: string | null;
  onAccept: () => void;
  onDecline: () => void;
}) {
  return (
    <section
      aria-label="Autorización de tratamiento de datos"
      className="space-y-3 rounded-[var(--dr-flit-radius-card)] border p-4"
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
            Antes de continuar, autoriza el tratamiento de tus datos
          </p>
          <p>
            Para responderte, DR. FLIT envía tus mensajes a un asistente de inteligencia artificial de un proveedor
            externo, que usa el manual de FLIT. Evita escribir datos personales en el chat.
          </p>
          <p>
            Si radicas un caso de soporte, tu nombre, correo, teléfono, la descripción y los adjuntos se envían al
            equipo de soporte de FLIT a través de su herramienta de gestión de casos.
          </p>
          <p>
            Tratamos estos datos conforme a la Ley 1581 de 2012. Sin tu autorización puedes seguir usando la
            búsqueda de Gestión, «Necesito ayuda» y «Normativa».
          </p>
        </div>
      </div>

      {error ? (
        <p role="alert" className="flex items-center gap-2 text-xs" style={{ color: "var(--dr-flit-text)" }}>
          <AlertCircle className="h-4 w-4 shrink-0" style={{ color: "var(--dr-flit-accent)" }} aria-hidden="true" />
          {error}
        </p>
      ) : null}

      <div className="flex flex-col gap-2">
        <button
          type="button"
          onClick={onAccept}
          disabled={accepting}
          className="w-full rounded-full px-4 py-3 text-sm font-semibold text-white transition-opacity hover:opacity-95 disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
          style={{ background: "var(--dr-flit-gradient-primary)" }}
        >
          {accepting ? "Registrando…" : "Acepto"}
        </button>
        <button
          type="button"
          onClick={onDecline}
          disabled={accepting}
          className="w-full rounded-full border px-4 py-2.5 text-sm font-semibold disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
          style={{ borderColor: "var(--dr-flit-brand)", color: "var(--dr-flit-brand)", background: "var(--dr-flit-card-bg)" }}
        >
          Ahora no
        </button>
      </div>
    </section>
  );
}
