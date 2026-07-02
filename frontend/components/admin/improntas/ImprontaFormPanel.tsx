"use client";

import { useMemo, useState, type FormEvent } from "react";
import { AlertTriangle, CheckCircle2, Download, Loader2 } from "lucide-react";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload } from "@/lib/auth/jwt";
import { generarImpronta } from "@/lib/api/admin-improntas";
import type { GenerarImprontaRequest } from "@/lib/api/types-improntas";
import { IMPRONTA_INPUT_CLS, IMPRONTA_LABEL_CLS, IMPRONTA_SECTION_CLS } from "./improntas-form-styles";

type SubmitStatus = "idle" | "submitting" | "error" | "success";

interface SessionDefaults {
  orgNombre: string;
  operador: string;
}

/**
 * Datos de organización/operador tomados del JWT en sesión (mismo mecanismo que
 * `usePermissions`/`Shell.useCurrentUser`: `getToken()` + `decodeJwtPayload()`).
 *
 * Limitación conocida (documentada en el handoff de la HU #10469): el JWT solo trae
 * `tenant_name` y `email`/`display_name`; no existe hoy un endpoint que devuelva NIT
 * ni ciudad de la empresa/tenant activa, así que `orgNit` y `orgCiudad` arrancan
 * vacíos y editables — no se dejan de pre-cargar, simplemente no hay fuente aún. Tampoco son
 * obligatorios (verificado contra el proveedor real: Kyverum genera la impronta sin ellos).
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
 * Formulario de captura del módulo "Generación de improntas" (HU #10469, AC1/AC3).
 * Controlado con `useState` (sin react-hook-form/zod, no están en el proyecto).
 * Envía la solicitud a Kyverum RUNT vía `generarImpronta` (descarga directa del PDF);
 * el manejo fino de mensajes de error por tipo (validación/auth/upstream de Kyverum)
 * es alcance de la HU #10471 — aquí se resuelve con un estado de error genérico.
 */
export function ImprontaFormPanel() {
  const defaults = useSessionDefaults();

  const [placa, setPlaca] = useState("");
  const [documento, setDocumento] = useState("");
  const [numMotor, setNumMotor] = useState("");
  const [numChasis, setNumChasis] = useState("");
  const [numSerie, setNumSerie] = useState("");
  const [marca, setMarca] = useState("");
  const [linea, setLinea] = useState("");
  const [modelo, setModelo] = useState("");
  const [orgNombre, setOrgNombre] = useState(defaults.orgNombre);
  const [orgNit, setOrgNit] = useState("");
  const [orgCiudad, setOrgCiudad] = useState("");
  const [operador, setOperador] = useState(defaults.operador);

  const [attempted, setAttempted] = useState(false);
  const [status, setStatus] = useState<SubmitStatus>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const placaMissing = placa.trim().length === 0;
  const documentoMissing = documento.trim().length === 0;
  // numMotor/numChasis/numSerie y orgNit/orgCiudad ya NO son obligatorios: verificado contra el
  // proveedor real (Kyverum RUNT resuelve los identificadores del vehículo internamente vía
  // placa+documento, y genera la impronta sin orgNit/orgCiudad).
  const orgNombreMissing = orgNombre.trim().length === 0;
  const operadorMissing = operador.trim().length === 0;

  const hasBlockingErrors = placaMissing || documentoMissing || orgNombreMissing || operadorMissing;

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setAttempted(true);

    // AC3 — bloquea el envío sin invocar al backend si falta placa, documento, nombre de
    // organización u operador.
    if (hasBlockingErrors) {
      return;
    }

    setStatus("submitting");
    setErrorMessage(null);

    const body: GenerarImprontaRequest = {
      placa: placa.trim().toUpperCase(),
      documento: documento.trim(),
      numMotor: numMotor.trim() || undefined,
      numChasis: numChasis.trim() || undefined,
      numSerie: numSerie.trim() || undefined,
      marca: marca.trim() || undefined,
      linea: linea.trim() || undefined,
      modelo: modelo.trim() || undefined,
      orgNombre: orgNombre.trim(),
      orgNit: orgNit.trim() || undefined,
      orgCiudad: orgCiudad.trim() || undefined,
      operador: operador.trim(),
    };

    try {
      await generarImpronta(body);
      setStatus("success");
    } catch {
      setStatus("error");
      setErrorMessage(
        "No se pudo generar la impronta. Verifica los datos del vehículo e intenta nuevamente.",
      );
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
            onChange={(e) => setPlaca(e.target.value.toUpperCase())}
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
            onChange={(e) => setDocumento(e.target.value)}
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

        <p className="text-[11px] opacity-70">
          El número de motor, chasis o serie es opcional: Kyverum RUNT los resuelve
          automáticamente a partir de la placa y el documento cuando no se diligencian.
        </p>
        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
          <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-num-motor">
            Número de motor
            <input
              id="impronta-num-motor"
              className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
              value={numMotor}
              onChange={(e) => setNumMotor(e.target.value)}
            />
          </label>
          <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-num-chasis">
            Número de chasis
            <input
              id="impronta-num-chasis"
              className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
              value={numChasis}
              onChange={(e) => setNumChasis(e.target.value)}
            />
          </label>
          <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-num-serie">
            Número de serie (VIN)
            <input
              id="impronta-num-serie"
              className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
              value={numSerie}
              onChange={(e) => setNumSerie(e.target.value)}
            />
          </label>
        </div>

        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
          <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-marca">
            Marca
            <input
              id="impronta-marca"
              className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
              value={marca}
              onChange={(e) => setMarca(e.target.value)}
            />
          </label>
          <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-linea">
            Línea
            <input
              id="impronta-linea"
              className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
              value={linea}
              onChange={(e) => setLinea(e.target.value)}
            />
          </label>
          <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-modelo">
            Modelo
            <input
              id="impronta-modelo"
              className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
              value={modelo}
              onChange={(e) => setModelo(e.target.value)}
            />
          </label>
        </div>
      </fieldset>

      <fieldset className={IMPRONTA_SECTION_CLS}>
        <legend className="px-1 text-sm font-semibold" style={{ color: "#162744" }}>
          Organización solicitante
        </legend>
        <p className="text-[11px] opacity-70">
          Datos pre-cargados desde tu compañía y usuario en sesión. Puedes editarlos si el
          certificado debe emitirse a nombre de otra organización u operador.
        </p>

        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
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
            <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-org-nit">
              NIT
              <input
                id="impronta-org-nit"
                className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
                value={orgNit}
                onChange={(e) => setOrgNit(e.target.value)}
              />
            </label>
          </div>
          <div>
            <label className={IMPRONTA_LABEL_CLS} style={{ color: "#162744" }} htmlFor="impronta-org-ciudad">
              Ciudad
              <input
                id="impronta-org-ciudad"
                className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
                value={orgCiudad}
                onChange={(e) => setOrgCiudad(e.target.value)}
              />
            </label>
          </div>
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

      {status === "error" && errorMessage && (
        <div
          role="alert"
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
          <span>
            Impronta generada correctamente. La descarga del PDF se inició en tu navegador.
          </span>
        </div>
      )}

      <button
        type="submit"
        disabled={submitting}
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
