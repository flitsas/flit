"use client";

// HU #10710 — parametrización de la radicación por Quipux de una secretaría del catálogo:
// código DIVIPO + las tres banderas por familia de trámite. Solo SuperAdmin (el endpoint
// exige SuperAdminPolicy). Mismo patrón que TransitOfficeStatusDialog: Modal + confirmación,
// en error muestra el mensaje y NO cierra.
//
// OJO — no confundir con el toggle «Consola en solo lectura» de TramitesSuperSection
// (`operation_mode`): aquello describe al OT CLIENTE de FLIT, esto describe a la secretaría
// DESTINO a la que FLIT radica y que normalmente ni siquiera es cliente.
import { useState } from "react";
import { Send } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { ApiValidationError } from "@/lib/api/types";
import {
  updateTransitOfficeQuipuxSettings,
  type TransitOfficeOperationalStatus,
  type TransitOfficeQuipuxSettings,
} from "@/lib/api/admin-transit-office-tenants";
import { digitsOnly } from "@/lib/format/currency";
import { OT_INPUT_CLS } from "./ot-form-styles";

export interface TransitOfficeQuipuxDialogProps {
  office: TransitOfficeOperationalStatus;
  onClose: () => void;
  onSaved: (settings: TransitOfficeQuipuxSettings) => void;
}

/** El DIVIPO es numérico y conserva ceros a la izquierda (Medellín = 05001) → se guarda como texto. */
const DIVIPO_PATTERN = /^\d{1,20}$/;

export function TransitOfficeQuipuxDialog({
  office,
  onClose,
  onSaved,
}: TransitOfficeQuipuxDialogProps) {
  const [divipoCode, setDivipoCode] = useState(office.divipoCode ?? "");
  const [matricula, setMatricula] = useState(office.quipuxRegistration);
  const [traspaso, setTraspaso] = useState(office.quipuxTransfer);
  const [otros, setOtros] = useState(office.quipuxOther);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const trimmed = divipoCode.trim();
  const algunaBandera = matricula || traspaso || otros;
  // Vacío NO es un error MIENTRAS no haya banderas activas: es «aún no se conoce», el estado
  // normal de las secretarías todavía no integradas. Solo se valida el formato cuando el
  // administrador sí escribió algo.
  const formatoInvalido = trimmed !== "" && !DIVIPO_PATTERN.test(trimmed);
  // Activar una familia sin DIVIPO es un estado inconsistente («declara pero no radica»): se
  // bloquea el guardado, igual que el backend (422 DIVIPO_REQUIRED_FOR_FLAGS).
  const sinDivipoConBanderas = trimmed === "" && algunaBandera;
  const elegible = trimmed !== "" && algunaBandera;

  async function save() {
    if (formatoInvalido || sinDivipoConBanderas) {
      return;
    }
    setError(null);
    setBusy(true);
    try {
      const settings = await updateTransitOfficeQuipuxSettings(office.id, {
        divipoCode: trimmed === "" ? null : trimmed,
        quipuxRegistration: matricula,
        quipuxTransfer: traspaso,
        quipuxOther: otros,
      });
      onSaved(settings);
    } catch (err) {
      // El 422 del backend viaja como `{ code }`, pero apiFetch lo convierte en
      // ApiValidationError sin conservarlo; el único 422 que esta pantalla puede provocar es
      // DIVIPO_CODE_INVALID (las tres banderas siempre se envían), así que el mensaje es preciso.
      if (err instanceof ApiValidationError) {
        setError("El código DIVIPO no es válido: debe ser numérico (máximo 20 dígitos).");
      } else {
        const status = (err as { status?: number }).status;
        setError(
          status === 404
            ? "La secretaría no existe o está descatalogada."
            : "No se pudo guardar la parametrización de Quipux.",
        );
      }
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      size="md"
      zClassName="z-[90]"
      icon={Send}
      title="Radicación por Quipux"
      description={office.name}
    >
      <div className="space-y-4">
        <div>
          <label htmlFor="ot-quipux-divipo" className="text-xs font-semibold">
            Código DIVIPO
          </label>
          <input
            id="ot-quipux-divipo"
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            autoComplete="off"
            value={divipoCode}
            disabled={busy}
            onChange={(e) => setDivipoCode(digitsOnly(e.target.value).slice(0, 20))}
            placeholder="Ej. 05001"
            aria-invalid={formatoInvalido || undefined}
            aria-describedby="ot-quipux-divipo-hint"
            className={`mt-1 font-mono ${OT_INPUT_CLS}`}
          />
          <p id="ot-quipux-divipo-hint" className="mt-1 text-[11px] opacity-60">
            El código que espera Quipux para esta secretaría. Es obligatorio para activar
            cualquier familia; déjalo vacío solo si la secretaría aún no se integra (sin
            banderas): es el estado normal de las secretarías todavía no integradas.
          </p>
          {formatoInvalido && (
            <p role="alert" className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }}>
              El código DIVIPO debe ser numérico (máximo 20 dígitos). Conserva los ceros a la
              izquierda.
            </p>
          )}
        </div>

        <fieldset className="space-y-2">
          <legend className="text-xs font-semibold">Trámites que se radican por Quipux</legend>
          <p className="mb-2 text-[11px] opacity-60">
            El alta es gradual: una secretaría puede estar integrada para traspasos y no para
            matrículas.
          </p>
          <ToggleSwitch
            id="ot-quipux-matricula"
            label="Matrículas"
            checked={matricula}
            disabled={busy}
            onChange={setMatricula}
          />
          <ToggleSwitch
            id="ot-quipux-traspaso"
            label="Traspasos"
            checked={traspaso}
            disabled={busy}
            onChange={setTraspaso}
          />
          <ToggleSwitch
            id="ot-quipux-otros"
            label="Otros trámites"
            checked={otros}
            disabled={busy}
            onChange={setOtros}
          />
        </fieldset>

        {/* Bloqueo: activar una familia sin DIVIPO dejaría el estado inconsistente «declara
            pero no radica». El código es obligatorio en cuanto se enciende una bandera. */}
        {sinDivipoConBanderas && (
          <p
            role="alert"
            className="rounded-xl px-3 py-2 text-[11px]"
            style={{ background: "#FFF4EC", color: "#7A2E00", border: "1px solid #FFD9C2" }}
          >
            <span className="font-semibold">Falta el código DIVIPO.</span> Para activar una
            familia primero debes cargar el código: sin él la secretaría no radica, así que no
            se puede guardar en este estado.
          </p>
        )}

        {elegible && (
          <p className="text-[11px] opacity-70">
            Esta secretaría queda lista para radicar por Quipux.
          </p>
        )}

        {error && (
          <p role="alert" className="text-sm" style={{ color: "#FF4E00" }}>
            {error}
          </p>
        )}

        <div className="flex gap-3">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={save}
            disabled={busy || formatoInvalido || sinDivipoConBanderas}
            className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            {busy ? "Guardando…" : "Guardar"}
          </button>
        </div>
      </div>
    </Modal>
  );
}
