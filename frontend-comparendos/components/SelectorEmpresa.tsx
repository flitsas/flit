"use client";

import { useState } from "react";
import { InlineAlert } from "@flit/ui";
import { apiFetch, ApiError } from "@/lib/api";
import type { EmpresaResumen } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";
import { ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Filtro maestro por gestora del SuperAdmin (ADO #12982): sin empresa elegida no se lista nada. Lee las empresas de
 * GET /admin/empresas; si ese endpoint todavía no existe, deja pegar el identificador de la empresa.
 */
export function SelectorEmpresa({
  valor,
  onCambio,
  etiquetaArriba = false,
  textoVacio = "Elige una empresa…",
}: {
  valor: string | null;
  onCambio: (id: string | null) => void;
  /**
   * Patrón de los filtros de Reportes (el de `GlobalFilters` de Trámites): la etiqueta encima del control, como las fechas,
   * para que la fila alinee por abajo. Mientras carga se ve el mismo control deshabilitado (sin salto de la fila).
   */
  etiquetaArriba?: boolean;
  /** Texto de la opción vacía (Reportes: «Todas las empresas»). */
  textoVacio?: string;
}) {
  const empresas = useCarga("empresas", async (signal) => {
    try {
      return await apiFetch<EmpresaResumen[]>("/admin/empresas", { signal });
    } catch (e) {
      if (e instanceof ApiError && (e.status === 404 || e.status === 405)) return null;
      throw e;
    }
  });
  const [texto, setTexto] = useState(valor ?? "");

  if (etiquetaArriba && (empresas.estado === "loading" || empresas.datos)) {
    const cargando = empresas.estado === "loading";
    return (
      <label className={ETIQUETA_CLS}>
        Empresa
        <select className={`${INPUT_CLS} w-full sm:w-72`} value={valor ?? ""} onChange={(e) => onCambio(e.target.value || null)} disabled={cargando}>
          <option value="">{cargando ? "Cargando empresas…" : textoVacio}</option>
          {(empresas.datos ?? []).map((e) => (
            <option key={e.id} value={e.id}>
              {e.nombre ?? e.id}
            </option>
          ))}
        </select>
      </label>
    );
  }

  if (empresas.estado === "loading") return <p className="text-xs opacity-60">Cargando empresas…</p>;

  if (empresas.datos) {
    return (
      <label className="flex items-center gap-2 text-xs font-semibold">
        Empresa
        <select className={`${INPUT_CLS} w-72`} value={valor ?? ""} onChange={(e) => onCambio(e.target.value || null)} aria-label="Empresa">
          <option value="">{textoVacio}</option>
          {empresas.datos.map((e) => (
            <option key={e.id} value={e.id}>
              {e.nombre ?? e.id}
            </option>
          ))}
        </select>
      </label>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      <label className="flex items-center gap-2 text-xs font-semibold">
        Empresa
        <input
          className={`${INPUT_CLS} w-96`}
          value={texto}
          placeholder="Identificador de la empresa"
          onChange={(e) => {
            setTexto(e.target.value);
            onCambio(UUID.test(e.target.value.trim()) ? e.target.value.trim() : null);
          }}
        />
      </label>
      {empresas.estado === "error" && <InlineAlert tone="error" compact>{empresas.error}</InlineAlert>}
      <span className="text-[11px] opacity-60">Pega el identificador de la empresa: el listado de empresas aún no está disponible.</span>
    </div>
  );
}
