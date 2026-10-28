"use client";

// Épica #14021 — Encabezado guiado de Plataforma > Persona Natural: dice en una frase qué es esta pantalla, muestra los
// tres pasos que hacen falta para que el portal ofrezca un trámite (hecho o pendiente, con un botón directo al paso) y
// resume en lenguaje de negocio lo que ve hoy el ciudadano. Los números salen de las mismas listas que usan las
// pestañas (tipos y organismos); no se inventa ningún dato.
import { useCallback, useEffect, useState } from "react";
import { ArrowRight, Check, Eye } from "lucide-react";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { fetchOrganismosPn, fetchTiposPn, type OrganismoPn, type TipoPn } from "@/lib/api/admin-pn-config";
import { PnButton } from "./pn-componentes";

export type PasoConfiguracion = "tipos" | "organismos" | "tarifas";

export interface ResumenConfiguracion {
  /** Tipos de trámite activados y listos (elegibles + habilitados). */
  tiposActivos: number;
  /** De esos, los que tienen al menos un organismo con tarifa vigente. */
  tiposConTarifa: number;
  organismosHabilitados: number;
  /** Organismos habilitados que no tienen ningún tipo con tarifa vigente. */
  organismosSinTarifa: number;
  /** Organismos habilitados con al menos un tipo con tarifa vigente. */
  organismosConTarifa: number;
}

export function calcularResumen(tipos: readonly TipoPn[], organismos: readonly OrganismoPn[]): ResumenConfiguracion {
  const activos = tipos.filter((t) => t.ofrecible);
  const habilitados = organismos.filter((o) => o.habilitado);
  return {
    tiposActivos: activos.length,
    tiposConTarifa: activos.filter((t) => t.organismosConTarifaVigente > 0).length,
    organismosHabilitados: habilitados.length,
    organismosSinTarifa: habilitados.filter((o) => o.tiposConTarifaVigente === 0).length,
    organismosConTarifa: habilitados.filter((o) => o.tiposConTarifaVigente > 0).length,
  };
}

