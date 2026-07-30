"use client";

import { useMemo, useState, type FormEvent } from "react";
import { AlertTriangle, CheckCircle2, Download, Loader2 } from "lucide-react";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload } from "@/lib/auth/jwt";
import { describeImprontaError, generarImpronta } from "@/lib/api/admin-improntas";
import type { GenerarImprontaRequest, GenerarImprontaResult } from "@/lib/api/types-improntas";
import { digitsOnly } from "@/lib/format/currency";
import { sanitizePlate } from "@/lib/validation/fieldRules";
import { IMPRONTA_INPUT_CLS, IMPRONTA_LABEL_CLS, IMPRONTA_SECTION_CLS } from "./improntas-form-styles";

type SubmitStatus = "idle" | "submitting" | "error" | "success";

interface SessionDefaults {
  orgNombre: string;
  operador: string;
}

/**
 * Datos de organización/operador tomados del JWT en sesión (mismo mecanismo que
 * `usePermissions`/`Shell.useCurrentUser`: `getToken()` + `decodeJwtPayload()`).
 */
function useSessionDefaults(): SessionDefaults {
  return useMemo(() => {
    const payload = decodeJwtPayload(getToken());
    const tenantName = typeof payload?.tenant_name === "string" ? payload.tenant_name : "";
    const displayName =
      (typeof payload?.display_name === "string" && payload.display_name) ||
      (typeof payload?.email === "string" && payload.email) ||
      "";
    return { orgNombre: tenantName, operador: displayName };
  }, []);
}

/**
 * Formulario de captura del módulo "Generación de improntas" (HU #10469 AC1/AC3,
 * HU #10471 AC1/AC2/AC3). Controlado con `useState` (sin react-hook-form/zod, no están
 * en el proyecto). Envía la solicitud a Kyverum RUNT vía `generarImpronta` (descarga
 * directa del PDF mediante `downloadFile`) y traduce cada tipo de error del backend
 * (`VALIDATION_ERROR`/`UNAUTHORIZED`/`UPSTREAM_UNAVAILABLE`) a un mensaje específico
 * vía `describeImprontaError`, sin exponer detalles técnicos crudos del proveedor.
 */
