"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { CheckCircle2, Clock, Copy, RefreshCw, ShieldAlert, ShieldCheck } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  cooldownSecondsFromError,
  domainErrorCode,
  domainErrorMessage,
  getAdminDomain,
  getCompanyDomain,
  verifyAdminDomain,
  verifyCompanyDomain,
} from "@/lib/api/domain-client";
import { ApiError, type TenantDomainResponse, type TenantDomainStatus } from "@/lib/api/types";

export type DomainPanelMode = "admin" | "company";

export interface DomainStatusPanelProps {
  mode: DomainPanelMode;
  /** Requerido cuando `mode === "admin"`. */
  tenantId?: string;
  /**
   * Se llama con el dominio vigente en cada carga o comprobación exitosas (o `null` si la red no
   * tiene dominio). `DomainRegisterForm` lo usa para conocer el `rowVersion` vigente sin duplicar
   * la petición GET.
   */
  onLoaded?: (domain: TenantDomainResponse | null) => void;
  /** Incrementar para forzar una recarga externa (p. ej. tras registrar/retirar el dominio). */
  reloadToken?: number;
}

const STATUS_META: Record<TenantDomainStatus, { label: string; Icon: typeof Clock; color: string }> = {
  pending: { label: "Pendiente de comprobación", Icon: Clock, color: "#F9AC00" },
  verified: { label: "Comprobado", Icon: ShieldCheck, color: "#557EFF" },
  active: { label: "Activo", Icon: CheckCircle2, color: "#8CC63F" },
  failed: { label: "Comprobación fallida", Icon: ShieldAlert, color: "#FF4E00" },
};

const REASON_MESSAGES: Record<string, string> = {
  TXT_NOT_FOUND: "No se encontró el registro TXT de verificación en el DNS.",
  TXT_MISMATCH: "El registro TXT encontrado no coincide con el valor esperado.",
  DNS_ERROR: "Ocurrió un error al consultar el DNS. Vuelve a intentarlo más tarde.",
};

function reasonMessage(reason: string | null): string | null {
  if (!reason) return null;
  return REASON_MESSAGES[reason] ?? `Motivo: ${reason}.`;
}

function formatDateTime(iso: string | null): string | null {
  if (!iso) return null;
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return parsed.toLocaleString("es-CO", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/**
 * Estado del dominio + instrucciones DNS copiables + comprobación a demanda (HU #12427 AC1/AC2).
 * `mode="admin"` (SuperAdmin sobre `tenantId`) y `mode="company"` (autogestión de la cabeza)
 * comparten exactamente esta UI — solo cambia qué endpoint de `/verify` se llama (AC3).
 */
export function DomainStatusPanel({ mode, tenantId, onLoaded, reloadToken }: DomainStatusPanelProps) {
  const [status, setStatus] = useState<UiStatus>("loading");
  const [domain, setDomain] = useState<TenantDomainResponse | null>(null);
  const [verifying, setVerifying] = useState(false);
  const [verifyError, setVerifyError] = useState<string | null>(null);
  const [verifyMessage, setVerifyMessage] = useState<string | null>(null);
  const [cooldownSeconds, setCooldownSeconds] = useState<number | null>(null);
  const cooldownTimer = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      setVerifyError(null);
      setVerifyMessage(null);
      try {
        const data = mode === "admin" && tenantId ? await getAdminDomain(tenantId) : await getCompanyDomain();
        if (signal?.aborted) return;
        setDomain(data);
        setStatus(data ? "ready" : "empty");
        onLoaded?.(data);
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [mode, tenantId, onLoaded],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load, reloadToken]);

  useEffect(() => {
    return () => {
      if (cooldownTimer.current) clearInterval(cooldownTimer.current);
    };
  }, []);

  function startCooldown(seconds: number) {
    if (cooldownTimer.current) clearInterval(cooldownTimer.current);
    setCooldownSeconds(seconds);
    cooldownTimer.current = setInterval(() => {
      setCooldownSeconds((prev) => {
        if (prev === null || prev <= 1) {
          if (cooldownTimer.current) clearInterval(cooldownTimer.current);
          return null;
        }
        return prev - 1;
      });
    }, 1000);
  }

  async function handleVerify() {
    setVerifying(true);
    setVerifyError(null);
    setVerifyMessage(null);
    try {
      const result = mode === "admin" && tenantId ? await verifyAdminDomain(tenantId) : await verifyCompanyDomain();
      setDomain(result);
      onLoaded?.(result);
      setVerifyMessage(
        result.status === "active" || result.status === "verified"
          ? "Comprobación exitosa."
          : "Se comprobó el dominio; sigue pendiente. Revisa el motivo indicado.",
      );
    } catch (err) {
      if (err instanceof ApiError && err.status === 429) {
        startCooldown(cooldownSecondsFromError(err) ?? 30);
        setVerifyError(err.message);
      } else {
        setVerifyError(domainErrorMessage(domainErrorCode(err)) || "No se pudo comprobar el dominio.");
      }
    } finally {
      setVerifying(false);
    }
  }

  return (
    <div>
      <UiStateBoundary
        status={status}
        onRetry={() => void load()}
        errorMessage="No se pudo cargar el estado del dominio."
        emptyMessage="Esta red aún no tiene un dominio registrado."
      >
        {domain && <DomainStatusBody domain={domain} />}

        {domain && (domain.status === "pending" || domain.status === "failed") && (
          <DnsInstructions verification={domain.verification} />
        )}

        {domain && (
          <div className="mt-4 flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={() => void handleVerify()}
              disabled={verifying || (cooldownSeconds !== null && cooldownSeconds > 0)}
              className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              <RefreshCw className={verifying ? "h-3.5 w-3.5 animate-spin" : "h-3.5 w-3.5"} aria-hidden />
              {verifying
                ? "Comprobando…"
                : cooldownSeconds !== null && cooldownSeconds > 0
                  ? `Espera ${cooldownSeconds} s`
                  : "Comprobar ahora"}
            </button>
            {verifyMessage && (
              <p role="status" className="text-xs font-medium" style={{ color: "#8CC63F" }}>
                {verifyMessage}
              </p>
            )}
            {verifyError && (
              <p role="alert" className="text-xs font-medium" style={{ color: "#FF4E00" }}>
                {verifyError}
              </p>
            )}
          </div>
        )}
      </UiStateBoundary>
    </div>
  );
}

function DomainStatusBody({ domain }: { domain: TenantDomainResponse }) {
  const meta = STATUS_META[domain.status];
  const Icon = meta.Icon;
  const reason = reasonMessage(domain.statusReason);
  const changedAt = formatDateTime(domain.statusChangedAt);
  const graceUntil = formatDateTime(domain.graceUntil);
  const issuedAt = formatDateTime(domain.certificate.issuedAt);
  const expiresAt = formatDateTime(domain.certificate.expiresAt);

  return (
    <div className="rounded-xl border p-4" style={{ borderColor: "#DFE5ED" }}>
      <p className="text-xs font-semibold opacity-70">Dominio</p>
      <p data-testid="domain-host" className="mb-2 font-mono text-sm font-semibold" style={{ color: "#162744" }}>
        {domain.host}
      </p>

      {/* Estado con icono + texto — nunca solo color (AC5). */}
      <div className="flex items-center gap-2" role="status">
        <Icon className="h-4 w-4" style={{ color: meta.color }} aria-hidden />
        <span className="text-sm font-semibold" style={{ color: meta.color }}>
          {meta.label}
        </span>
      </div>

      {reason && <p className="mt-1 text-xs opacity-80">{reason}</p>}
      {changedAt && <p className="mt-1 text-[11px] opacity-60">Último cambio: {changedAt}</p>}

      {graceUntil && (
        <p className="mt-2 rounded-lg border px-2.5 py-1.5 text-xs" style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)", color: "#8a6000" }}>
          Registro TXT no encontrado; el dominio sigue operando hasta {graceUntil}.
        </p>
      )}

      {issuedAt && (
        <p className="mt-2 text-[11px] opacity-70">
          Certificado emitido el {issuedAt}
          {expiresAt ? ` · vence el ${expiresAt}` : ""}.
        </p>
      )}
    </div>
  );
}

