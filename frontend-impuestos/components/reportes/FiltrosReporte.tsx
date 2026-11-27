"use client";

import { useId, useState, type FormEvent } from "react";
import { SearchableSelect } from "@flit/ui/SearchableSelect";
import { SelectorSegmentado } from "@flit/ui/SelectorSegmentado";
import { BOTON_SECUNDARIO_CLS, CAMPO_CLS, ETIQUETA_CLS } from "@/components/comun/estilos";
import { FiltroPeriodo } from "@/components/estadisticas/FiltroPeriodo";
import { ETIQUETA_ESTADO_ITEM, OPCIONES_TIPO, type ReporteCatalogo } from "@/lib/reportes/catalogo";
import type { Departamento, FiltrosReporte as Filtros } from "@/lib/reportes/api";

export interface OpcionUsuario {
  id: string;
  nombre: string;
}

/** Placa en mayúscula, sin espacios, como la normaliza el contrato (≤ 10). */
const normalizarPlaca = (texto: string) => texto.toUpperCase().replace(/\s+/g, "").slice(0, 10);

/**
 * Filtros de Reportes (spec-visual §3.7): Desde · Hasta · Tipo · Estado · Departamento · Usuario · Placa, y «Limpiar
 * filtros». Solo se pintan los que aplican al reporte activo. Fechas, tipo y listas se aplican al cambiar; la placa, al
 * pulsar Enter o al salir del campo. «Departamento» y «Usuario» salen de GET /catalogos/reportes (delta 12, permiso
 * impuestos.reportes.ver, también el Gestor) y solo aparecen si el catálogo cargó con opciones; el usuario se muestra por
 * su nombre y viaja su id.
 */
export function FiltrosReporte({
  reporte,
  filtros,
  hoy,
  departamentos,
  usuarios,
  errorRango,
  onCambiar,
  onLimpiar,
}: {
  reporte: ReporteCatalogo;
  filtros: Filtros;
  hoy: string;
  departamentos: Departamento[] | null;
  usuarios: OpcionUsuario[] | null;
  errorRango: string | null;
  onCambiar: (cambios: Partial<Filtros>) => void;
  onLimpiar: () => void;
}) {
  const id = useId();
  const aplica = new Set(reporte.filtros);
  const [placa, setPlaca] = useState(filtros.placa);
  const [placaAplicada, setPlacaAplicada] = useState(filtros.placa);
  // La placa cambió desde fuera («Limpiar filtros»): el campo la sigue.
  if (filtros.placa !== placaAplicada) {
    setPlacaAplicada(filtros.placa);
    setPlaca(filtros.placa);
  }

  const aplicarPlaca = (ev?: FormEvent) => {
    ev?.preventDefault();
    if (placa !== filtros.placa) onCambiar({ placa });
  };

  return (
    <div role="search" aria-label={`Filtros de ${reporte.nombre}`} className="flex flex-col gap-3">
      <div className="flex flex-wrap items-end gap-3">
        <FiltroPeriodo valor={filtros.rango} hoy={hoy} onAplicar={(rango) => onCambiar({ rango })} errorServidor={errorRango} />
        {aplica.has("tipo") ? (
          <div className="flex flex-col gap-1">
            <SelectorSegmentado
              etiqueta="Tipo"
              opciones={OPCIONES_TIPO.map((o) => ({ valor: o.valor || "TODAS", label: o.label }))}
              valor={filtros.tipoLiquidacion || "TODAS"}
              onCambio={(v) => onCambiar({ tipoLiquidacion: v === "TODAS" ? "" : (v as Filtros["tipoLiquidacion"]) })}
            />
          </div>
        ) : null}
      </div>
      <div className="flex flex-wrap items-end gap-3">
        {aplica.has("estado") ? (
          <div className="flex w-full flex-col gap-1 sm:w-56">
            <label htmlFor={`${id}-estado`} className={ETIQUETA_CLS}>
              Estado
            </label>
            <select id={`${id}-estado`} value={filtros.estado} onChange={(e) => onCambiar({ estado: e.target.value })} className={CAMPO_CLS}>
              <option value="">Todos</option>
              {Object.entries(ETIQUETA_ESTADO_ITEM).map(([codigo, etiqueta]) => (
                <option key={codigo} value={codigo}>
                  {etiqueta}
                </option>
              ))}
            </select>
          </div>
        ) : null}
        {aplica.has("departamento") && departamentos && departamentos.length > 0 ? (
          <div className="flex w-full flex-col gap-1 sm:w-56">
            <label htmlFor={`${id}-departamento`} className={ETIQUETA_CLS}>
              Departamento
            </label>
            <select
              id={`${id}-departamento`}
              value={filtros.departamento}
              onChange={(e) => onCambiar({ departamento: e.target.value })}
              className={CAMPO_CLS}
            >
              <option value="">Todos</option>
              {departamentos.map((d) => (
                <option key={d.codigo} value={d.codigo}>
                  {d.nombre}
                </option>
              ))}
            </select>
          </div>
        ) : null}
        {aplica.has("usuario") && usuarios && usuarios.length > 0 ? (
          <div className="w-full sm:w-64">
            <SearchableSelect
              label="Usuario"
              defaultLabel="Todos"
              options={usuarios.map((u) => ({ value: u.id, label: u.nombre }))}
              value={filtros.usuario}
              onChange={(usuario) => onCambiar({ usuario })}
              className="[&_button]:min-h-6 [&_button]:min-w-6"
            />
          </div>
        ) : null}
        {aplica.has("placa") ? (
          <form onSubmit={aplicarPlaca} className="flex w-full flex-col gap-1 sm:w-44">
            <label htmlFor={`${id}-placa`} className={ETIQUETA_CLS}>
              Placa
            </label>
            <input
              id={`${id}-placa`}
              type="text"
              inputMode="text"
              autoComplete="off"
              maxLength={10}
              value={placa}
              onChange={(e) => setPlaca(normalizarPlaca(e.target.value))}
              onBlur={() => aplicarPlaca()}
              className={`${CAMPO_CLS} font-semibold uppercase tracking-[0.08em]`}
            />
          </form>
        ) : null}
        <button type="button" onClick={onLimpiar} className={BOTON_SECUNDARIO_CLS}>
          Limpiar filtros
        </button>
      </div>
    </div>
  );
}