export function ImprontaFormPanel() {
  const defaults = useSessionDefaults();

  const [placa, setPlaca] = useState("");
  const [documento, setDocumento] = useState("");
  const [orgNombre, setOrgNombre] = useState(defaults.orgNombre);
  const [operador, setOperador] = useState(defaults.operador);

  const [attempted, setAttempted] = useState(false);
  const [status, setStatus] = useState<SubmitStatus>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [result, setResult] = useState<GenerarImprontaResult | null>(null);

  const placaMissing = placa.trim().length === 0;
  const documentoMissing = documento.trim().length === 0;
  const orgNombreMissing = orgNombre.trim().length === 0;
  const operadorMissing = operador.trim().length === 0;

  const hasBlockingErrors = placaMissing || documentoMissing || orgNombreMissing || operadorMissing;

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    // HU #10471 AC3 — evita un doble envío concurrente si por algún medio (p. ej.
    // Enter en un campo) se dispara `submit` mientras ya hay una solicitud en curso.
    if (status === "submitting") {
      return;
    }

    setAttempted(true);

    // AC3 — bloquea el envío sin invocar al backend si falta placa, documento, nombre de
    // organización u operador.
    if (hasBlockingErrors) {
      return;
    }

    setStatus("submitting");
    setErrorMessage(null);
    setResult(null);

    const body: GenerarImprontaRequest = {
      placa: placa.trim().toUpperCase(),
      documento: documento.trim(),
      orgNombre: orgNombre.trim(),
      operador: operador.trim(),
    };

    try {
      const generated = await generarImpronta(body);
      setResult(generated);
      setStatus("success");
    } catch (error) {
      setStatus("error");
      setErrorMessage(describeImprontaError(error));
    }
  }

  const submitting = status === "submitting";

  return (
    <form className="flex flex-col gap-4" onSubmit={(e) => void handleSubmit(e)} noValidate>
      <fieldset className={IMPRONTA_SECTION_CLS}>
        <legend className="px-1 text-sm font-semibold" style={{ color: "#162744" }}>
          Datos del vehículo
        </legend>

        <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-placa">
          Placa <span aria-hidden="true">*</span>
          <input
            id="impronta-placa"
            className={`mt-1 uppercase tracking-widest ${IMPRONTA_INPUT_CLS}`}
            value={placa}
            onChange={(e) => setPlaca(sanitizePlate(e.target.value))}
            placeholder="ABC123"
            aria-required="true"
            aria-invalid={attempted && placaMissing}
            aria-describedby={attempted && placaMissing ? "impronta-placa-error" : undefined}
          />
        </label>
        {attempted && placaMissing && (
          <p id="impronta-placa-error" role="alert" className="text-[11px] font-medium" style={{ color: "#FF4E00" }}>
            La placa es obligatoria.
          </p>
        )}

        <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-documento">
          Documento del propietario <span aria-hidden="true">*</span>
          <input
            id="impronta-documento"
            className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
            value={documento}
            onChange={(e) => setDocumento(digitsOnly(e.target.value))}
            inputMode="numeric"
            pattern="[0-9]*"
            autoComplete="off"
            placeholder="1040326572"
            aria-required="true"
            aria-invalid={attempted && documentoMissing}
            aria-describedby={attempted && documentoMissing ? "impronta-documento-error" : undefined}
          />
        </label>
        {attempted && documentoMissing && (
          <p id="impronta-documento-error" role="alert" className="text-[11px] font-medium" style={{ color: "#FF4E00" }}>
            El documento del propietario es obligatorio.
          </p>
        )}
      </fieldset>

      <fieldset className={IMPRONTA_SECTION_CLS}>
        <legend className="px-1 text-sm font-semibold" style={{ color: "#162744" }}>
          Organización solicitante
        </legend>
        <p className="text-[11px] opacity-70">
          Datos pre-cargados desde tu compañía y usuario en sesión. Puedes editarlos si el
          certificado debe emitirse a nombre de otra organización u operador.
        </p>

        <div>
          <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-org-nombre">
            Nombre de la organización <span aria-hidden="true">*</span>
            <input
              id="impronta-org-nombre"
              className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
              value={orgNombre}
              onChange={(e) => setOrgNombre(e.target.value)}
              aria-required="true"
              aria-invalid={attempted && orgNombre.trim().length === 0}
            />
          </label>
          {attempted && orgNombre.trim().length === 0 && (
            <p role="alert" className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }}>
              El nombre de la organización es obligatorio.
            </p>
          )}
        </div>

        <div>
          <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-operador">
            Operador <span aria-hidden="true">*</span>
            <input
              id="impronta-operador"
              className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
              value={operador}
              onChange={(e) => setOperador(e.target.value)}
              aria-required="true"
              aria-invalid={attempted && operadorMissing}
            />
          </label>
          {attempted && operadorMissing && (
            <p role="alert" className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }}>
              El operador es obligatorio.
            </p>
          )}
        </div>
      </fieldset>

      {submitting && (
        <p
          role="status"
          aria-live="polite"
          className="flex items-center gap-2 text-[11px] font-medium"
          style={{ color: "#162744" }}
          data-testid="impronta-loading"
        >
          <Loader2 className="h-3.5 w-3.5 shrink-0 animate-spin" aria-hidden="true" />
          Generando impronta… puede tardar unos segundos porque se consulta un servicio externo
          (RUNT).
        </p>
      )}

      {status === "error" && errorMessage && (
        <div
          role="alert"
          aria-live="assertive"
          className="flex items-start gap-2 rounded-xl border p-3 text-xs font-medium"
          style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
          data-testid="impronta-error"
        >
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <span>{errorMessage}</span>
        </div>
      )}

      {status === "success" && (
        <div
          role="status"
          aria-live="polite"
          className="flex items-start gap-2 rounded-xl border p-3 text-xs font-medium"
          style={{ borderColor: "#00DBD5", color: "#162744" }}
          data-testid="impronta-success"
        >
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" style={{ color: "#00DBD5" }} aria-hidden="true" />
          <div className="flex flex-col gap-1">
            <span>
              Impronta generada y descargada. La descarga del PDF se inició en tu navegador.
            </span>
            {(result?.radicado || result?.hash) && (
              <dl className="grid grid-cols-1 gap-x-4 gap-y-0.5 text-[11px] font-normal opacity-80 md:grid-cols-2">
                {result?.radicado && (
                  <div className="flex gap-1">
                    <dt className="font-semibold">Radicado:</dt>
                    <dd data-testid="impronta-radicado">{result.radicado}</dd>
                  </div>
                )}
                {result?.hash && (
                  <div className="flex gap-1 break-all">
                    <dt className="font-semibold">Hash:</dt>
                    <dd data-testid="impronta-hash">{result.hash}</dd>
                  </div>
                )}
              </dl>
            )}
          </div>
        </div>
      )}

      <button
        type="submit"
        disabled={submitting}
        aria-busy={submitting}
        className="flex w-fit items-center justify-center gap-2 rounded-xl px-6 py-2.5 text-xs font-semibold text-white disabled:opacity-50"
        style={{ background: "linear-gradient(135deg,#557EFF 0%,#00DBD5 100%)" }}
      >
        {submitting ? (
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
        ) : (
          <Download className="h-4 w-4" aria-hidden="true" />
        )}
        {submitting ? "Generando impronta…" : "Generar impronta"}
      </button>
    </form>
  );
}
