"use client";

import { useEffect, useRef, useState } from "react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Modal } from "@flit/ui/Modal";
import { BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS } from "@/components/configuracion/estilos";
import { CamposOverrideDictamen } from "@/components/backoffice/CamposOverrideDictamen";
import { useFocoAtrapado } from "@/components/tablero/DetalleCelda";
import { Etiqueta, TEXTO_SECUNDARIO_CLS, Vacio } from "@/components/tablero/vistasDetalle/comun";
import {
  corregirDictamen,
  esSinDictamen,
  obtenerDictamen,
  obtenerDictamenBackoffice,
  rotuloAlerta,
  rotuloEfecto,
  rotuloFuenteAlerta,
  type Dictamen,
} from "@/lib/dictamen/api";

export interface PanelDictamenProps {
  diagnosticoId: string;
  vehiculoId: string;
  /** Placa, VIN o identificador, solo para el título. No viaja en el evento. */
  identificador: string;
  modo: "compania" | "backoffice";
  /** Compañía explícita del backoffice. La compañía del token no la usa. */
  empresa?: string;
  /** Capacidad `corregirDictamen`. En la compañía no se pinta aunque llegue en true. */
  puedeCorregir: boolean;
  onClose: () => void;
  /** Botón que abrió el panel: recibe el foco al cerrarlo. */
  devolverFocoA?: HTMLElement | null;
}

type Carga = { tipo: "cargando" } | { tipo: "vacio" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; dictamen: Dictamen };

const BOTON_DICTAMEN_CLS = BOTON_SECUNDARIO_CLS.replace("px-4 py-2 text-sm", "px-3 py-1 text-xs");

/** «Dictamen» bajo la placa. Al abrirlo el panel pide el GET; no entra en el sondeo de la grilla. */
export function BotonAbrirDictamen({ identificador, onAbrir }: { identificador: string; onAbrir: (disparador: HTMLElement) => void }) {
  return (
    <button type="button" className={BOTON_DICTAMEN_CLS} aria-label={`Ver dictamen de ${identificador}`} onClick={(e) => onAbrir(e.currentTarget)}>
      Dictamen
    </button>
  );
}

const nombreVeredicto = (veredicto: Dictamen["veredicto"]) => (veredicto === "BLOQUEADO" ? "Bloqueado" : "Viable");

/**
 * Panel del dictamen vigente (corte 7), el mismo en el tablero del lote y en la grilla del backoffice. No es una ruta
 * y no entra en el sondeo: al abrirse pide el GET. Cuatro estados: vacío (SIN_DICTAMEN), cargando, error con Reintentar
 * y lleno. En lleno se ven el veredicto (con texto, no solo color) y cada alertamiento en el orden del arreglo, con su rótulo
 * y el nombre de la fuente, nunca el código (Punto 5). Si la
 * redacción está pendiente, se lee «redacción IA pendiente». El override solo se ofrece en el backoffice con la capacidad
 * (sus campos viven en components/backoffice/CamposOverrideDictamen).
 */
