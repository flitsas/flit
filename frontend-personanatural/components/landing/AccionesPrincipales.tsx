"use client";

import { useState } from "react";
import type { DatosInicio } from "@/components/modales/IniciarModal";
import type { ClientePortal } from "@/lib/api/cliente";
import { mensajeDe } from "@/lib/api/errores";
import { ASSETS } from "@/lib/assets";
import { cedulaValida, normalizarVin, soloDigitos } from "@/lib/entradas";
import { tiposDePuerta } from "@/lib/landing/puerta-iniciar";
import { datosVehiculo, ETIQUETA_VEHICULO, MARCADOR_VEHICULO, modoVehiculo, normalizarVehiculo, vehiculoValido } from "@/lib/landing/vehiculo";
import { boton } from "./estilos";
import { SelectorTipoTramite } from "./SelectorTipoTramite";
import { useDefinicion, type useTiposTramite } from "./useCatalogos";

type Tipos = ReturnType<typeof useTiposTramite>;

function IconoAccion({ src }: { src: string }) {
  return (
    <span aria-hidden className="action-top-icon">
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img src={src} alt="" />
    </span>
  );
}

interface Props {
  tipos: Tipos;
  cliente: ClientePortal;
  onIniciar?: (datos: DatosInicio) => void;
  onConsultar?: () => void;
  onCotizar?: () => void;
}

/**
 * «Iniciar mi trámite», «Consultar mi trámite» y «Cotizar mi trámite». Lo que el ciudadano escribe vive solo en el estado
 * de la pantalla (nunca en almacenamiento del navegador ni en la URL) y se conserva mientras no recargue la página, incluso
 * si cierra el modal con «Iniciar más tarde». Un control sin acción conectada queda deshabilitado y lo dice.
 */
export function AccionesPrincipales({ tipos, cliente, onIniciar, onConsultar, onCotizar }: Props) {
  const [cedula, setCedula] = useState("");
  const [vehiculo, setVehiculo] = useState("");
  const [tipo, setTipo] = useState("");
  const opciones = tiposDePuerta(tipos.data ?? []);
  const sinTipos = tipos.isSuccess && opciones.length === 0;
  // La definición del tipo dice si el vehículo se identifica por placa, VIN o ambos; mientras no responda, es la placa.
  const definicion = useDefinicion(cliente, tipo);
  const modo = modoVehiculo(definicion.data?.flags.entryMode);
  // Se guarda lo escrito (alfanumérico en mayúsculas) y se muestra y valida con el formato del modo vigente: si el modo
  // cambia cuando llega la definición del tipo, no se pierde lo que el ciudadano ya escribió.
  const identificacion = normalizarVehiculo(modo, vehiculo);
  const listo = cedulaValida(cedula) && vehiculoValido(modo, identificacion) && tipo !== "";

  return (
    <section className="dashboard-actions-stack" aria-label="Acciones principales">
      <div className="dashboard-institutional-card action-start" id="nuevo-tramite">
        <IconoAccion src={ASSETS.acciones.iniciar} />
        <h2 className="dashboard-title">Iniciar mi trámite</h2>
        <p>Da clic aquí para iniciar el proceso de tu trámite digital.</p>
        <form
          className="action-start-fields"
          onSubmit={(e) => {
            e.preventDefault();
            if (listo && onIniciar) onIniciar({ tipoCodigo: tipo, cedula, tipoDocumento: "CC", ...datosVehiculo(modo, identificacion) });
          }}
        >
          <label>
            Número de Cédula
            <input required className="field" inputMode="numeric" autoComplete="off" placeholder="Ej. 1020456789" value={cedula} onChange={(e) => setCedula(soloDigitos(e.target.value))} />
          </label>
          <label>
            {ETIQUETA_VEHICULO[modo]}
            <input
              required
              className="field uppercase"
              autoComplete="off"
              placeholder={MARCADOR_VEHICULO[modo]}
              maxLength={modo === "PLATE" ? 6 : 30}
              value={identificacion}
              onChange={(e) => setVehiculo(normalizarVin(e.target.value))}
            />
          </label>
          <div className="dashboard-label" aria-busy={tipos.isPending}>
            Tipo de Trámite
            <SelectorTipoTramite
              opciones={opciones}
              valor={tipo}
              onChange={setTipo}
              disabled={!tipos.isSuccess || sinTipos}
              marcador={tipos.isPending ? "Cargando..." : "Selecciona"}
            />
            <div aria-live="polite" className="text-xs font-normal">
              {tipos.isError && (
                <p role="alert">
                  {mensajeDe(tipos.error)}{" "}
                  <button type="button" className={boton("link", "h-auto p-0 text-xs", "none")} onClick={() => void tipos.refetch()}>Reintentar</button>
                </p>
              )}
              {sinTipos && <p>Por ahora no hay trámites disponibles en el portal.</p>}
            </div>
          </div>
          <button type="submit" aria-haspopup="dialog" className={boton("institutional", "dashboard-submit", "none")} disabled={!listo || !onIniciar}>Iniciar trámite</button>
        </form>
      </div>
      <div className="action-rows">
        <AccionFila etiqueta="Consultar mi trámite" src={ASSETS.acciones.consultar} onClick={onConsultar} />
        <AccionFila etiqueta="Cotizar mi trámite" src={ASSETS.acciones.cotizar} onClick={onCotizar} />
      </div>
    </section>
  );
}

/** Fila clicable del diseño. Sin acción conectada queda deshabilitada y lo dice. */
function AccionFila({ etiqueta, src, onClick }: { etiqueta: string; src: string; onClick?: () => void }) {
  if (onClick) {
    return (
      <button type="button" aria-label={etiqueta} aria-haspopup="dialog" onClick={onClick} className="dashboard-institutional-card action-row action-clickable">
        <IconoAccion src={src} />
        <span className="dashboard-title">{etiqueta}</span>
      </button>
    );
  }
  return (
    <button type="button" disabled aria-label={`${etiqueta} (próximamente)`} className="dashboard-institutional-card action-row action-clickable disabled:cursor-not-allowed disabled:opacity-70">
      <IconoAccion src={src} />
      <span className="dashboard-title">{etiqueta}</span>
      <span className="text-xs font-normal">Próximamente</span>
    </button>
  );
}
