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
import { ApiError } from "@/lib/api/types";
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

/** Tipologías simulables: lo que cambia es el objeto del contrato. */
const TIPOLOGIAS = [
  { value: "traspaso_standard", label: "Traspaso de propiedad" },
  { value: "matricula_inicial", label: "Matrícula inicial" },
] as const;

export interface MandatoSimuladorPanelProps {
  /** Organismos disponibles, ya cargados por el catálogo (no se vuelven a pedir). */
  offices: MandateOtConfigView[];
}

/**
 * Simulador de mandatos (HU #11707): muestra el contrato que emitiría un organismo según el tipo de
 * persona del mandante y el trámite.
 *
 * <p>El mandante, la placa y el trámite se rellenan con <b>datos de muestra</b> generados en el
 * momento —no se le piden al usuario—, porque lo que se juzga aquí es la redacción, no los datos.</p>
 */
export function MandatoSimuladorPanel({ offices }: MandatoSimuladorPanelProps) {
  const { show: showToast } = useToast();

  const [officeId, setOfficeId] = useState("");
  const [personType, setPersonType] = useState<"natural" | "juridica">("juridica");
  const [tipologia, setTipologia] = useState<string>("traspaso_standard");
  const [tipo, setTipo] = useState<MandatoTipoNegocio | "config">("config");
  const [signerId, setSignerId] = useState("");
  const [toEmail, setToEmail] = useState("");

  const [signers, setSigners] = useState<MandateSimulatorSignerOption[]>([]);
  const [signersStatus, setSignersStatus] = useState<"idle" | "loading" | "ready" | "error">("idle");
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

  const body = () => ({
    officeId,
    personType,
    tipologia,
    // "config" ⇒ no se manda modo y el backend aplica el configurado para el organismo.
    assignmentMode: tipo === "config" ? null : resolveAssignmentMode(tipo),
    mandateSignerId: signerId || null,
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
          trámite. El mandante, la placa y el trámite se rellenan con datos de muestra: no
          corresponde a ningún trámite real.
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
          <span className="text-xs font-semibold text-[#162244] dark:text-white">Trámite</span>
          <select
            value={tipologia}
            onChange={(e) => setTipologia(e.target.value)}
            disabled={busy}
            data-testid="simulador-tramite"
            className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
          >
            {TIPOLOGIAS.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
        </label>

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
              disabled={busy || !officeId}
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
          disabled={busy || !officeId}
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
