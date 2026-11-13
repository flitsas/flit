"use client";

import { useEffect, useState } from "react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { BOTON_REINTENTAR_CLS } from "@/components/configuracion/estilos";
import { fechaDelDato } from "@/components/tablero/CeldaSemaforo";
import { Etiqueta, TEXTO_SECUNDARIO_CLS } from "@/components/tablero/vistasDetalle/comun";
import { obtenerVersionesDictamen, type AlertamientoVersion, type VersionDictamen } from "@/lib/backoffice/api";
import { CODIGO_AJUSTE_FLIT } from "@/lib/backoffice/ajusteDictamen";
import { rotuloAlerta } from "@/lib/dictamen/api";
import { EFECTOS_DICTAMEN } from "@/lib/rotulos";

export interface AuditoriaDictamenProps {
  diagnosticoId: string;
  vehiculoId: string;
  empresa: string;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo"; versiones: VersionDictamen[] };

const ORIGEN: Record<VersionDictamen["origen"], string> = { AUTOMATICO: "Automático", AJUSTE_FLIT: "Ajuste FLIT" };
const VEREDICTO: Record<VersionDictamen["veredicto"], string> = { VIABLE: "Viable", BLOQUEADO: "Bloqueado" };
const AJUSTE: Record<"AGREGADO" | "EDITADO", string> = { AGREGADO: "Agregado", EDITADO: "Editado" };

const tituloAlerta = (a: AlertamientoVersion) => (a.codigo === CODIGO_AJUSTE_FLIT ? "Alertamiento libre" : rotuloAlerta({ ...a, etiqueta: null }));

function ListaAlertas({ etiqueta, alertas, conAjuste }: { etiqueta: string; alertas: AlertamientoVersion[]; conAjuste: boolean }) {
  return (
    <ul aria-label={etiqueta} className="flex flex-col gap-1.5">
      {alertas.map((a, i) => (
        <li key={`${a.codigo}-${i}`} className="flex flex-col gap-0.5 rounded-[9px] border border-[color:var(--di-border)] px-3 py-2 text-sm">
          <span className="flex flex-wrap items-center gap-2 font-semibold text-[color:var(--di-text-primary)]">
            {tituloAlerta(a)}
            <Etiqueta tono={a.efecto === "BLOQUEA" ? "danger" : "warning"}>{EFECTOS_DICTAMEN[a.efecto] ?? a.efecto}</Etiqueta>
            {conAjuste && a.ajuste && <Etiqueta tono="info">{AJUSTE[a.ajuste]}</Etiqueta>}
          </span>
          <span className="text-[color:var(--di-text-secondary)]">{a.texto}</span>
        </li>
      ))}
    </ul>
  );
}

/**
 * Auditoría del dictamen de un vehículo (HU #14562 · AC47, D6/D7). Solo existe en el backoffice: ninguna pantalla de la
 * compañía muestra autor, motivo ni versiones anteriores. Versiones de la más nueva a la más vieja, con origen, usuario,
 * motivo, fecha, alertamientos agregados o editados, eliminados y la marca «Superado por recálculo». Cuatro estados.
 */
export function AuditoriaDictamen({ diagnosticoId, vehiculoId, empresa }: AuditoriaDictamenProps) {
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);

  useEffect(() => {
    const control = new AbortController();
    obtenerVersionesDictamen(diagnosticoId, vehiculoId, empresa, control.signal).then(
      (versiones) => {
        if (!control.signal.aborted) setCarga({ tipo: "listo", versiones });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error });
      },
    );
    return () => control.abort();
  }, [diagnosticoId, vehiculoId, empresa, intento]);

  if (carga.tipo === "cargando") return <VehicleLoader label="Cargando la auditoría del dictamen…" />;
  if (carga.tipo === "error")
    return (
      <InlineAlert
        tone="error"
        title="No se pudo cargar la auditoría"
        action={
          <button
            type="button"
            onClick={() => {
              setCarga({ tipo: "cargando" });
              setIntento((n) => n + 1);
            }}
            className={BOTON_REINTENTAR_CLS}
          >
            Reintentar
          </button>
        }
      >
        {carga.error instanceof Error ? carga.error.message : "Intenta de nuevo en un momento."}
      </InlineAlert>
    );
  if (carga.versiones.length === 0) return <p className={TEXTO_SECUNDARIO_CLS}>Este vehículo aún no tiene dictamen.</p>;

  return (
    <ol aria-label="Versiones del dictamen" className="flex flex-col gap-3">
      {carga.versiones.map((v) => (
        <li
          key={v.version}
          aria-label={`Versión ${v.version}`}
          className="flex flex-col gap-2 rounded-[var(--di-radius-modal)] border border-[color:var(--di-border)] bg-[var(--di-surface)] p-4"
        >
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-semibold text-[color:var(--di-heading)]">{`Versión ${v.version}`}</span>
            <Etiqueta tono={v.origen === "AJUSTE_FLIT" ? "info" : "neutral"}>{ORIGEN[v.origen]}</Etiqueta>
            <Etiqueta tono={v.veredicto === "VIABLE" ? "success" : "danger"}>{VEREDICTO[v.veredicto]}</Etiqueta>
            {v.superado_por_recalculo && <Etiqueta tono="warning">Superado por recálculo</Etiqueta>}
          </div>
          <dl className="grid grid-cols-[7rem_1fr] gap-x-3 gap-y-1 text-sm">
            <dt className={TEXTO_SECUNDARIO_CLS}>Fecha</dt>
            <dd className="tabular-nums">
              <time dateTime={v.en}>{fechaDelDato(v.en)}</time>
            </dd>
            {v.origen === "AJUSTE_FLIT" && (
              <>
                <dt className={TEXTO_SECUNDARIO_CLS}>Usuario</dt>
                <dd>{v.usuario?.nombre?.trim() || "Usuario no disponible"}</dd>
                <dt className={TEXTO_SECUNDARIO_CLS}>Motivo</dt>
                <dd className="break-words">{v.motivo?.trim() || "Sin motivo registrado"}</dd>
              </>
            )}
          </dl>
          {v.alertamientos.length > 0 && (
            <ListaAlertas etiqueta={`Alertamientos de la versión ${v.version}`} alertas={v.alertamientos} conAjuste={v.origen === "AJUSTE_FLIT"} />
          )}
          {(v.eliminados?.length ?? 0) > 0 && (
            <div className="flex flex-col gap-1">
              <p className="text-xs font-semibold text-[color:var(--di-text-secondary)]">Eliminados</p>
              <ListaAlertas etiqueta={`Eliminados en la versión ${v.version}`} alertas={v.eliminados ?? []} conAjuste={false} />
            </div>
          )}
        </li>
      ))}
    </ol>
  );
}
