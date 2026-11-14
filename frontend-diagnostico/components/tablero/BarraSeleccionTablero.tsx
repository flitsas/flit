"use client";

import { useEffect, useId, useRef } from "react";
import { BarraSeleccion } from "@flit/ui/BarraSeleccion";
import { Button } from "@flit/ui/Button";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { IconoDxBandera } from "@/components/dx/iconos/IconoDxBandera";
import { IconoDxCrearTramite } from "@/components/dx/iconos/IconoDxCrearTramite";
import { BOTON_SECUNDARIO_CLS, FOCO_CLS, MARCA_CLS } from "@/components/configuracion/estilos";
import { MAXIMO_SOLICITUD_MASIVA, type MotivoOmision, type ResultadoSolicitud } from "@/lib/saneamiento/api";
import { ROTULO_MOTIVO_NO_ELEGIBLE } from "@/lib/saneamiento/estados";
import type { FilaVehiculo } from "@/lib/tablero/api";
import { derivacionDeFila, rotuloMotivo } from "@/lib/tablero/derivacion";

/** HU #14562 AC32: «1 vehículo seleccionado» / «N vehículos seleccionados» (maqueta del tablero). */
export const textoSeleccionadas = (n: number) => (n === 1 ? "1 vehículo seleccionado" : `${n} vehículos seleccionados`);

// ---------- Elegibilidad de la casilla ----------

/** Capacidades de la sesión que dan acciones en bloque. */
export interface CapacidadesSeleccion {
  derivar: boolean;
  sanear: boolean;
}

/** Selección de una fila: si la casilla existe, si se habilita, para qué acciones cuenta y, si no, por qué. */
export interface SeleccionFila {
  /** La fila trae datos de alguna acción que el usuario tiene (sin ellos, no hay casilla). */
  visible: boolean;
  habilitada: boolean;
  /** Cuenta en «Crear trámites (N)». */
  derivar: boolean;
  /** Cuenta en «Sanear (N)». */
  sanear: boolean;
  /** Motivo de la deshabilitada: los de cada acción del usuario, unidos con « · ». */
  motivo?: string;
}

/**
 * Elegibilidad de la fila para la selección del tablero (spec-ui §2.4, HU #14577 AC10): habilitada si lo es para alguna
 * acción que el usuario tenga, derivar (`derivacionDeFila(v).elegible` y `derivar`) o sanear (`v.saneamiento.elegible` y
 * `sanear`). Deshabilitada, el motivo une los de las acciones que el usuario tiene, p. ej. «… · Faltan consultas por
 * terminar».
 */
export function seleccionDeFila(v: FilaVehiculo, capacidades: CapacidadesSeleccion): SeleccionFila {
  const d = derivacionDeFila(v);
  const conDerivacion = capacidades.derivar && d.conocida;
  const conSaneamiento = capacidades.sanear && v.saneamiento != null;
  const derivar = conDerivacion && d.elegible;
  const sanear = conSaneamiento && v.saneamiento?.elegible === true;
  const habilitada = derivar || sanear;
  let motivo: string | undefined;
  if (!habilitada) {
    const motivos: string[] = [];
    if (conDerivacion) motivos.push(rotuloMotivo(d.motivo ?? (d.tramite ? "YA_TIENE_TRAMITE" : null)));
    const m = v.saneamiento?.motivo_no_elegible;
    if (conSaneamiento && m && Object.prototype.hasOwnProperty.call(ROTULO_MOTIVO_NO_ELEGIBLE, m)) motivos.push(ROTULO_MOTIVO_NO_ELEGIBLE[m]);
    motivo = motivos.length ? motivos.join(" · ") : undefined;
  }
  return { visible: conDerivacion || conSaneamiento, habilitada, derivar, sanear, motivo };
}

export interface CasillaTableroProps {
  vehiculo: FilaVehiculo;
  seleccion: SeleccionFila;
  marcada: boolean;
  onCambiar: (vehiculoId: string, marcada: boolean) => void;
}

/**
 * Casilla de selección de una fila (reemplaza a `CasillaDerivacion`; HU #14562 · m-8). Activa si la fila es elegible para
 * alguna acción del usuario; deshabilitada, el motivo va en un texto `hidden` enlazado por `aria-describedby` (la
 * descripción lo toma y el lector no lo repite al recorrer la fila) y en `title` para el puntero. Sin datos de ninguna
 * acción del usuario, no se pinta.
 */
export function CasillaTablero({ vehiculo, seleccion, marcada, onCambiar }: CasillaTableroProps) {
  const motivoId = `${useId()}-motivo`;
  if (!seleccion.visible) return null;
  const identificador = vehiculo.placa || vehiculo.vin || vehiculo.identificador;
  const motivo = seleccion.habilitada ? undefined : seleccion.motivo;
  return (
    <>
      <input
        type="checkbox"
        aria-label={`Seleccionar la placa ${identificador}`}
        aria-describedby={motivo ? motivoId : undefined}
        checked={seleccion.habilitada && marcada}
        disabled={!seleccion.habilitada}
        title={motivo}
        onChange={(e) => onCambiar(vehiculo.id, e.target.checked)}
        className={`${MARCA_CLS} disabled:cursor-not-allowed disabled:opacity-50`}
      />
      {motivo && (
        <span id={motivoId} hidden>
          {motivo}
        </span>
      )}
    </>
  );
}

