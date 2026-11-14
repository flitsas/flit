"use client";

import { useEffect, useEffectEvent, useId, useState, type KeyboardEvent } from "react";
import { Icono } from "@flit/ui/Icono";
import { BOTON_SECUNDARIO_CLS, CAMPO_CLS, ETIQUETA_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { CAMPO_INVALIDO_CLS, ERROR_CLS } from "@/components/parametrizacion/estilos";
import type { ModoDiagnostico } from "@/lib/diagnosticos/api";
import type { EstadoSaneamientoActivo } from "@/lib/saneamiento/api";
import { etiquetaSaneamiento } from "@/lib/saneamiento/estados";
import {
  ESTADOS_SANEAMIENTO_FILTRO,
  type CampoFiltroVehiculos,
  type EstadoVehiculo,
  type FiltrosVehiculos as Filtros,
  type VeredictoFiltro,
} from "@/lib/vehiculos/listado";

/** Espera al escribir la placa antes de consultar (spec-ui §4.2). */
export const ESPERA_PLACA_MS = 400;

export interface FiltrosVehiculosProps {
  valor: Filtros;
  /** 400 con `campo`: el control queda inválido con el mensaje junto a él. */
  error: { campo: CampoFiltroVehiculos; mensaje: string } | null;
  onCambiar: (cambios: Partial<Filtros>) => void;
  onLimpiar: () => void;
  /** «Actualizar»: vuelve a pedir la grilla (no hay refresco automático, plan §6.6). */
  onActualizar: () => void;
}

/** Control de la pantalla que pinta cada campo del error (los dos de saneamiento caen en el mismo select). */
type Control = "placa" | "estado" | "veredicto" | "saneamiento" | "modo" | "con_tramite" | "desde" | "hasta";
const CONTROL_DE: Record<CampoFiltroVehiculos, Control> = {
  placa: "placa",
  estado: "estado",
  veredicto: "veredicto",
  saneamiento: "saneamiento",
  saneamiento_estado: "saneamiento",
  modo: "modo",
  con_tramite: "con_tramite",
  desde: "desde",
  hasta: "hasta",
};

const ESTADOS: readonly { valor: EstadoVehiculo; texto: string }[] = [
  { valor: "EN_PROCESO", texto: "En proceso" },
  { valor: "TERMINADO", texto: "Terminado" },
];
const DICTAMENES: readonly { valor: VeredictoFiltro; texto: string }[] = [
  { valor: "VIABLE", texto: "Viable" },
  { valor: "BLOQUEADO", texto: "Bloqueado" },
  { valor: "SIN_DICTAMEN", texto: "Sin dictamen" },
];
const MODOS_FILTRO: readonly { valor: ModoDiagnostico; texto: string }[] = [
  { valor: "SIMPLE", texto: "Simple" },
  { valor: "CON_ANALISIS", texto: "Con análisis IA" },
];

/** Un solo select para «con / sin» y los cuatro estados (D-10): un estado implica «con saneamiento». */
type ValorSaneamiento = "" | "CON" | "SIN" | EstadoSaneamientoActivo;
const valorSaneamiento = (f: Filtros): ValorSaneamiento => f.saneamientoEstado ?? f.saneamiento ?? "";
function cambiosSaneamiento(v: ValorSaneamiento): Partial<Filtros> {
  if (v === "") return { saneamiento: null, saneamientoEstado: null };
  if (v === "CON" || v === "SIN") return { saneamiento: v, saneamientoEstado: null };
  return { saneamiento: "CON", saneamientoEstado: v };
}

const SELECT_CLS = "w-full min-[480px]:w-48";

/**
 * Filtros de la pestaña «Vehículos» (spec-ui §4.2, HU #14577 AC21) en una tarjeta. «Placa» se pinta en mayúsculas y aplica
 * 400 ms después de dejar de escribir o con Enter. «Estado del vehículo», «Dictamen», «Saneamiento», «Modo» y «Trámite» son
 * `<select>` nativos de una opción (D-10); «Desde» y «Hasta», fechas acotadas entre sí. Cada control tiene etiqueta visible;
 * un 400 con `campo` deja el control con `aria-invalid` y el mensaje enlazado por `aria-describedby`. A 360 px los controles
 * pasan a una columna (`flex-wrap`, ancho completo).
 */
export function FiltrosVehiculos({ valor, error, onCambiar, onLimpiar, onActualizar }: FiltrosVehiculosProps) {
  const id = useId();
  const [placa, setPlaca] = useState(valor.placa.toUpperCase());
  const [placaExterna, setPlacaExterna] = useState(valor.placa);
  // Última placa emitida: Enter aplica de inmediato y la espera que vence después no la vuelve a mandar.
  const [emitida, setEmitida] = useState(valor.placa);

  // La placa de los filtros cambió desde fuera (Limpiar filtros, volver atrás): el campo la sigue.
  if (valor.placa !== placaExterna) {
    setPlacaExterna(valor.placa);
    setPlaca(valor.placa.toUpperCase());
    setEmitida(valor.placa);
  }

  function aplicarPlaca(texto: string) {
    const limpia = texto.trim();
    if (limpia === emitida.trim() || limpia === valor.placa.trim()) return;
    setEmitida(texto);
    onCambiar({ placa: texto });
  }
  const emitirPlaca = useEffectEvent((texto: string) => aplicarPlaca(texto));
  useEffect(() => {
    const espera = setTimeout(() => emitirPlaca(placa), ESPERA_PLACA_MS);
    return () => clearTimeout(espera);
  }, [placa]);

  function alPulsarTecla(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key !== "Enter") return;
    e.preventDefault();
    aplicarPlaca(placa);
  }

  const control = error ? CONTROL_DE[error.campo] : null;
  const errorId = (c: Control) => `${id}-${c}-error`;
  const invalido = (c: Control) => (control === c ? { "aria-invalid": true as const, "aria-describedby": errorId(c) } : {});
  const clase = (c: Control, extra: string) => `${CAMPO_CLS} ${extra} ${control === c ? CAMPO_INVALIDO_CLS : ""}`.trim();
  const mensaje = (c: Control) =>
    control === c && error ? (
      <p id={errorId(c)} className={ERROR_CLS}>
        {error.mensaje}
      </p>
    ) : null;

  const campo = (c: Control, etiqueta: string, entrada: React.ReactNode, ancho = "w-full min-[480px]:w-auto") => (
    <div className={`flex flex-col gap-1 ${ancho}`}>
      <label htmlFor={`${id}-${c}`} className={ETIQUETA_CLS}>
        {etiqueta}
      </label>
      {entrada}
      {mensaje(c)}
    </div>
  );

  return (
    <div role="group" aria-label="Filtros de vehículos" className={`${TARJETA_CLS} flex flex-wrap items-start gap-3`}>
      {campo(
        "placa",
        "Placa",
        <input
          id={`${id}-placa`}
          type="text"
          inputMode="text"
          autoComplete="off"
          spellCheck={false}
          maxLength={20}
          value={placa}
          onChange={(e) => setPlaca(e.target.value.toUpperCase())}
          onKeyDown={alPulsarTecla}
          className={clase("placa", "uppercase tracking-wider")}
          {...invalido("placa")}
        />,
        "w-full min-[480px]:w-40",
      )}
      {campo(
        "estado",
        "Estado del vehículo",
        <select
          id={`${id}-estado`}
          value={valor.estado ?? ""}
          onChange={(e) => onCambiar({ estado: (e.target.value || null) as EstadoVehiculo | null })}
          className={clase("estado", SELECT_CLS)}
          {...invalido("estado")}
        >
          <option value="">Todos</option>
          {ESTADOS.map((o) => (
            <option key={o.valor} value={o.valor}>
              {o.texto}
            </option>
          ))}
        </select>,
      )}
      {campo(
        "veredicto",
        "Dictamen",
        <select
          id={`${id}-veredicto`}
          value={valor.veredicto ?? ""}
          onChange={(e) => onCambiar({ veredicto: (e.target.value || null) as VeredictoFiltro | null })}
          className={clase("veredicto", SELECT_CLS)}
          {...invalido("veredicto")}
        >
          <option value="">Todos</option>
          {DICTAMENES.map((o) => (
            <option key={o.valor} value={o.valor}>
              {o.texto}
            </option>
          ))}
        </select>,
      )}
      {campo(
        "saneamiento",
        "Saneamiento",
        <select
          id={`${id}-saneamiento`}
          value={valorSaneamiento(valor)}
          onChange={(e) => onCambiar(cambiosSaneamiento(e.target.value as ValorSaneamiento))}
          className={clase("saneamiento", SELECT_CLS)}
          {...invalido("saneamiento")}
        >
          <option value="">Todos</option>
          <option value="CON">Con saneamiento</option>
          <option value="SIN">Sin saneamiento</option>
          {ESTADOS_SANEAMIENTO_FILTRO.map((e) => (
            <option key={e} value={e}>
              {etiquetaSaneamiento(e)?.texto}
            </option>
          ))}
        </select>,
      )}
      {campo(
        "modo",
        "Modo",
        <select
          id={`${id}-modo`}
          value={valor.modo ?? ""}
          onChange={(e) => onCambiar({ modo: (e.target.value || null) as ModoDiagnostico | null })}
          className={clase("modo", SELECT_CLS)}
          {...invalido("modo")}
        >
          <option value="">Todos</option>
          {MODOS_FILTRO.map((o) => (
            <option key={o.valor} value={o.valor}>
              {o.texto}
            </option>
          ))}
        </select>,
      )}
      {campo(
        "con_tramite",
        "Trámite",
        <select
          id={`${id}-con_tramite`}
          value={valor.conTramite === null ? "" : String(valor.conTramite)}
          onChange={(e) => onCambiar({ conTramite: e.target.value === "" ? null : e.target.value === "true" })}
          className={clase("con_tramite", SELECT_CLS)}
          {...invalido("con_tramite")}
        >
          <option value="">Todos</option>
          <option value="true">Con trámite</option>
          <option value="false">Sin trámite</option>
        </select>,
      )}
      {campo(
        "desde",
        "Desde",
        <input
          id={`${id}-desde`}
          type="date"
          value={valor.desde}
          max={valor.hasta || undefined}
          onChange={(e) => onCambiar({ desde: e.target.value })}
          className={clase("desde", "w-full min-[480px]:w-44")}
          {...invalido("desde")}
        />,
      )}
      {campo(
        "hasta",
        "Hasta",
        <input
          id={`${id}-hasta`}
          type="date"
          value={valor.hasta}
          min={valor.desde || undefined}
          onChange={(e) => onCambiar({ hasta: e.target.value })}
          className={clase("hasta", "w-full min-[480px]:w-44")}
          {...invalido("hasta")}
        />,
      )}
      <div className="flex flex-wrap gap-2 self-end">
        <button type="button" onClick={onLimpiar} className={BOTON_SECUNDARIO_CLS}>
          Limpiar filtros
        </button>
        <button type="button" onClick={onActualizar} className={BOTON_SECUNDARIO_CLS}>
          <Icono nombre="reintento" className="h-4 w-4" />
          Actualizar
        </button>
      </div>
    </div>
  );
}
