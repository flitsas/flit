"use client";

import type { ReactNode } from "react";
import { ToggleSwitch } from "../ToggleSwitch";
import type { SettingsForm } from "../settingsForm";

// Pestaña Traspasos (HU #10194, AC2/AC3 / RF08). Switch onlyOwnVehicles +
// gestión de lista blanca (inyectada como slot — endpoint propio, no entra en el
// PUT settings).
export interface TraspasosTabProps {
  form: SettingsForm;
  onChange: (patch: Partial<SettingsForm>) => void;
  whitelistSlot?: ReactNode;
}

export function TraspasosTab({ form, onChange, whitelistSlot }: TraspasosTabProps) {
  return (
    <div className="space-y-4">
      <ToggleSwitch
        id="onlyOwnVehicles"
        label="Solo vehículos propios"
        description="Restringe los traspasos a vehículos que ya figuran como propiedad de esta compañía. Los correos registrados en la lista blanca quedan exentos y pueden tramitar traspasos de otros vehículos."
        checked={form.onlyOwnVehicles}
        onChange={(v) => onChange({ onlyOwnVehicles: v })}
      />
      {whitelistSlot && (
        <div className="rounded-2xl border p-4" style={{ borderColor: "#DFE5ED" }}>
          {whitelistSlot}
        </div>
      )}
    </div>
  );
}