// ---------- Resultado de «Sanear (N)» ----------

export interface ResultadoSanearVisible {
  /** Cambia en cada solicitud, para volver a mover el foco aunque el texto se repita. */
  n: number;
  tono: "success" | "warning";
  /** UA-7: «Saneamiento solicitado» o, con 0 solicitados, «No se solicitó el saneamiento». */
  titulo: string;
  texto: string;
}

const contar = (n: number, singular: string, plural: string) => `${n} ${n === 1 ? singular : plural}`;
const unirConY = (partes: string[]) => (partes.length <= 1 ? (partes[0] ?? "") : `${partes.slice(0, -1).join(", ")} y ${partes.at(-1)}`);

/** Orden y texto de cada motivo de omisión (spec-ui §2.4). */
const OMISIONES: readonly [MotivoOmision, string, string][] = [
  ["NO_TERMINADO", "con consultas por terminar", "con consultas por terminar"],
  ["YA_SOLICITADO", "que ya tenía saneamiento", "que ya tenían saneamiento"],
  ["NO_ENCONTRADO", "no disponible", "no disponibles"],
];

/**
 * Texto del resultado de POST /saneamientos/solicitudes (spec-ui §2.4, HU #14577 AC11): «Se solicitó el saneamiento de X
 * vehículos.» y, si hubo omitidos, «Y se omitieron: A con consultas por terminar, B que ya tenían saneamiento y C no
 * disponibles.» (solo las partes con cuenta). Sin omitidos, tono de éxito; con omitidos, de aviso. Ninguno solicitado: «No se
 * solicitó el saneamiento de ningún vehículo.» (D-29). El título sigue al cuerpo (UA-7): «Saneamiento solicitado» si se
 * solicitó alguno; si no, «No se solicitó el saneamiento».
 */
export function mensajeSolicitudSaneamiento(r: ResultadoSolicitud): Omit<ResultadoSanearVisible, "n"> {
  const titulo = r.solicitados > 0 ? "Saneamiento solicitado" : "No se solicitó el saneamiento";
  const cabeza =
    r.solicitados > 0
      ? `Se solicitó el saneamiento de ${contar(r.solicitados, "vehículo", "vehículos")}.`
      : "No se solicitó el saneamiento de ningún vehículo.";
  if (r.omitidos.length === 0) return { tono: "success", titulo, texto: cabeza };
  const partes = OMISIONES.map(([motivo, s, p]) => [r.omitidos.filter((o) => o.motivo === motivo).length, s, p] as const)
    .filter(([n]) => n > 0)
    .map(([n, s, p]) => contar(n, s, p));
  const total = r.omitidos.length;
  const detalle = partes.length ? `: ${unirConY(partes)}` : "";
  return { tono: "warning", titulo, texto: `${cabeza} ${total} ${total === 1 ? "se omitió" : "se omitieron"}${detalle}.` };
}

// ---------- Barra ----------

export interface AccionCrearTramites {
  /** Seleccionadas elegibles para crear trámite (N₁). */
  cantidad: number;
  enviando: boolean;
  onCrear: () => void;
  /** Error al encolar, en lenguaje claro. */
  error?: string | null;
}

export interface AccionSanear {
  /** Seleccionadas elegibles para sanear (N₂). */
  cantidad: number;
  enviando: boolean;
  onSanear: () => void;
  /** Error de la solicitud (`400 LISTA_INVALIDA`, red). */
  error?: string | null;
  /** Resultado de la última solicitud. */
  resultado?: ResultadoSanearVisible | null;
}

export interface BarraSeleccionTableroProps {
  /** Filas con la casilla habilitada (las que marca «Seleccionar todos los elegibles»). */
  elegibles: number;
  /** Seleccionadas con la casilla habilitada. */
  seleccionadas: number;
  onSeleccionarTodas: () => void;
  onQuitar: () => void;
  /** «Crear trámites (N)»: solo con la capacidad `derivar` (sin ella, ausente). */
  crear?: AccionCrearTramites;
  /** «Sanear (N)»: solo con la capacidad `sanear` (sin ella, ausente). */
  sanear?: AccionSanear;
}

