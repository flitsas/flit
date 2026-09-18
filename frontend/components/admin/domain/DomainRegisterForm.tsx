"use client";

import { useState } from "react";
import { Save, Undo2 } from "lucide-react";
import {
  domainErrorCode,
  domainErrorMessage,
  isValidHostFormat,
  registerAdminDomain,
  removeAdminDomain,
} from "@/lib/api/domain-client";
import type { TenantDomainResponse } from "@/lib/api/types";

export interface DomainRegisterFormProps {
  tenantId: string;
  /** Dominio vigente, o `null` si la red aún no tiene uno registrado. */
  domain: TenantDomainResponse | null;
  onRegistered: (domain: TenantDomainResponse) => void;
  onRemoved: () => void;
}

/**
 * Registrar, cambiar o retirar el dominio de una cabeza MARCA_BLANCA — exclusivo SuperAdmin
 * (HU #12427 AC3; registrar/cambiar/retirar es siempre `/admin/companies/{tenantId}/domain`,
 * nunca `/company/domain`, que es de solo lectura para la propia cabeza).
 */
export function DomainRegisterForm({ tenantId, domain, onRegistered, onRemoved }: DomainRegisterFormProps) {
  const [host, setHost] = useState(domain?.host ?? "");
  const [saving, setSaving] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [confirmingRemove, setConfirmingRemove] = useState(false);
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  const isChange = Boolean(domain);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    const trimmed = host.trim().toLowerCase();
    setFieldError(null);
    setFormError(null);

    if (!trimmed) {
      setFieldError("El dominio es obligatorio.");
      return;
    }
    if (!isValidHostFormat(trimmed)) {
      setFieldError("El dominio no tiene un formato válido. Usa solo el nombre de host, sin esquema ni puerto.");
      return;
    }

    setSaving(true);
    try {
      const updated = await registerAdminDomain(tenantId, {
        host: trimmed,
        rowVersion: domain?.rowVersion ?? null,
      });
      setHost(updated.host);
      onRegistered(updated);
    } catch (err) {
      setFormError(domainErrorMessage(domainErrorCode(err)) || "No se pudo registrar el dominio.");
    } finally {
      setSaving(false);
    }
  }

  async function handleRemove() {
    setRemoving(true);
    setFormError(null);
    try {
      await removeAdminDomain(tenantId);
      setHost("");
      setConfirmingRemove(false);
      onRemoved();
    } catch (err) {
      setFormError(domainErrorMessage(domainErrorCode(err)) || "No se pudo retirar el dominio.");
    } finally {
      setRemoving(false);
    }
  }

  return (
    <form onSubmit={(e) => void handleSubmit(e)} className="flex flex-col gap-3">
      <div>
        <label htmlFor="domain-host" className="mb-1 block text-xs font-semibold" style={{ color: "#162744" }}>
          Dominio de la red
        </label>
        <input
          id="domain-host"
          type="text"
          value={host}
          onChange={(e) => setHost(e.target.value)}
          placeholder="app.tudominio.com"
          maxLength={253}
          className="w-full max-w-md rounded-lg border px-3 py-2 font-mono text-sm"
          style={{ borderColor: fieldError ? "#FF4E00" : "#DFE5ED" }}
          aria-describedby={isChange ? "domain-host-hint" : undefined}
          aria-invalid={Boolean(fieldError)}
        />
        {isChange && (
          <p id="domain-host-hint" className="mt-1 text-[10px] opacity-60">
            Cambiar el dominio reinicia el ciclo de comprobación desde cero.
          </p>
        )}
        {fieldError && (
          <p role="alert" className="mt-1 text-[10px] font-medium" style={{ color: "#FF4E00" }}>
            {fieldError}
          </p>
        )}
      </div>

      {formError && (
        <p role="alert" className="text-xs font-medium" style={{ color: "#FF4E00" }}>
          {formError}
        </p>
      )}

      <div className="flex flex-wrap gap-3">
        <button
          type="submit"
          disabled={saving || removing}
          className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          <Save className="h-3.5 w-3.5" aria-hidden />
          {saving ? "Guardando…" : isChange ? "Cambiar dominio" : "Registrar dominio"}
        </button>

        {isChange && (
          <button
            type="button"
            onClick={() => setConfirmingRemove(true)}
            disabled={saving || removing}
            className="ml-auto inline-flex items-center gap-1.5 rounded-xl border px-4 py-2 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50"
            style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
          >
            <Undo2 className="h-3.5 w-3.5" aria-hidden /> Retirar dominio
          </button>
        )}
      </div>

      {confirmingRemove && (
        <div
          className="rounded-xl border p-3 text-xs"
          style={{ borderColor: "#FF4E00" }}
          role="alertdialog"
          aria-label="Confirmar retiro del dominio"
        >
          <p style={{ color: "#162744" }}>
            ¿Retirar el dominio de esta red? Los usuarios volverán a acceder por el dominio de FLIT.
            El registro histórico se conserva.
          </p>
          <div className="mt-2 flex gap-2">
            <button
              type="button"
              onClick={() => setConfirmingRemove(false)}
              disabled={removing}
              className="rounded-lg border px-3 py-1.5 font-medium disabled:opacity-60"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={() => void handleRemove()}
              disabled={removing}
              className="rounded-lg px-3 py-1.5 font-semibold text-white disabled:opacity-60"
              style={{ background: "#FF4E00" }}
            >
              {removing ? "Retirando…" : "Retirar"}
            </button>
          </div>
        </div>
      )}
    </form>
  );
}
