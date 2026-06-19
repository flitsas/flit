"use client";

import { Info } from "lucide-react";
import { NOTIFICATION_TARGET_LABELS, SMTP_LABELS, type SettingsForm } from "../settingsForm";

// Pestaña Contingencia FLIT (HU #10194, AC2). El backend no expone campos
// adicionales de contingencia; se presenta como sección informativa derivada de
// la configuración existente (sin inventar campos), siguiendo el handoff.
export interface ContingenciaFlitTabProps {
  form: SettingsForm;
}

export function ContingenciaFlitTab({ form }: ContingenciaFlitTabProps) {
  return (
    <div className="space-y-3">
      <div
        className="flex items-start gap-3 rounded-2xl border p-4"
        style={{ borderColor: "#DFE5ED", background: "rgba(0,219,213,0.06)" }}
      >
        <Info className="mt-0.5 h-4 w-4 shrink-0" style={{ color: "#557EFF" }} />
        <div className="text-xs">
          <p className="font-semibold">Plan de contingencia FLIT</p>
          <p className="mt-1 opacity-70">
            En contingencia, las notificaciones se enrutan según la configuración vigente. Estos
            valores se editan en las pestañas correspondientes y se guardan con &quot;Guardar todo&quot;.
          </p>
        </div>
      </div>

      <dl className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        <SummaryItem
          label="Enrutamiento de notificaciones"
          value={SMTP_LABELS[form.enrutamientoSMTP]}
        />
        <SummaryItem label="Baúl de firmas" value={form.baulFirmasActivo ? "Activo" : "Inactivo"} />
        <SummaryItem
          label="Métodos de recaudo"
          value={form.metodosRecaudo.length > 0 ? form.metodosRecaudo.join(", ") : "Ninguno"}
        />
        <SummaryItem
          label="Destinatario de notificaciones"
          value={NOTIFICATION_TARGET_LABELS[form.notificationTarget]}
        />
      </dl>
    </div>
  );
}

function SummaryItem({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
      <dt className="text-[10px] font-semibold uppercase opacity-60">{label}</dt>
      <dd className="mt-0.5 text-xs font-medium">{value}</dd>
    </div>
  );
}