/** Botón de la barra: con N = 0 (o sobre el tope) queda `aria-disabled` con el motivo como descripción y el clic se ignora. */
function AccionBarra({
  variante,
  cantidad,
  enviando,
  textoEnvio,
  texto,
  icono,
  bloqueo,
  onClick,
}: {
  variante: "primary" | "secondary";
  cantidad: number;
  enviando: boolean;
  textoEnvio: string;
  texto: string;
  icono: React.ReactNode;
  bloqueo: string | null;
  onClick: () => void;
}) {
  const bloqueoId = `${useId()}-bloqueo`;
  return (
    <>
      <Button
        variant={variante}
        cargando={enviando}
        aria-disabled={bloqueo ? "true" : undefined}
        aria-describedby={bloqueo ? bloqueoId : undefined}
        className={bloqueo ? "cursor-not-allowed opacity-60" : undefined}
        onClick={() => {
          if (!bloqueo) onClick();
        }}
      >
        {enviando ? (
          textoEnvio
        ) : (
          <>
            {icono}
            {`${texto} (${cantidad})`}
          </>
        )}
      </Button>
      {bloqueo && (
        <span id={bloqueoId} hidden>
          {bloqueo}
        </span>
      )}
    </>
  );
}

/**
 * Selección del tablero (spec-ui §2.4, HU #14577 AC10–AC11; reemplaza a `BarraSeleccionDerivacion`). Sin preselección,
 * «Seleccionar todos los elegibles» y, con algo marcado, la barra del kit (`BarraSeleccion`, sin cambios) con dos acciones
 * en este orden: «Crear trámites (N₁)» (primaria; solo con `derivar`) y «Sanear (N₂)» (secundaria; primaria si no hay
 * crear; solo con `sanear`). Cada N cuenta solo los seleccionados elegibles para esa acción. Bajo la barra, el error de
 * cada acción (`role="alert"`) y el resultado de «Sanear» (`InlineAlert`), que recibe el foco si la selección quedó vacía
 * (el botón pulsado desapareció).
 */
export function BarraSeleccionTablero({ elegibles, seleccionadas, onSeleccionarTodas, onQuitar, crear, sanear }: BarraSeleccionTableroProps) {
  const enviando = (crear?.enviando ?? false) || (sanear?.enviando ?? false);
  const resultadoRef = useRef<HTMLDivElement>(null);
  const resultado = sanear?.resultado ?? null;
  const seleccionVacia = seleccionadas === 0;
  useEffect(() => {
    // El foco se queda en «Sanear»; si la barra desapareció (selección vacía), pasa al resultado.
    if (resultado && seleccionVacia) resultadoRef.current?.focus();
  }, [resultado, seleccionVacia]);

  const etiqueta = crear && sanear ? "Selección para crear trámites o sanear" : crear ? "Selección para crear trámites" : "Selección para sanear";
  const ayuda = sanear ? "Cada acción cuenta solo los vehículos que cumplen su regla." : "Solo se seleccionan vehículos Viables y sin trámite";

  return (
    <div role="group" aria-label={etiqueta} className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={onSeleccionarTodas} disabled={elegibles === 0 || enviando} className={BOTON_SECUNDARIO_CLS}>
          {/* HU #14562 · m-9: íconos del kit y de Diagnóstico, sin lucide. */}
          <Icono nombre="check" className="h-4 w-4" />
          Seleccionar todos los elegibles
        </button>
      </div>
      <BarraSeleccion
        cantidad={seleccionadas}
        textoSeleccion={textoSeleccionadas}
        ayuda={ayuda}
        onQuitar={enviando ? undefined : onQuitar}
        acciones={
          <>
            {crear && (
              <AccionBarra
                variante="primary"
                cantidad={crear.cantidad}
                enviando={crear.enviando}
                textoEnvio="Creando trámites…"
                texto="Crear trámites"
                icono={<IconoDxCrearTramite tamano={16} />}
                bloqueo={crear.cantidad === 0 ? "Ninguno de los seleccionados se puede crear" : null}
                onClick={crear.onCrear}
              />
            )}
            {sanear && (
              <AccionBarra
                variante={crear ? "secondary" : "primary"}
                cantidad={sanear.cantidad}
                enviando={sanear.enviando}
                textoEnvio="Solicitando…"
                texto="Sanear"
                icono={<IconoDxBandera tamano={16} />}
                bloqueo={
                  sanear.cantidad === 0
                    ? "Ninguno de los seleccionados se puede sanear"
                    : sanear.cantidad > MAXIMO_SOLICITUD_MASIVA
                      ? `Máximo ${MAXIMO_SOLICITUD_MASIVA} vehículos por solicitud`
                      : null
                }
                onClick={sanear.onSanear}
              />
            )}
          </>
        }
      />
      {crear?.error && (
        <p role="alert" aria-label="Resultado de crear trámites" className="text-xs font-semibold" style={{ color: "var(--badge-danger-fg)" }}>
          {crear.error}
        </p>
      )}
      {sanear?.error && (
        <p role="alert" aria-label="Resultado de sanear" className="text-xs font-semibold" style={{ color: "var(--badge-danger-fg)" }}>
          {sanear.error}
        </p>
      )}
      {resultado && (
        <div ref={resultadoRef} tabIndex={-1} className={`rounded-[9px] ${FOCO_CLS}`}>
          {/* UA-4: no es región viva; lo anuncia la región de la pantalla (el foco se quedó) o el foco que llega aquí. */}
          <InlineAlert tone={resultado.tono} title={resultado.titulo} vivo={false}>
            {resultado.texto}
          </InlineAlert>
        </div>
      )}
    </div>
  );
}