export function usePnResumen() {
  const [resumen, setResumen] = useState<ResumenConfiguracion | null>(null);
  const [error, setError] = useState(false);

  const refrescar = useCallback(async (signal?: AbortSignal) => {
    try {
      const [tipos, organismos] = await Promise.all([fetchTiposPn(signal), fetchOrganismosPn(signal)]);
      if (signal?.aborted) return;
      setResumen(calcularResumen(tipos, organismos));
      setError(false);
    } catch {
      if (!signal?.aborted) setError(true);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial del resumen
    void refrescar(controller.signal);
    return () => controller.abort();
  }, [refrescar]);

  return { resumen, error, refrescar: () => refrescar() };
}

interface Paso {
  id: PasoConfiguracion;
  titulo: string;
  hecho: boolean;
  detalle: string;
  boton: string;
}

function plural(n: number, uno: string, varios: string): string {
  return `${n} ${n === 1 ? uno : varios}`;
}

function pasosDe(r: ResumenConfiguracion): Paso[] {
  return [
    {
      id: "tipos",
      titulo: "Un tipo de trámite",
      hecho: r.tiposActivos > 0,
      detalle:
        r.tiposActivos > 0
          ? `${plural(r.tiposActivos, "trámite activado", "trámites activados")} en el portal.`
          : "Aún no has activado ningún trámite.",
      boton: "Elegir trámites",
    },
    {
      id: "organismos",
      titulo: "Un organismo de tránsito",
      hecho: r.organismosHabilitados > 0,
      detalle:
        r.organismosHabilitados > 0
          ? `${plural(r.organismosHabilitados, "organismo habilitado", "organismos habilitados")}.`
          : "Aún no has habilitado ningún organismo.",
      boton: "Elegir organismos",
    },
    {
      id: "tarifas",
      titulo: "Una tarifa vigente",
      hecho: r.tiposConTarifa > 0,
      detalle:
        r.tiposConTarifa > 0
          ? `${plural(r.tiposConTarifa, "trámite", "trámites")} con tarifa vigente.`
          : "Ningún trámite activado tiene tarifa vigente.",
      boton: "Configurar tarifas",
    },
  ];
}

function frasePortal(r: ResumenConfiguracion, pasos: Paso[]): string {
  if (pasos.every((p) => p.hecho)) {
    return `Hoy el ciudadano puede iniciar ${plural(r.tiposConTarifa, "tipo de trámite", "tipos de trámite")} en ${plural(
      r.organismosConTarifa,
      "organismo de tránsito",
      "organismos de tránsito",
    )}.`;
  }
  const faltan = pasos.filter((p) => !p.hecho).map((p) => p.titulo.toLowerCase());
  return `Hoy el ciudadano todavía no puede iniciar ningún trámite. Falta: ${faltan.join(", ")}.`;
}

export function PnEstadoConfiguracion({
  resumen,
  error,
  onReintentar,
  onIr,
}: {
  resumen: ResumenConfiguracion | null;
  error: boolean;
  onReintentar: () => void;
  onIr: (paso: PasoConfiguracion) => void;
}) {
  const pasos = resumen ? pasosDe(resumen) : null;

  return (
    <section
      aria-labelledby="pn-estado-titulo"
      className="flex flex-col gap-4 rounded-[18px] border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#162744]"
    >
      <div>
        <h2 id="pn-estado-titulo" className="text-base font-bold text-flit-primary dark:text-white">
          Estado de la configuración
        </h2>
        <p className="text-sm text-[#59677D] dark:text-white/78">
          Para que el portal ofrezca un trámite al ciudadano necesitas tres cosas. Cada paso te lleva a donde se resuelve.
        </p>
      </div>

      {error && !resumen && (
        <InlineAlert
          tone="error"
          action={
            <PnButton variant="secondary" onClick={onReintentar}>
              Reintentar
            </PnButton>
          }
        >
          No se pudo consultar el estado de la configuración.
        </InlineAlert>
      )}

      {!resumen && !error && (
        <p role="status" className="text-sm text-[#59677D] dark:text-white/78">
          Consultando el estado de la configuración…
        </p>
      )}

      {resumen && pasos && (
        <>
          <ol className="grid gap-3 md:grid-cols-3" aria-label="Pasos de la configuración">
            {pasos.map((p, i) => (
              <li
                key={p.id}
                className="flex flex-col gap-2 rounded-xl border border-[#DFE5ED] p-3 dark:border-white/15"
              >
                <div className="flex items-center gap-2">
                  <span
                    aria-hidden="true"
                    className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-sm font-bold ${
                      p.hecho ? "bg-[#8CC63F] text-white" : "border-2 border-[#DFE5ED] text-[#59677D]"
                    }`}
                  >
                    {p.hecho ? <Check className="h-4 w-4" /> : i + 1}
                  </span>
                  <span className="text-sm font-bold text-flit-primary dark:text-white">
                    <span className="sr-only">Paso {i + 1}: </span>
                    {p.titulo}
                  </span>
                  <span className={`ml-auto text-xs font-semibold ${p.hecho ? "text-[#4F7A12]" : "text-[#B45309]"}`}>
                    {p.hecho ? "Hecho" : "Pendiente"}
                  </span>
                </div>
                <p className="text-sm text-[#59677D] dark:text-white/78">{p.detalle}</p>
                <div className="mt-auto">
                  <PnButton variant={p.hecho ? "secondary" : "primary"} icon={ArrowRight} onClick={() => onIr(p.id)}>
                    {p.boton}
                  </PnButton>
                </div>
              </li>
            ))}
          </ol>

          <div className="flex flex-col gap-2 rounded-xl bg-[#EEF5FF] p-3 dark:bg-white/5">
            <p className="flex items-start gap-2 text-sm font-semibold text-flit-primary dark:text-white">
              <Eye className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <span>{frasePortal(resumen, pasos)}</span>
            </p>
            {resumen.organismosSinTarifa > 0 && (
              <InlineAlert tone="warning" compact>
                {plural(
                  resumen.organismosSinTarifa,
                  "organismo habilitado no tiene",
                  "organismos habilitados no tienen",
                )}{" "}
                tarifa vigente: el ciudadano no podrá cotizar ahí.
              </InlineAlert>
            )}
          </div>
        </>
      )}
    </section>
  );
}
