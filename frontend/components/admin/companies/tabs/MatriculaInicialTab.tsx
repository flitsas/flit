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
        description="Autoriza a esta compañía a tramitar la primera matrícula de un vehículo nuevo (0 km) ante el organismo de tránsito, es decir, asignarle placa por primera vez. Si se desactiva, la compañía no podrá radicar matrículas iniciales en la plataforma."
        checked={form.allowInitialRegistration}
        onChange={(v) => onChange({ allowInitialRegistration: v })}
      />
      <ToggleSwitch
        id="allowMiscNewVehicles"
        label="Permitir vehículos de categorías misceláneas"
        description="Extiende la matrícula inicial a vehículos nuevos de categorías especiales o «misceláneas» —como remolques, semirremolques, maquinaria agrícola o industrial, motocarros y similares—, además de los automóviles y camiones convencionales. Si se desactiva, solo se podrán matricular las categorías estándar."
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