function DnsInstructions({
  verification,
}: {
  verification: TenantDomainResponse["verification"];
}) {
  return (
    <div className="mt-4">
      <h3 className="mb-2 text-xs font-semibold" style={{ color: "#162744" }}>
        Registros DNS que debes crear
      </h3>
      <div className="overflow-x-auto rounded-xl border" style={{ borderColor: "#DFE5ED" }}>
        <table className="w-full text-left text-xs">
          <thead>
            <tr className="bg-[#F4F7FC] dark:bg-white/5">
              <th className="px-3 py-2 font-semibold">Tipo</th>
              <th className="px-3 py-2 font-semibold">Nombre</th>
              <th className="px-3 py-2 font-semibold">Valor</th>
              <th className="px-3 py-2 font-semibold">
                <span className="sr-only">Acciones</span>
              </th>
            </tr>
          </thead>
          <tbody>
            <DnsRow type="TXT" name={verification.txtName} value={verification.txtValue} />
            <DnsRow type="CNAME" name={verification.cnameName} value={verification.cnameTarget} />
          </tbody>
        </table>
      </div>
    </div>
  );
}

function DnsRow({ type, name, value }: { type: string; name: string; value: string }) {
  return (
    <tr className="border-t" style={{ borderColor: "#DFE5ED" }}>
      <td className="px-3 py-2 font-semibold">{type}</td>
      <td className="max-w-[220px] truncate px-3 py-2 font-mono">{name}</td>
      <td className="max-w-[260px] truncate px-3 py-2 font-mono">{value}</td>
      <td className="px-3 py-2">
        <CopyButton value={value} label={`Copiar valor del registro ${type}`} />
      </td>
    </tr>
  );
}

function CopyButton({ value, label }: { value: string; label: string }) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(value);
      } else {
        throw new Error("clipboard no disponible");
      }
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* clipboard no disponible en este entorno — sin bloquear la UI */
    }
  }

  return (
    <>
      <button
        type="button"
        onClick={() => void copy()}
        className="inline-flex items-center gap-1 rounded-lg border px-2 py-1 text-[11px] font-semibold"
        style={{ borderColor: "#557EFF", color: "#557EFF" }}
        aria-label={label}
      >
        <Copy className="h-3 w-3" aria-hidden />
        {copied ? "Copiado" : "Copiar"}
      </button>
      <span className="sr-only" role="status" aria-live="polite">
        {copied ? "Valor copiado al portapapeles." : ""}
      </span>
    </>
  );
}
