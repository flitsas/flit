"use client";

import { useEffect, useState } from "react";
import { Loader2, MailCheck, ShieldAlert, ShieldCheck } from "lucide-react";
import { OtSidePanel } from "./OtSidePanel";
import { OT_INPUT_CLS } from "./ot-form-styles";
import { ApiValidationError } from "@/lib/api/types";
import {
  resendMandateSignerIdentity,
  sendMandateSignerIdentity,
  type MandateSigner,
  type MandateSignerInput,
  type MandateSignerSaved,
  type OtCompany,
} from "@/lib/api/admin-mandate-signers";
import { fetchOtUsers, type OtUserItem } from "@/lib/api/admin-ot-security";

export interface MandatarioFormPanelProps {
  open: boolean;
  transitOfficeId: string;
  /** Mandatario en edición, o `null` para alta. */
  editing: MandateSigner | null;
  companies: OtCompany[];
  onClose: () => void;
  onSubmit: (input: MandateSignerInput) => Promise<MandateSignerSaved>;
  onSaved: (saved: MandateSignerSaved) => void;
  onError: (message: string) => void;
}

/** Tipos de documento de una persona mandataria (el mandatario firma como persona natural). */
const DOCUMENT_TYPES: ReadonlyArray<{ value: string; label: string }> = [
  { value: "CC", label: "Cédula de ciudadanía" },
  { value: "CE", label: "Cédula de extranjería" },
  { value: "PA", label: "Pasaporte" },
  { value: "TI", label: "Tarjeta de identidad" },
];

/**
 * Formulario de mandatario (ADR-0023, ampliado por ADR-0036): nombre, tipo + número de documento,
 * correo (para la validación de identidad), cuenta de usuario de OT (cotejo del firmante al aprobar,
 * §D9), huella (readonly) y multiselect de compañías. Con la MULTIPLICIDAD (ADR-0036) las compañías
 * YA NO se deshabilitan por estar tomadas por otro mandatario; solo se deshabilitan las bloqueadas o
 * inactivas en el OT (RF33). En edición muestra el estado de identidad y permite enviar/reenviar la
 * validación por correo.
 */
