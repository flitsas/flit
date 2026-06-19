"use client";

import { ToggleSwitch } from "../ToggleSwitch";
import type { SettingsForm } from "../settingsForm";

// Pestaña Matrícula Inicial (HU #10194, AC2 / RF07).
export interface MatriculaInicialTabProps {
  form: SettingsForm;
  onChange: (patch: Partial<SettingsForm>) => void;
  fieldErrors?: Record<string, string>;
}

export function MatriculaInicialTab({ form, onChange, fieldErrors }: MatriculaInicialTabProps) {
  return (
    <div className="space-y-3">
      <ToggleSwitch
        id="allowInitialRegistration"
        label="Permitir matrícula inicial"
        description="Habilita el registro de vehículos nuevos por primera vez."
        checked={form.allowInitialRegistration}
        onChange={(v) => onChange({ allowInitialRegistration: v })}
      />
      <ToggleSwitch
        id="allowMiscNewVehicles"
        label="Permitir vehículos nuevos misceláneos"
        description="Habilita matrícula de categorías misceláneas."
        checked={form.allowMiscNewVehicles}
        onChange={(v) => onChange({ allowMiscNewVehicles: v })}
      />
      <FieldError message={fieldErrors?.switchesMatricula} />
    </div>
  );
}

function FieldError({ message }: { message?: string }) {
  if (!message) {
    return null;
  }
  return (
    <p className="text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
      {message}
    </p>
  );
}
