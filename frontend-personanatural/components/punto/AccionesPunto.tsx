"use client";

// HU #14220 / #14222 — Acciones del punto con el mismo layout del portal (AccionesPrincipales), sin Cotizar.
import { useState } from "react";
import { AccionFila } from "@/components/landing/AccionesPrincipales";
import { boton } from "@/components/landing/estilos";
import { SelectorTipoTramite } from "@/components/landing/SelectorTipoTramite";
import { MobilityLoader } from "@/components/MobilityLoader";
import { textoArranque, type ClientePortal, type TipoTramite } from "@/lib/api/cliente";
import { mensajeDeFallo } from "@/lib/api/errores";
import { ASSETS } from "@/lib/assets";
import { normalizarPlaca, soloDigitos } from "@/lib/entradas";
import { tiposDelPunto } from "@/lib/landing/puerta-iniciar";
import { MENSAJE_CONSULTANDO_VEHICULO } from "@/lib/tramite/diagnostico";

function IconoAccion({ src }: { src: string }) {
  return (
    <span aria-hidden className="action-top-icon">
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img src={src} alt="" />
    </span>
  );
}

const OPCIONES: readonly TipoTramite[] = tiposDelPunto.map((t) => ({
  code: t.codigo,
  name: t.nombre,
  family: t.codigo.startsWith("TRASPASO") ? "TRASPASO" : "OTROS",
  version: 1,
  disponible: true,
}));

export function AccionesPunto({
  cliente,
  onConsultar,
  onAviso,
}: {
  cliente: ClientePortal;
  onConsultar: () => void;
  /** Fallas y reglas de negocio: mismo AvisoModal del portal. */
  onAviso: (titulo: string, mensaje: string) => void;
}) {
  const [placa, setPlaca] = useState("");
  const [documento, setDocumento] = useState("");
  const [tipo, setTipo] = useState("");
  const [fase, setFase] = useState<"formulario" | "cargando" | "diagnostico">("formulario");

  const listo = placa !== "" && documento !== "" && tipo !== "";

  async function arrancar() {
    if (!listo) return;
    setFase("cargando");
    try {
      const resultado = await cliente.punto.arranque({ placa, documento, tipoCodigo: tipo });
      if (resultado.puedeSeguir) {
        setFase("diagnostico");
        return;
      }
      onAviso("No pudimos continuar", textoArranque(resultado) || "No pudimos iniciar el trámite. Inténtalo de nuevo.");
      setFase("formulario");
    } catch (err) {
      onAviso("No pudimos continuar", mensajeDeFallo(err));
      setFase("formulario");
    }
  }

  if (fase === "diagnostico") {
    return (
      <section className="dashboard-actions-stack" aria-label="Acciones del punto">
        <div className="dashboard-institutional-card action-start" role="region" aria-labelledby="punto-diagnostico">
          <IconoAccion src={ASSETS.acciones.iniciar} />
          <h2 id="punto-diagnostico" className="dashboard-title">
            Puedes continuar al diagnóstico
          </h2>
          <p>El vehículo quedó listo en este punto para seguir con el diagnóstico.</p>
          <button type="button" className={boton("institutional", "dashboard-submit", "none")} onClick={() => setFase("formulario")}>
            Volver
          </button>
        </div>
        <FilaConsultar onConsultar={onConsultar} />
      </section>
    );
  }

  return (
    <section className="dashboard-actions-stack" aria-label="Acciones del punto">
      <div className="dashboard-institutional-card action-start" id="nuevo-tramite">
        <IconoAccion src={ASSETS.acciones.iniciar} />
        <h2 className="dashboard-title">Iniciar mi trámite</h2>
        <p>Placa, documento y tipo de trámite. No se pide contraseña.</p>
        <form
          className="action-start-fields"
          aria-busy={fase === "cargando"}
          onSubmit={(e) => {
            e.preventDefault();
            void arrancar();
          }}
        >
          <label>
            Placa
            <input
              required
              className="field uppercase"
              autoComplete="off"
              placeholder="Ej. ABC123"
              maxLength={6}
              value={placa}
              onChange={(e) => setPlaca(normalizarPlaca(e.target.value))}
            />
          </label>
          <label>
            Número de documento
            <input
              required
              className="field"
              inputMode="numeric"
              autoComplete="off"
              placeholder="Ej. 1020456789"
              value={documento}
              onChange={(e) => setDocumento(soloDigitos(e.target.value))}
            />
          </label>
          <div className="dashboard-label">
            Tipo de Trámite
            <SelectorTipoTramite opciones={OPCIONES} valor={tipo} onChange={setTipo} marcador="Selecciona" />
          </div>
          <button type="submit" className={boton("institutional", "dashboard-submit", "none")} disabled={!listo || fase === "cargando"}>
            Iniciar trámite
          </button>
        </form>
      </div>
      <FilaConsultar onConsultar={onConsultar} />
      {/* Mismo overlay de espera que IniciarModal / PaginaTramite del portal. */}
      {fase === "cargando" && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 backdrop-blur-sm">
          <MobilityLoader mensaje={MENSAJE_CONSULTANDO_VEHICULO} />
        </div>
      )}
    </section>
  );
}

/** Misma fila AccionFila del portal; sin Cotizar (una sola fila). */
function FilaConsultar({ onConsultar }: { onConsultar: () => void }) {
  return (
    <div className="action-rows action-rows-una">
      <AccionFila etiqueta="Consultar mi trámite" src={ASSETS.acciones.consultar} onClick={onConsultar} />
    </div>
  );
}
