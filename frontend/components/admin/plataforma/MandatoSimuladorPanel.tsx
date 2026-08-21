"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Eye, Mail } from "lucide-react";
import {
  fetchMandateSimulationPreview,
  listMandateSimulatorSigners,
  sendMandateSimulation,
  type MandateOtConfigView,
  type MandateSimulatorSignerOption,
} from "@/lib/api/admin-plataforma-mandatos";

import { tramitesClient } from "@/lib/api/tramites-client";
import { ApiError } from "@/lib/api/types";
import type { FurPrendaKind } from "@/lib/api/admin-plataforma-fur";
import type { ProcedureFamily, ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";
import { openPdfBlobInNewTab } from "@/lib/documents/open-document-tab";
import { MANDATO_TIPOS, resolveAssignmentMode, type MandatoTipoNegocio } from "@/lib/plataforma/mandato-templates";
import { useToast } from "@/components/admin/Toast";

/**
 * Envío de la simulación por correo. **Oculto** por decisión de producto (2026-08-21): el simulador
 * solo sirve para ver cómo queda el documento.
 *
 * <p>Es un interruptor de interfaz, no un borrado: el endpoint, el servicio y `sendMandateSimulation`
 * siguen intactos. Volver a exponerlo es cambiar esta constante a `true`.</p>
 */
const MOSTRAR_ENVIO_POR_CORREO = false;

const FAMILIAS: { value: ProcedureFamily; label: string }[] = [
  { value: "MATRICULAS", label: "Matrículas" },
  { value: "TRASPASO", label: "Traspaso" },
  { value: "OTROS", label: "Otros trámites" },
];

const PRENDA: { value: FurPrendaKind; label: string }[] = [
  { value: "ninguna", label: "No aplica" },
  { value: "inscripcion", label: "Inscripción de prenda" },
  { value: "levantamiento", label: "Levantamiento de prenda" },
  { value: "ambas", label: "Ambas (inscripción y levantamiento)" },
];

function inferFromType(type: ProcedureTypeSummary | undefined) {
  const code = (type?.code ?? "").toUpperCase();
  const prenda: FurPrendaKind =
    code === "LEVANTAR_INSCRIBIR_PRENDA"
      ? "ambas"
      : code.includes("LEVANTAMIENTO_PRENDA")
        ? "levantamiento"
        : code.includes("PRENDA_INSCRIPCION") || code.includes("INSCRIBIR_PRENDA")
          ? "inscripcion"
          : "ninguna";
  return {
    color: code === "CAMBIO_COLOR",
    combustible: code === "CONVERSION_COMBUSTIBLE",
    carroceria: code === "CAMBIO_CARROCERIA",
    blindaje: code.includes("BLINDAJE"),
    prenda,
    lockColor: code === "CAMBIO_COLOR",
    lockCombustible: code === "CONVERSION_COMBUSTIBLE",
    lockCarroceria: code === "CAMBIO_CARROCERIA",
    lockBlindaje: code.includes("BLINDAJE"),
    lockPrenda:
      code === "PRENDA_INSCRIPCION" ||
      code === "LEVANTAMIENTO_PRENDA" ||
      code === "LEVANTAR_INSCRIBIR_PRENDA",
  };
}

export interface MandatoSimuladorPanelProps {
  /** Organismos disponibles, ya cargados por el catálogo (no se vuelven a pedir). */
  offices: MandateOtConfigView[];
}

/**
 * Simulador de mandatos (HU #11707): muestra el contrato que emitiría un organismo según el tipo de
 * persona del mandante y el trámite.
 *
 * <p>El mandante y la placa se rellenan con <b>datos de muestra</b>. El trámite sí se elige: familia
 * y tipo salen de <c>tramites.procedure_types</c> (los mismos que radica el wizard).</p>
 */
export function MandatoSimuladorPanel({ offices }: MandatoSimuladorPanelProps) {
  const { show: showToast } = useToast();

  const [officeId, setOfficeId] = useState("");
  const [personType, setPersonType] = useState<"natural" | "juridica">("juridica");
  const [family, setFamily] = useState<ProcedureFamily | "">("TRASPASO");
  const [procedureTypeCode, setProcedureTypeCode] = useState("");
  const [prenda, setPrenda] = useState<FurPrendaKind>("ninguna");
  const [cambioColor, setCambioColor] = useState(false);
  const [cambioCombustible, setCambioCombustible] = useState(false);
  const [cambioCarroceria, setCambioCarroceria] = useState(false);
  const [blindaje, setBlindaje] = useState(false);
  const [tipo, setTipo] = useState<MandatoTipoNegocio | "config">("config");
  const [signerId, setSignerId] = useState("");
  const [toEmail, setToEmail] = useState("");

  const [signers, setSigners] = useState<MandateSimulatorSignerOption[]>([]);
  const [signersStatus, setSignersStatus] = useState<"idle" | "loading" | "ready" | "error">("idle");
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeSummary[]>([]);
  const [typesStatus, setTypesStatus] = useState<"idle" | "loading" | "ready" | "error">("idle");
  const [previewing, setPreviewing] = useState(false);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const busy = previewing || sending;

  const sortedOffices = useMemo(
    () => [...offices].sort((a, b) => a.name.localeCompare(b.name, "es")),
    [offices],
  );

  const loadSigners = useCallback(async (id: string) => {
    if (!id) {
      setSigners([]);
      setSignersStatus("idle");
      return;
    }
    setSignersStatus("loading");
    try {
      const items = await listMandateSimulatorSigners(id);
      setSigners(items);
      setSignersStatus("ready");
    } catch {
      setSigners([]);
      setSignersStatus("error");
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga dependiente del OT elegido
    void loadSigners(officeId);
    setSignerId("");
  }, [officeId, loadSigners]);

  useEffect(() => {
    let cancelled = false;
    setTypesStatus("loading");
    void tramitesClient
      .listPublishedProcedureTypes()
      .then((items) => {
        if (cancelled) return;
        setProcedureTypes(items);
        setTypesStatus("ready");
      })
      .catch(() => {
        if (cancelled) return;
        setProcedureTypes([]);
        setTypesStatus("error");
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const typesInFamily = useMemo(
    () => procedureTypes.filter((t) => t.family === family).sort((a, b) => a.name.localeCompare(b.name, "es")),
    [procedureTypes, family],
  );

  useEffect(() => {
    if (typesStatus !== "ready") return;
    if (!family) {
      setProcedureTypeCode("");
      return;
    }
    const stillValid = typesInFamily.some((t) => t.code === procedureTypeCode);
    if (stillValid) return;
    const preferred =
      family === "TRASPASO"
        ? typesInFamily.find((t) => t.code === "TRASPASO_STANDARD")
        : family === "MATRICULAS"
          ? typesInFamily.find((t) => t.code === "MATRICULA_NUEVA")
          : undefined;
    setProcedureTypeCode(preferred?.code ?? typesInFamily[0]?.code ?? "");
  }, [family, typesInFamily, typesStatus, procedureTypeCode]);

  const selectedType = typesInFamily.find((t) => t.code === procedureTypeCode);
  const inferredLocks = inferFromType(selectedType);

  useEffect(() => {
    const inferred = inferFromType(selectedType);
    setCambioColor(inferred.color);
    setCambioCombustible(inferred.combustible);
    setCambioCarroceria(inferred.carroceria);
    setBlindaje(inferred.blindaje);
    setPrenda(inferred.prenda);
  }, [procedureTypeCode, selectedType]);

  const body = () => ({
    officeId,
    personType,
    procedureTypeCode: procedureTypeCode || null,
    assignmentMode: tipo === "config" ? null : resolveAssignmentMode(tipo),
    mandateSignerId: signerId || null,
    prenda,
    cambioColor,
    cambioCombustible,
    cambioCarroceria,
    blindaje,
  });

  const handlePreview = async () => {
    setError(null);
    setPreviewing(true);
    // El helper abre la pestaña ANTES de pedir el PDF (si no, el bloqueador de pop-ups la mata) y
    // por eso relanza un error genérico. Se retiene el original para poder decir POR QUÉ falló.
    let original: unknown = null;
    try {
      await openPdfBlobInNewTab(async () => {
        try {
          return await fetchMandateSimulationPreview(body());
        } catch (err) {
          original = err;
          throw err;
        }
      });
    } catch (err) {
      setError(messageFrom(original ?? err, "No se pudo generar la simulación."));
    } finally {
      setPreviewing(false);
    }
  };

  const handleSend = async () => {
    setError(null);
    if (!toEmail.trim()) {
      setError("Indica el correo de destino.");
      return;
    }
    setSending(true);
    try {
      const message = await sendMandateSimulation({ ...body(), toEmail: toEmail.trim() });
      showToast(message, "success");
    } catch (err) {
      setError(messageFrom(err, "No se pudo enviar la simulación."));
    } finally {
      setSending(false);
    }
  };

  const soloMandante = tipo === "institucional" || tipo === "abierto";

  return (
    <section
      className="space-y-3 rounded-2xl border border-[#DFE5ED] bg-white/60 p-4 dark:border-white/10 dark:bg-[#0B0F14]/60"
      aria-labelledby="mandato-simulador-heading"
      data-testid="mandato-simulador"
    >
      <div>
        <h3
          id="mandato-simulador-heading"
          className="text-xs font-semibold text-[#162244] dark:text-white"
        >
          Simulador de mandatos
        </h3>
        <p className="mt-0.5 text-[11px] leading-relaxed text-[#59677D] dark:text-white/65">
          Mira cómo queda el contrato que emitiría cada organismo según el tipo de persona y el
          trámite. El mandante y la placa se rellenan con datos de muestra; el tipo de trámite es el
          del catálogo publicado.
        </p>
      </div>

      {error ? (
        <p
          role="alert"
          className="rounded-xl border border-[#FF4E00]/40 bg-[rgba(255,78,0,0.06)] px-3 py-2 text-xs text-[#FF4E00]"
        >
          {error}
        </p>
      ) : null}

      <div className="grid gap-3 sm:grid-cols-2">
        <label className="block space-y-1.5">
          <span className="text-xs font-semibold text-[#162244] dark:text-white">
            Organismo de tránsito
          </span>
          <select
            value={officeId}
            onChange={(e) => setOfficeId(e.target.value)}
            disabled={busy}
            data-testid="simulador-ot"
            className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
          >
            <option value="">Selecciona un organismo…</option>
            {sortedOffices.map((o) => (
              <option key={o.officeId} value={o.officeId}>
                {o.name} · {o.code}
              </option>
            ))}
          </select>
        </label>

        <label className="block space-y-1.5">
          <span className="text-xs font-semibold text-[#162244] dark:text-white">
            El mandante firma como
          </span>
          <select
            value={personType}
            onChange={(e) => setPersonType(e.target.value as "natural" | "juridica")}
            disabled={busy}
            data-testid="simulador-persona"
            className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
          >
            <option value="juridica">Persona jurídica (con representante legal)</option>
            <option value="natural">Persona natural</option>
          </select>
        </label>

        <label className="block space-y-1.5">
          <span className="text-xs font-semibold text-[#162244] dark:text-white">
            Tipo de trámite padre (familia)
          </span>
          <select
            value={family}
            onChange={(e) => setFamily(e.target.value as ProcedureFamily | "")}
            disabled={busy || typesStatus === "loading"}
            data-testid="simulador-familia"
            className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
          >
            <option value="">Selecciona una familia</option>
            {FAMILIAS.map((f) => (
              <option key={f.value} value={f.value}>
                {f.label}
              </option>
            ))}
          </select>
        </label>

        <label className="block space-y-1.5">
          <span className="text-xs font-semibold text-[#162244] dark:text-white">Tipo de trámite</span>
          <select
            value={procedureTypeCode}
            onChange={(e) => setProcedureTypeCode(e.target.value)}
            disabled={busy || !family || typesInFamily.length === 0}
            data-testid="simulador-tramite"
            className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
          >
            <option value="">Selecciona un tipo</option>
            {typesInFamily.map((t) => (
              <option key={t.id} value={t.code}>
                {t.name} ({t.code})
              </option>
            ))}
          </select>
          {typesStatus === "error" ? (
            <span className="block text-xs text-[#FF4E00]">
              No se pudieron cargar los tipos de trámite publicados.
            </span>
          ) : null}
          {typesStatus === "ready" && family && typesInFamily.length === 0 ? (
            <span className="block text-xs text-[#59677D] dark:text-white/65">
              Esta familia no tiene tipos publicados.
            </span>
          ) : null}
        </label>

        <label className="block space-y-1.5">
          <span className="text-xs font-semibold text-[#162244] dark:text-white">Prenda</span>
          <select
            value={prenda}
            onChange={(e) => setPrenda(e.target.value as FurPrendaKind)}
            disabled={busy || inferredLocks.lockPrenda}
            data-testid="simulador-prenda"
            className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
          >
            {PRENDA.map((p) => (
              <option key={p.value} value={p.value}>
                {p.label}
              </option>
            ))}
          </select>
        </label>

        <fieldset
          className="sm:col-span-2 flex flex-col gap-2 rounded-xl border border-[#DFE5ED] p-3 dark:border-white/10"
          data-testid="simulador-transformaciones"
        >
          <legend className="px-1 text-xs font-semibold text-[#162244] dark:text-white">
            Transformaciones
          </legend>
          <div className="grid gap-2 sm:grid-cols-2">
            <label className="flex items-center gap-2 text-sm text-[#162244] dark:text-white">
              <input
                type="checkbox"
                checked={cambioColor}
                disabled={busy || inferredLocks.lockColor}
                onChange={(e) => setCambioColor(e.target.checked)}
                data-testid="simulador-cambio-color"
                className="h-4 w-4 rounded border-[#DFE5ED] disabled:cursor-not-allowed disabled:opacity-60"
              />
              Cambio de color
            </label>
            <label className="flex items-center gap-2 text-sm text-[#162244] dark:text-white">
              <input
                type="checkbox"
                checked={cambioCombustible}
                disabled={busy || inferredLocks.lockCombustible}
                onChange={(e) => setCambioCombustible(e.target.checked)}
                data-testid="simulador-cambio-combustible"
                className="h-4 w-4 rounded border-[#DFE5ED] disabled:cursor-not-allowed disabled:opacity-60"
              />
              Cambio de combustible
            </label>
            <label className="flex items-center gap-2 text-sm text-[#162244] dark:text-white">
              <input
                type="checkbox"
                checked={cambioCarroceria}
                disabled={busy || inferredLocks.lockCarroceria}
                onChange={(e) => setCambioCarroceria(e.target.checked)}
                data-testid="simulador-cambio-carroceria"
                className="h-4 w-4 rounded border-[#DFE5ED] disabled:cursor-not-allowed disabled:opacity-60"
              />
              Cambio de carrocería
            </label>
            <label className="flex items-center gap-2 text-sm text-[#162244] dark:text-white">
              <input
                type="checkbox"
                checked={blindaje}
                disabled={busy || inferredLocks.lockBlindaje}
                onChange={(e) => setBlindaje(e.target.checked)}
                data-testid="simulador-blindaje"
                className="h-4 w-4 rounded border-[#DFE5ED] disabled:cursor-not-allowed disabled:opacity-60"
              />
              Blindaje
            </label>
          </div>
        </fieldset>

        <label className="block space-y-1.5">
          <span className="text-xs font-semibold text-[#162244] dark:text-white">
            Tipo de mandatario
          </span>
          <select
            value={tipo}
            onChange={(e) => setTipo(e.target.value as MandatoTipoNegocio | "config")}
            disabled={busy}
            data-testid="simulador-tipo"
            className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
          >
            <option value="config">El configurado para el organismo</option>
            {MANDATO_TIPOS.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
        </label>

        <label className="block space-y-1.5 sm:col-span-2">
          <span className="text-xs font-semibold text-[#162244] dark:text-white">
            Mandatario que firma
          </span>
          <select
            value={signerId}
            onChange={(e) => setSignerId(e.target.value)}
            disabled={busy || soloMandante || signers.length === 0}
            data-testid="simulador-mandatario"
            className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
          >
            <option value="">
              {soloMandante ? "No aplica para este tipo" : "Sin asignar (datos de muestra)"}
            </option>
            {signers.map((s) => (
              <option key={s.id} value={s.id}>
                {s.fullName} · {s.documentNumber}
                {s.tieneFirmaEnBaul ? " · firma en baúl" : s.identityVigente ? " · identidad vigente" : ""}
              </option>
            ))}
          </select>
          {officeId && signersStatus === "ready" && signers.length === 0 ? (
            <span
              className="block text-[11px] text-[#59677D] dark:text-white/65"
              data-testid="simulador-sin-mandatarios"
            >
              Este organismo no tiene mandatarios habilitados. Puedes simular igual: el mandatario
              saldrá con datos de muestra.
            </span>
          ) : null}
          {signersStatus === "error" ? (
            <span className="block text-[11px] text-[#FF4E00]">
              No se pudieron cargar los mandatarios de este organismo.
            </span>
          ) : null}
        </label>
      </div>

      <div className="flex flex-wrap items-end gap-2">
        {MOSTRAR_ENVIO_POR_CORREO ? (
          <>
            <label className="min-w-[14rem] flex-1 space-y-1.5">
              <span className="text-xs font-semibold text-[#162244] dark:text-white">Enviar a</span>
              <input
                type="email"
                value={toEmail}
                onChange={(e) => setToEmail(e.target.value)}
                disabled={busy}
                placeholder="correo@empresa.com"
                data-testid="simulador-correo"
                className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] placeholder:text-[#59677D]/70 disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
              />
            </label>
            <button
              type="button"
              disabled={busy || !officeId || !procedureTypeCode}
              onClick={() => void handleSend()}
              className="inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
              style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
            >
              <Mail className="h-3.5 w-3.5" aria-hidden="true" />
              {sending ? "Enviando…" : "Enviar por correo"}
            </button>
          </>
        ) : null}

        <button
          type="button"
          disabled={busy || !officeId || !procedureTypeCode}
          onClick={() => void handlePreview()}
          className="ml-auto inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
        >
          <Eye className="h-3.5 w-3.5" aria-hidden="true" />
          {previewing ? "Generando…" : "Ver PDF"}
        </button>
      </div>
    </section>
  );
}

/** Mensaje del backend cuando lo trae; nunca la ruta ni el status crudos. */
function messageFrom(err: unknown, fallback: string): string {
  if (!(err instanceof ApiError)) return fallback;
  const body = err.body as { message?: unknown } | null;
  const message = typeof body?.message === "string" ? body.message.trim() : "";
  return message || err.message || fallback;
}
