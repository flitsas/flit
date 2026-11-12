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
import { ApiError, type TenantDomainResponse } from "@/lib/api/types";
import type { DomainClient } from "./DomainStatusPanel";

/** Con cliente propio manda el motivo exacto del servidor; sin él, el texto por código de marca blanca. */
function failureMessage(err: unknown, ownClient: boolean, fallback: string): string {
  if (ownClient && err instanceof ApiError && err.message.trim()) return err.message;
  return domainErrorMessage(domainErrorCode(err)) || fallback;
}

export interface DomainRegisterFormProps {
  tenantId: string;
  /** Producto del dominio. Sin él, el dominio principal de la red. */
  purpose?: string;
  /** Texto de la etiqueta del campo. */
  label?: string;
  /** Dominio vigente, o `null` si la red aún no tiene uno registrado. */
  domain: TenantDomainResponse | null;
  onRegistered: (domain: TenantDomainResponse) => void;
  onRemoved: () => void;
  /** Cliente propio del dominio (portal del organismo). Sin él, el de marca blanca. */
  client?: DomainClient;
  /** A quién pertenece el dominio en el texto de retiro. Por defecto «esta red». */
  ownerLabel?: string;
  /** Feature #14380 (portal del OT): un dominio por organismo. Con dominio vigente solo se puede retirar. */
  soloRetirar?: boolean;
  /** Texto de lo que pasa al retirar. Por defecto, el de marca blanca. */
  notaRetiro?: string;
}

/**
 * Registrar, cambiar o retirar el dominio de una cabeza MARCA_BLANCA — exclusivo SuperAdmin
 * (HU #12427 AC3; registrar/cambiar/retirar es siempre `/admin/companies/{tenantId}/domain`,
 * nunca `/company/domain`, que es de solo lectura para la propia cabeza).
 */
export function DomainRegisterForm({ tenantId, purpose, label = "Dominio de la red", domain, onRegistered, onRemoved, client, ownerLabel = "esta red", soloRetirar = false, notaRetiro }: DomainRegisterFormProps) {
  const [host, setHost] = useState(domain?.host ?? "");
  const inputId = `domain-host${purpose ? `-${purpose}` : ""}`;
  const [saving, setSaving] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [confirmingRemove, setConfirmingRemove] = useState(false);
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  const isChange = Boolean(domain);
  const mostrarCampo = !(soloRetirar && isChange);

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
      const body = { host: trimmed, rowVersion: domain?.rowVersion ?? null };
      const updated = client ? await client.register(body) : await registerAdminDomain(tenantId, body, purpose);
      setHost(updated.host);
      onRegistered(updated);
    } catch (err) {
      setFormError(failureMessage(err, client !== undefined, "No se pudo registrar el dominio."));
    } finally {
      setSaving(false);
    }
  }

  async function handleRemove() {
    setRemoving(true);
    setFormError(null);
    try {
      await (client ? client.remove() : removeAdminDomain(tenantId, purpose));
      setHost("");
      setConfirmingRemove(false);
      onRemoved();
    } catch (err) {
      setFormError(failureMessage(err, client !== undefined, "No se pudo retirar el dominio."));
    } finally {
      setRemoving(false);
    }
  }

  return (
    <form onSubmit={(e) => void handleSubmit(e)} className="flex flex-col gap-3">
      {mostrarCampo && (
      <div>
        <label htmlFor={inputId} className="mb-1 block text-xs font-semibold" style={{ color: "var(--color-flit-primary)" }}>
          {label}
        </label>
        <input
          id={inputId}
          type="text"
          value={host}
          onChange={(e) => setHost(e.target.value)}
          placeholder="app.tudominio.com"
          maxLength={253}
          className="w-full max-w-md rounded-lg border px-3 py-2 font-mono text-sm"
          style={{ borderColor: fieldError ? "var(--destructive)" : "var(--border)" }}
          aria-describedby={isChange ? `${inputId}-hint` : undefined}
          aria-invalid={Boolean(fieldError)}
        />
        {isChange && (
          <p id={`${inputId}-hint`} className="mt-1 text-[10px] opacity-60">
            Cambiar el dominio reinicia el ciclo de comprobación desde cero.
          </p>
        )}
        {fieldError && (
          <p role="alert" className="mt-1 text-[10px] font-medium" style={{ color: "var(--destructive)" }}>
            {fieldError}
          </p>
        )}
      </div>
      )}

      {!mostrarCampo && <p className="text-xs">Para usar otro dominio, retira primero este.</p>}

      {formError && (
        <p role="alert" className="text-xs font-medium" style={{ color: "var(--destructive)" }}>
          {formError}
        </p>
      )}

      <div className="flex flex-wrap gap-3">
        {mostrarCampo && (
        <button
          type="submit"
          disabled={saving || removing}
          className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
          style={{ background: "var(--gradient-action)" }}
        >
          <Save className="h-3.5 w-3.5" aria-hidden />
          {saving ? "Guardando…" : isChange ? "Cambiar dominio" : "Registrar dominio"}
        </button>
        )}

        {isChange && (
          <button
            type="button"
            onClick={() => setConfirmingRemove(true)}
            disabled={saving || removing}
            className="ml-auto inline-flex items-center gap-1.5 rounded-xl border px-4 py-2 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50"
            style={{ borderColor: "var(--destructive)", color: "var(--destructive)" }}
          >
            <Undo2 className="h-3.5 w-3.5" aria-hidden /> Retirar dominio
          </button>
        )}
      </div>

      {confirmingRemove && (
        <div
          className="rounded-xl border p-3 text-xs"
          style={{ borderColor: "var(--destructive)" }}
          role="alertdialog"
          aria-label="Confirmar retiro del dominio"
        >
          <p style={{ color: "var(--color-flit-primary)" }}>
            ¿Retirar el dominio de {ownerLabel}? {notaRetiro ?? "Los usuarios volverán a acceder por el dominio de FLIT."}
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
              style={{ background: "var(--destructive)", color: "var(--destructive-foreground)" }}
            >
              {removing ? "Retirando…" : "Retirar"}
            </button>
          </div>
        </div>
      )}
    </form>
  );
}
