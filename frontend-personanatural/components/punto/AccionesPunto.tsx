"use client";

// HU #14220 / #14222 / #14232 — Acciones del punto con el mismo layout del portal (AccionesPrincipales). Cotizar va con el organismo fijo del punto.
// Tras arranque OK abre el cascarón del wizard (DiagnosticoPunto), igual que el portal web PN.
import { useEffect, useState } from "react";
import { AccionFila } from "@/components/landing/AccionesPrincipales";
import { boton } from "@/components/landing/estilos";
import { SelectorTipoTramite } from "@/components/landing/SelectorTipoTramite";
import { MobilityLoader } from "@/components/MobilityLoader";
import { DiagnosticoPunto } from "@/components/punto/DiagnosticoPunto";
import { useTiposTramitePunto } from "@/components/landing/useCatalogos";
import { textoArranque, type ClientePortal } from "@/lib/api/cliente";
import { mensajeDe, mensajeDeFallo } from "@/lib/api/errores";
import { ASSETS } from "@/lib/assets";
import { normalizarPlaca, soloDigitos } from "@/lib/entradas";
import { ETIQUETA_VEHICULO } from "@/lib/landing/vehiculo";
import { MENSAJE_CONSULTANDO_VEHICULO } from "@/lib/tramite/diagnostico";

function IconoAccion({ src }: { src: string }) {
  return (
    <span aria-hidden className="action-top-icon">
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img src={src} alt="" />
    </span>
  );
}

export function AccionesPunto({
  cliente,
  organismoId,
  organismoNombre,
  onConsultar,
  onCotizar,
  onAviso,
  onWizard,
}: {
  cliente: ClientePortal;
  /** Organismo de la sesión del punto: sus tipos son los que tienen tarifa vigente ahí. */
  organismoId: string;
  organismoNombre: string;
  onConsultar: () => void;
  onCotizar?: () => void;
  /** Fallas y reglas de negocio: mismo AvisoModal del portal. */
  onAviso: (titulo: string, mensaje: string, tono?: "alerta" | "exito" | "info") => void;
  /** El padre cambia el shell (dashboard → cascarón del wizard). */
  onWizard?: (activo: boolean) => void;
}) {
  const [placa, setPlaca] = useState("");
  const [documento, setDocumento] = useState("");
  const [tipo, setTipo] = useState("");
  const [fase, setFase] = useState<"formulario" | "cargando" | "diagnostico">("formulario");
  // Mismo catálogo del portal web, acotado al organismo de la sesión del punto (sin lista fija de tipos).
  const tipos = useTiposTramitePunto(cliente, organismoId);
  const opciones = tipos.data ?? [];
  const sinTipos = tipos.isSuccess && opciones.length === 0;

  const listo = placa !== "" && documento !== "" && tipo !== "";

  useEffect(() => {
    onWizard?.(fase === "diagnostico");
  }, [fase, onWizard]);

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
      <DiagnosticoPunto
        cliente={cliente}
        placa={placa}
        documento={documento}
        tipoCodigo={tipo}
        tipoNombre={opciones.find((o) => o.code === tipo)?.name}
        organismoNombre={organismoNombre}
        onVolver={() => setFase("formulario")}
        onTerminar={() => {
          setPlaca("");
          setDocumento("");
          setTipo("");
          setFase("formulario");
        }}
        onAviso={onAviso}
      />
    );
  }

  return (
    <section className="dashboard-actions-stack" aria-label="Acciones del punto">
      <div className="dashboard-institutional-card action-start" id="nuevo-tramite">
        <IconoAccion src={ASSETS.acciones.iniciar} />
        <h2 className="dashboard-title">Iniciar mi trámite</h2>
        <p>Da clic aquí para iniciar el proceso de tu trámite digital en {organismoNombre}.</p>
        <form
          className="action-start-fields"
          aria-busy={fase === "cargando"}
          onSubmit={(e) => {
            e.preventDefault();
            void arrancar();
          }}
        >
          <label>
            Número de Cédula
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
          <label>
            {ETIQUETA_VEHICULO.PLATE}
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
          <div className="dashboard-label" aria-busy={tipos.isPending}>
            Tipo de Trámite
            <SelectorTipoTramite
              opciones={opciones}
              valor={tipo}
              onChange={setTipo}
              disabled={!tipos.isSuccess || sinTipos}
              marcador={tipos.isPending ? "Cargando..." : "Selecciona"}
              cargando={tipos.isPending}
            />
            <div aria-live="polite" className="text-xs font-normal">
              {tipos.isError && (
                <p role="alert">
                  {mensajeDe(tipos.error)}{" "}
                  <button type="button" className={boton("link", "h-auto p-0 text-xs", "none")} onClick={() => void tipos.refetch()}>
                    Reintentar
                  </button>
                </p>
              )}
              {sinTipos && <p>Por ahora no hay trámites disponibles en este punto.</p>}
            </div>
          </div>
          <button type="submit" className={boton("institutional", "dashboard-submit", "none")} disabled={!listo || fase === "cargando"}>
            Iniciar trámite
          </button>
        </form>
      </div>
      <FilasAccion onConsultar={onConsultar} onCotizar={onCotizar} />
      {fase === "cargando" && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 backdrop-blur-sm">
          <MobilityLoader mensaje={MENSAJE_CONSULTANDO_VEHICULO} />
        </div>
      )}
    </section>
  );
}

/** Mismas filas AccionFila del portal. Sin acción de cotizar conectada queda una sola fila. */
function FilasAccion({ onConsultar, onCotizar }: { onConsultar: () => void; onCotizar?: () => void }) {
  return (
    <div className={onCotizar ? "action-rows" : "action-rows action-rows-una"}>
      <AccionFila etiqueta="Consultar mi trámite" src={ASSETS.acciones.consultar} onClick={onConsultar} />
      {onCotizar && <AccionFila etiqueta="Cotizar mi trámite" src={ASSETS.acciones.cotizar} onClick={onCotizar} />}
    </div>
  );
}
