"use client";

import { useEffect, useMemo, useState } from "react";
import { MultiSelectBuscable, type OpcionSeleccionable } from "@/components/atom/MultiSelectBuscable";
import {
  fetchCompanyAssociableCompanies,
  fetchOtAssociableCompanies,
  type AssociableCompany,
  type MandateSignerOfficeCompanies,
} from "@/lib/api/admin-mandate-signers";

/**
 * De dónde salen las compañías que se pueden asociar (HU #13181): el OT y el Super Admin buscan entre
 * todas las compañías activas; el Admin de Compañía solo ve sus hijas.
 */
export type FuenteAsociadas =
  | { modo: "ot"; transitOfficeId: string }
  | { modo: "hijas"; tenantId: string; networkHeadId?: string | null };

/** Compañía marcada. Sin `name` cuando viene de un mandatario guardado y aún no se vio en la lista. */
export interface AsociadaSeleccionada {
  id: string;
  name?: string;
  nit?: string;
}

/**
 * Selección inicial al editar: con nombre y NIT cuando el servidor los trae; solo con id (respaldo
 * «Compañía asociada») si no.
 */
export function precargarAsociadas(
  officeCompanies?: MandateSignerOfficeCompanies[],
): Record<string, AsociadaSeleccionada> {
  const out: Record<string, AsociadaSeleccionada> = {};
  for (const o of officeCompanies ?? []) {
    for (const c of o.associatedCompanies ?? []) {
      if (c.id) out[c.id] = { id: c.id, name: c.name || undefined, nit: c.nit || undefined };
    }
    for (const id of o.associatedCompanyTenantIds ?? []) {
      if (!out[id]) out[id] = { id };
    }
  }
  return out;
}

export function etiquetaAsociada(c: AsociadaSeleccionada): string {
  if (!c.name) return "Compañía asociada";
  return c.nit ? `${c.name} · NIT ${c.nit}` : c.name;
}

export const TEXTO_SOLO_SU_COMPANIA = "Este mandatario aplica solo a su compañía";

/**
 * HU #13181 — compañías asociadas del formulario del mandatario, según el perfil. Solo nombre y NIT
 * (Habeas Data). HU #13248b: selector múltiple con buscador (lista completa, sin paginar).
 * `onSinRed` avisa al formulario cuando no hay lista (Admin de Compañía sin hijas).
 */
export function MandatarioCompaniasAsociadas({
  fuente,
  seleccion,
  onChange,
  excluirIds = [],
  errores = {},
  onSinRed,
  ayuda,
}: {
  fuente: FuenteAsociadas;
  seleccion: Record<string, AsociadaSeleccionada>;
  onChange: (next: Record<string, AsociadaSeleccionada>) => void;
  /** Compañía propia del mandatario: no se ofrece (el servidor la rechazaría). */
  excluirIds?: string[];
  /** Motivo 422 por compañía (id de tenant → mensaje). */
  errores?: Record<string, string>;
  onSinRed?: (sinRed: boolean) => void;
  /** Texto bajo el selector. Por defecto explica que, sin selección, aplica solo a su compañía. */
  ayuda?: string;
}) {
  const [companias, setCompanias] = useState<AssociableCompany[] | null>(null);
  const [soloPropia, setSoloPropia] = useState(false);
  const [error, setError] = useState(false);
  const [reintento, setReintento] = useState(0);

  const clave = fuente.modo === "ot" ? `ot:${fuente.transitOfficeId}` : `hijas:${fuente.tenantId}:${fuente.networkHeadId ?? ""}`;

  useEffect(() => {
    const ctrl = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial de la lista completa
    setError(false);
    setCompanias(null);
    void (async () => {
      try {
        const res =
          fuente.modo === "ot"
            ? await fetchOtAssociableCompanies(fuente.transitOfficeId, { all: true }, ctrl.signal)
            : await fetchCompanyAssociableCompanies(
                fuente.tenantId,
                { all: true },
                ctrl.signal,
                fuente.networkHeadId,
              );
        if (ctrl.signal.aborted) return;
        setCompanias(res.items);
        setSoloPropia(fuente.modo === "hijas" && (res.aplicaSoloASuCompania || res.items.length === 0));
      } catch {
        if (!ctrl.signal.aborted) setError(true);
      }
    })();
    return () => ctrl.abort();
    // `fuente` se resume en `clave`: un objeto nuevo con los mismos datos no debe volver a pedir la lista.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clave, reintento]);

  useEffect(() => {
    if (companias) onSinRed?.(soloPropia);
  }, [companias, soloPropia, onSinRed]);

  const opciones = useMemo<OpcionSeleccionable[]>(
    () =>
      (companias ?? [])
        .filter((c) => !excluirIds.includes(c.id))
        .map((c) => ({ id: c.id, label: c.name, detalle: `NIT ${c.nit}`, codigo: c.nit })),
    [companias, excluirIds],
  );

  const elegidas = useMemo<OpcionSeleccionable[]>(
    () =>
      Object.values(seleccion).map((c) => {
        const conocida = opciones.find((o) => o.id === c.id);
        return (
          conocida ?? {
            id: c.id,
            label: c.name ?? "Compañía asociada",
            detalle: c.nit ? `NIT ${c.nit}` : undefined,
            codigo: c.nit,
          }
        );
      }),
    [seleccion, opciones],
  );

  const alCambiar = (next: OpcionSeleccionable[]) => {
    const out: Record<string, AsociadaSeleccionada> = {};
    for (const o of next) {
      const previa = seleccion[o.id];
      out[o.id] = previa ?? { id: o.id, name: o.label, nit: o.codigo };
    }
    onChange(out);
  };

  if (soloPropia && !error && companias) {
    return (
      <p className="text-xs leading-tight opacity-70" data-testid="mandatario-asociadas-sin-red">
        {TEXTO_SOLO_SU_COMPANIA}
      </p>
    );
  }

  return (
    <div className="space-y-2" data-testid={fuente.modo === "ot" ? "mandatario-asociadas-ot" : "mandatario-asociadas-hijas"}>
      <MultiSelectBuscable
        testId="mandatario-asociadas"
        grupo="Compañías"
        opciones={opciones}
        seleccion={elegidas}
        onChange={alCambiar}
        buscarLabel="Buscar compañía por nombre o NIT"
        loading={!companias && !error}
        error={error}
        onRetry={() => setReintento((n) => n + 1)}
        textoCargando="Cargando compañías…"
        textoError="No se pudieron cargar las compañías."
        textoVacio="No hay compañías para asociar."
        errores={errores}
      />
      <p className="text-xs leading-tight opacity-70">{ayuda ?? TEXTO_OPCIONAL}</p>
    </div>
  );
}

const TEXTO_OPCIONAL = "Opcional. Sin selección, el mandatario aplica solo a su compañía.";