export function MandatarioFormPanel({
  open,
  transitOfficeId,
  editing,
  companies,
  onClose,
  onSubmit,
  onSaved,
  onError,
}: MandatarioFormPanelProps) {
  const [fullName, setFullName] = useState("");
  const [documentType, setDocumentType] = useState("CC");
  const [documentNumber, setDocumentNumber] = useState("");
  const [email, setEmail] = useState("");
  const [userId, setUserId] = useState("");
  const [selected, setSelected] = useState<Set<string>>(() => new Set());
  const [submitting, setSubmitting] = useState(false);

  const [otUsers, setOtUsers] = useState<OtUserItem[]>([]);
  const [identityBusy, setIdentityBusy] = useState(false);
  const [identityMsg, setIdentityMsg] = useState<{ tone: "success" | "error"; text: string } | null>(null);

  useEffect(() => {
    if (!open) {
      return;
    }
    // Precarga (edición) o limpieza (alta) al abrir el panel.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sincroniza el formulario con el registro editado al abrir
    setFullName(editing?.fullName ?? "");
    setDocumentType(editing?.documentType ?? "CC");
    setDocumentNumber(editing?.documentNumber ?? "");
    setEmail(editing?.email ?? "");
    setUserId(editing?.userId ?? "");
    setSelected(new Set(editing?.companyTenantIds ?? []));
    setIdentityMsg(null);
  }, [open, editing]);

  useEffect(() => {
    if (!open) {
      return;
    }
    const controller = new AbortController();
    fetchOtUsers({ transitOfficeId }, controller.signal)
      .then((res) => setOtUsers(res.data))
      .catch(() => setOtUsers([]));
    return () => controller.abort();
  }, [open, transitOfficeId]);

  const toggleCompany = (companyTenantId: string) => {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(companyTenantId)) {
        next.delete(companyTenantId);
      } else {
        next.add(companyTenantId);
      }
      return next;
    });
  };

  const handleSubmit = async () => {
    setSubmitting(true);
    try {
      const saved = await onSubmit({
        fullName: fullName.trim(),
        documentType,
        documentNumber: documentNumber.trim(),
        email: email.trim() ? email.trim() : null,
        userId: userId ? userId : null,
        companyTenantIds: [...selected],
      });
      onSaved(saved);
    } catch (err) {
      const serverMessage =
        err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      onError(serverMessage ?? "No se pudo guardar el mandatario.");
    } finally {
      setSubmitting(false);
    }
  };

  const handleIdentity = async () => {
    if (!editing) {
      return;
    }
    setIdentityBusy(true);
    setIdentityMsg(null);
    try {
      const alreadyValidated = editing.identityValidationRef !== null;
      const result = alreadyValidated
        ? await resendMandateSignerIdentity(transitOfficeId, editing.id)
        : await sendMandateSignerIdentity(transitOfficeId, editing.id);
      setIdentityMsg({
        tone: "success",
        text: result.reused
          ? "El mandatario ya tiene una validación de identidad vigente."
          : "Validación de identidad enviada al correo del mandatario.",
      });
    } catch (err) {
      const serverMessage =
        err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      setIdentityMsg({
        tone: "error",
        text: serverMessage ?? "No se pudo enviar la validación de identidad.",
      });
    } finally {
      setIdentityBusy(false);
    }
  };

  const canSubmit =
    fullName.trim().length > 0 && documentNumber.trim().length > 0 && selected.size > 0 && !submitting;

  const validated = editing?.identityValidationRef != null;

  const footer = (
    <button
      type="button"
      disabled={!canSubmit}
      onClick={() => void handleSubmit()}
      className="flex w-full items-center justify-center gap-2 rounded-xl py-2.5 text-xs font-semibold text-white disabled:opacity-50"
      style={{ background: "#557EFF" }}
    >
      {submitting && <Loader2 className="h-4 w-4 animate-spin" />}
      {editing ? "Guardar cambios" : "Registrar mandatario"}
    </button>
  );

  return (
    <OtSidePanel
      open={open}
      title={editing ? "Editar mandatario" : "Nuevo mandatario"}
      ariaLabel={editing ? "Editar mandatario" : "Registrar mandatario"}
      onClose={onClose}
      disabled={submitting}
      footer={footer}
    >
      <div className="space-y-4">
        <div>
          <label htmlFor="ms-fullname" className="mb-1 block text-xs font-semibold">
            Nombre del mandatario
          </label>
          <input
            id="ms-fullname"
            value={fullName}
            onChange={(e) => setFullName(e.target.value)}
            className={OT_INPUT_CLS}
            placeholder="Nombre completo"
          />
        </div>

        <div className="grid grid-cols-[8rem_1fr] gap-2">
          <div>
            <label htmlFor="ms-doctype" className="mb-1 block text-xs font-semibold">
              Tipo de documento
            </label>
            <select
              id="ms-doctype"
              value={documentType}
              onChange={(e) => setDocumentType(e.target.value)}
              className={OT_INPUT_CLS}
            >
              {DOCUMENT_TYPES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.value}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="ms-document" className="mb-1 block text-xs font-semibold">
              Número de documento
            </label>
            <input
              id="ms-document"
              value={documentNumber}
              onChange={(e) => setDocumentNumber(e.target.value)}
              className={OT_INPUT_CLS}
              placeholder="Número de documento"
              inputMode="numeric"
            />
          </div>
        </div>

        <div>
          <label htmlFor="ms-email" className="mb-1 block text-xs font-semibold">
            Correo electrónico
          </label>
          <input
            id="ms-email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className={OT_INPUT_CLS}
            placeholder="correo@dominio.com"
            aria-describedby="ms-email-help"
          />
          <p id="ms-email-help" className="mt-1 text-[10px] opacity-60">
            Necesario para enviarle la validación de identidad.
          </p>
        </div>

        <div>
          <label htmlFor="ms-user" className="mb-1 block text-xs font-semibold">
            Cuenta de usuario de OT
          </label>
          <select
            id="ms-user"
            value={userId}
            onChange={(e) => setUserId(e.target.value)}
            className={OT_INPUT_CLS}
            aria-describedby="ms-user-help"
          >
            <option value="">— Sin vincular —</option>
            {otUsers.map((u) => (
              <option key={u.id} value={u.id}>
                {u.fullName} · {u.email}
              </option>
            ))}
          </select>
          <p id="ms-user-help" className="mt-1 text-[10px] opacity-60">
            Al aprobar un trámite, el mandatario cuyo usuario coincide con quien aprueba firma
            automáticamente el mandato.
          </p>
        </div>

        {editing && (
          <div className="rounded-xl border p-3" data-testid="ms-identity">
            <div className="flex items-center justify-between gap-2">
              <span
                className="inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-semibold"
                style={
                  validated
                    ? { background: "rgba(112,207,58,0.14)", color: "#3f7a15" }
                    : { background: "rgba(240,90,53,0.12)", color: "#b45309" }
                }
              >
                {validated ? <ShieldCheck className="h-3.5 w-3.5" /> : <ShieldAlert className="h-3.5 w-3.5" />}
                {validated ? "Identidad validada" : "Identidad sin validar"}
              </span>
              <button
                type="button"
                disabled={identityBusy || !editing.email}
                onClick={() => void handleIdentity()}
                className="inline-flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
                style={{ color: "#557EFF", borderColor: "#557EFF" }}
              >
                {identityBusy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <MailCheck className="h-3.5 w-3.5" />}
                {validated ? "Reenviar validación" : "Enviar validación"}
              </button>
            </div>
            {!editing.email && (
              <p className="mt-2 text-[10px] opacity-60">
                Agrega un correo y guarda para poder enviar la validación de identidad.
              </p>
            )}
            {identityMsg && (
              <p
                role="status"
                className="mt-2 text-[11px]"
                style={{ color: identityMsg.tone === "success" ? "#3f7a15" : "#c2410c" }}
              >
                {identityMsg.text}
              </p>
            )}
          </div>
        )}

        <div>
          <label htmlFor="ms-hash" className="mb-1 block text-xs font-semibold">
            Huella de integridad
          </label>
          <input
            id="ms-hash"
            readOnly
            value={editing?.integrityHash ?? ""}
            placeholder="Se generará automáticamente al guardar"
            aria-describedby="ms-hash-help"
            className={`${OT_INPUT_CLS} font-mono opacity-70`}
          />
          <p id="ms-hash-help" className="mt-1 text-[10px] opacity-60">
            SHA-256 de nombre + documento + fecha de registro. Se regenera al editar; los
            mandatos ya emitidos conservan su huella.
          </p>
        </div>

        <fieldset className="space-y-2">
          <legend className="mb-1 text-xs font-semibold">Compañías gestoras</legend>
          {companies.length === 0 ? (
            <p className="rounded-xl border p-3 text-center text-[11px] opacity-60">
              El organismo de tránsito no tiene compañías habilitadas.
            </p>
          ) : (
            <ul className="space-y-2" data-testid="ms-company-list">
              {companies.map((company) => {
                // ADR-0036 — MULTIPLICIDAD: una compañía puede tener varios mandatarios, así que ya no
                // se deshabilita por estar "tomada por otro". Solo se bloquea si está inactiva/deshabilitada
                // en el OT (RF33) y no estaba ya seleccionada.
                const blockedInOt = !company.isEnabled || !company.isActive;
                const checked = selected.has(company.companyTenantId);
                const disabled = blockedInOt && !checked;
                // Otros mandatarios que ya cubren esta compañía (informativo, no bloquea).
                const otherSigners = company.assignedSigners.filter((s) => s.mandateSignerId !== editing?.id);
                const note = blockedInOt
                  ? "bloqueada o inactiva en el OT"
                  : otherSigners.length > 0
                    ? `${otherSigners.length} mandatario${otherSigners.length > 1 ? "s" : ""} más`
                    : null;
                return (
                  <li
                    key={company.companyTenantId}
                    className="flex items-center gap-3 rounded-xl border px-3 py-2 text-xs dark:bg-[#0B0F14]"
                  >
                    <input
                      id={`ms-company-${company.companyTenantId}`}
                      type="checkbox"
                      checked={checked}
                      disabled={disabled}
                      aria-describedby={note ? `ms-company-${company.companyTenantId}-reason` : undefined}
                      onChange={() => toggleCompany(company.companyTenantId)}
                      className="h-4 w-4 accent-[#557EFF]"
                    />
                    <label
                      htmlFor={`ms-company-${company.companyTenantId}`}
                      className={`flex-1 ${disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer"}`}
                    >
                      <span className="font-semibold">{company.legalName}</span>
                      {note && (
                        <span
                          id={`ms-company-${company.companyTenantId}-reason`}
                          className="ml-2 inline-block rounded-full border px-2 py-0.5 text-[10px] font-semibold"
                          style={
                            blockedInOt
                              ? { background: "rgba(255,78,0,0.1)", color: "#FF4E00" }
                              : { background: "rgba(85,126,255,0.1)", color: "#557EFF" }
                          }
                        >
                          {note}
                        </span>
                      )}
                    </label>
                  </li>
                );
              })}
            </ul>
          )}
        </fieldset>
      </div>
    </OtSidePanel>
  );
}
