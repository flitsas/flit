"use client";

import { Suspense, useCallback, useMemo, useState } from "react";
import { useSearchParams } from "next/navigation";
import { ETIQUETA_PUNTO_CLS, etiquetaKit } from "@flit/ui/StatusBadge";
import { useSesion } from "@/components/ComparendosShell";
import { Encabezado } from "@/components/Pantalla";
import { RequiereReportes } from "@/components/reportes/RequiereReportes";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { ProgramarEnvio } from "@/components/consultas-personalizadas/ProgramarEnvio";
import { QueryConsole } from "@/components/consultas-personalizadas/QueryConsole";
import {
  COMPARENDO_DEFAULT_COLUMNS,
  COMPARENDO_PRESETS,
  comparendoColumns,
  tonoEstadoLegal,
} from "@/components/consultas-personalizadas/comparendo-columns";
import { fuenteComparendos, type ComparendoQueryRow, type QueryField, type SavedQuery } from "@/lib/api/consultas-personalizadas";
import { P } from "@/lib/nav";
import { tienePermiso } from "@/lib/sesion";

/** Celdas que se pintan distinto de su texto: el estado legal con la etiqueta del kit (texto del catálogo), como en la bandeja. */
function renderCelda(columnId: string, fila: ComparendoQueryRow, texto: string) {
  if (columnId !== "estadoLegal" || !fila.estadoLegal) return null;
  return (
    <span {...etiquetaKit(tonoEstadoLegal(fila.estadoLegal))}>
      <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
      {texto}
    </span>
  );
}

// Identidad estable (la consola las usa como dependencia): una con la columna Empresa y otra sin ella.
const columnasConEmpresa = (fields: QueryField[]) => comparendoColumns(fields, { conEmpresa: true });
const columnasSinEmpresa = (fields: QueryField[]) => comparendoColumns(fields, { conEmpresa: false });
const rowKey = (fila: ComparendoQueryRow) => fila.id;

/**
 * La empresa del SuperAdmin vive en `?empresa=` (mismo nombre que el resto de pestañas de Reportes): así viaja entre
 * pestañas y dentro del enlace copiado. Se escribe con `replaceState`, que Next sincroniza con `useSearchParams`.
 */
function cambiarEmpresa(empresa: string | null) {
  const url = new URL(window.location.href);
  if (empresa) url.searchParams.set("empresa", empresa);
  else url.searchParams.delete("empresa");
  // La consulta armada no viaja a otra empresa: sus fichas (un NIT, un organismo) son de la anterior y darían 0 sin
  // explicación. Se empieza de cero.
  url.searchParams.delete("q");
  window.history.replaceState(window.history.state, "", url);
}

function Contenido() {
  const { sesion } = useSesion();
  const params = useSearchParams();
  const esSuperAdmin = !!sesion?.usuario.esSuperAdmin;
  // Solo el SuperAdmin elige empresa; los demás consultan la suya (el servidor la fija por la sesión).
  const empresa = esSuperAdmin ? params.get("empresa") || null : null;
  const todas = esSuperAdmin && !empresa;

  // «Programar envío» (ronda 2) solo con el permiso de programar, y sobre una empresa concreta: el envío es de una
  // empresa, así que el SuperAdmin en «todas» no lo ofrece.
  const puedeProgramar = tienePermiso(sesion, P.reportesProgramar) && !todas;
  const [programando, setProgramando] = useState<SavedQuery | null>(null);
  const abrirProgramar = useCallback((q: SavedQuery) => setProgramando(q), []);

  // El origen se memoriza por empresa: la consola lo usa como dependencia de sus efectos.
  const source = useMemo(
    () => ({ ...fuenteComparendos(empresa, todas), onSchedule: puedeProgramar ? abrirProgramar : undefined }),
    [empresa, todas, puedeProgramar, abrirProgramar],
  );
  // «Empresa» solo tiene dato para el SuperAdmin con «todas»: en los demás casos la columna ni se ofrece.
  const columnas = todas ? columnasConEmpresa : columnasSinEmpresa;
  const porDefecto = useMemo(() => (todas ? ["empresaNombre", ...COMPARENDO_DEFAULT_COLUMNS] : COMPARENDO_DEFAULT_COLUMNS), [todas]);
  // Con «todas», la columna Empresa no se cae al aplicar un preset o abrir una consulta del sistema.
  const fijas = useMemo(() => (todas ? ["empresaNombre"] : undefined), [todas]);

  return (
    <section className="flex min-w-0 flex-col gap-6">
      <Encabezado
        titulo="Consultas"
        descripcion="Arma tu propia consulta sobre los comparendos: pega una lista de números o placas, combina filtros, guárdala y expórtala a Excel."
      />
      {esSuperAdmin && (
        <div className="flex flex-col gap-1">
          <SelectorEmpresa valor={empresa} onCambio={cambiarEmpresa} />
          <span className="text-xs text-[color:var(--di-text-tertiary)]">Sin empresa elegida se consulta sobre todas.</span>
        </div>
      )}
      {/* `key`: cambiar de empresa trae otro catálogo y otras consultas guardadas; la consola arranca limpia. */}
      <QueryConsole<ComparendoQueryRow>
        key={empresa ?? "todas"}
        source={source}
        columns={columnas}
        presets={COMPARENDO_PRESETS}
        defaultColumns={porDefecto}
        rowKey={rowKey}
        sheetName="Comparendos"
        ambito={todas ? "las empresas" : "su empresa"}
        renderCell={renderCelda}
        columnasFijas={fijas}
      />
      {puedeProgramar && <ProgramarEnvio consulta={programando} empresa={empresa} onCerrar={() => setProgramando(null)} />}
    </section>
  );
}

/** Reportes 2.0 — pestaña Consultas (contrato §4–§5). */
export default function Page() {
  return (
    <RequiereReportes permiso={P.reportesConsultas}>
      <Suspense fallback={null}>
        <Contenido />
      </Suspense>
    </RequiereReportes>
  );
}