export function PanelDictamen({
  diagnosticoId,
  vehiculoId,
  identificador,
  modo,
  empresa,
  puedeCorregir,
  onClose,
  devolverFocoA,
}: PanelDictamenProps) {
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [elegido, setElegido] = useState<Dictamen["veredicto"] | null>(null);
  const [confirmado, setConfirmado] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);
  const ancla = useRef<HTMLDivElement>(null);
  useFocoAtrapado(ancla, devolverFocoA);
  const ofreceOverride = modo === "backoffice" && puedeCorregir;

  useEffect(() => {
    const control = new AbortController();
    // La compañía inválida del backoffice lanza antes del fetch: el then lo convierte en rechazo, fuera del cuerpo del efecto.
    const pedido = Promise.resolve().then(() =>
      modo === "backoffice"
        ? obtenerDictamenBackoffice(diagnosticoId, vehiculoId, empresa ?? "", control.signal)
        : obtenerDictamen(diagnosticoId, vehiculoId, control.signal),
    );
    pedido.then(
      (dictamen) => {
        if (!control.signal.aborted) setCarga({ tipo: "listo", dictamen });
      },
      (error: unknown) => {
        if (control.signal.aborted) return;
        if (esSinDictamen(error)) setCarga({ tipo: "vacio" });
        else setCarga({ tipo: "error", mensaje: error instanceof Error ? error.message : "No se pudo cargar el dictamen." });
      },
    );
    return () => control.abort();
  }, [diagnosticoId, vehiculoId, empresa, modo, intento]);

  async function reemplazar() {
    if (!ofreceOverride || !elegido || !confirmado || enviando || !empresa) return;
    setEnviando(true);
    setAviso(null);
    try {
      const dictamen = await corregirDictamen(diagnosticoId, vehiculoId, empresa, elegido);
      setCarga({ tipo: "listo", dictamen });
      setElegido(null);
      setConfirmado(false);
    } catch (error) {
      setAviso(error instanceof Error ? error.message : "No se pudo reemplazar el veredicto.");
    } finally {
      setEnviando(false);
    }
  }

  let contenido;
  if (carga.tipo === "cargando") {
    contenido = (
      <p role="status" aria-label="Cargando el dictamen" className={TEXTO_SECUNDARIO_CLS}>
        Cargando el dictamen…
      </p>
    );
  } else if (carga.tipo === "vacio") {
    contenido = <Vacio>Este vehículo no tiene un dictamen vigente.</Vacio>;
  } else if (carga.tipo === "error") {
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudo cargar el dictamen"
        action={
          <button type="button" onClick={() => { setCarga({ tipo: "cargando" }); setIntento((n) => n + 1); }} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {carga.mensaje}
      </InlineAlert>
    );
  } else {
    const { dictamen } = carga;
    contenido = (
      <div className="flex flex-col gap-4">
        <p className="text-sm text-flit-primary">
          Veredicto:{" "}
          <Etiqueta tono={dictamen.veredicto === "BLOQUEADO" ? "danger" : "success"}>{nombreVeredicto(dictamen.veredicto)}</Etiqueta>
        </p>
        {dictamen.estado_redaccion === "PENDIENTE" && <p className="text-sm text-flit-primary">redacción IA pendiente</p>}
        <ol className="flex flex-col gap-3">
          {dictamen.alertamientos.map((alerta, i) => (
            <li key={`${i}-${alerta.codigo}`} className="rounded-xl border border-border p-3">
              <p className="text-sm font-semibold text-flit-primary">{rotuloAlerta(alerta)}</p>
              <p className={`mt-1 ${TEXTO_SECUNDARIO_CLS}`}>{`Efecto: ${rotuloEfecto(alerta)}`}</p>
              <p className={TEXTO_SECUNDARIO_CLS}>{`Fuente: ${rotuloFuenteAlerta(alerta)}`}</p>
              <p className="mt-1 text-sm text-flit-primary">{alerta.texto}</p>
            </li>
          ))}
        </ol>
        {ofreceOverride && (
          <CamposOverrideDictamen
            elegido={elegido}
            onElegir={setElegido}
            confirmado={confirmado}
            onConfirmar={setConfirmado}
            aviso={aviso}
          />
        )}
      </div>
    );
  }

  return (
    <Modal icono="mazo"
      open
      onClose={onClose}
      busy={enviando}
      size="lg"
      zClassName="z-[110]"
      title={`Dictamen · ${identificador}`}
      footer={
        ofreceOverride && carga.tipo === "listo" ? (
          <div className="flex justify-end">
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!elegido || !confirmado || enviando} onClick={() => void reemplazar()}>
              Reemplazar veredicto
            </button>
          </div>
        ) : null
      }
    >
      <div ref={ancla}>{contenido}</div>
    </Modal>
  );
}
