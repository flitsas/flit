"use client";

import { useMemo, useState } from "react";
import { Check, Search, Settings2 } from "lucide-react";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { ActionsMenu } from "@/components/atom/ActionsMenu";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import type { TransitOffice } from "@/lib/api/types";
import { ApiValidationError } from "@/lib/api/types";

/** Estado operativo de un OT en FLIT (HU #10518) para bloquear habilitación. */
export interface OtOperationalInfo {
  hasTenant: boolean;
  estadoActivo: boolean | null;
}

// Tabla consolidada de Organismos de Tránsito (HU #10194 — consolidación de las 3 matrices
// apiladas: grants, restricciones de consulta y políticas de bloqueo — en UNA tabla).
// Columnas: Organismo, Código, Estado (switch de grant) y Acciones (menú "⋯" con las
// configuraciones scoped a ese OT). Semántica de tabla real vía `DataTable` (HU #10844):
// `<table>/<thead>/<tbody>` con `scope="col"`, overflow-x auto y los 4 estados de UI.
//
// La columna Estado REUTILIZA la lógica que antes vivía en `OTMatrix.tsx`: UI optimista +
// rollback, indicador "Guardando…/Guardado" y bloqueo de habilitación cuando el OT no es
// operable (`operationalById`) con su badge; quitar el grant siempre se permite (HU #10518).
export interface OTConfigTableProps {
  offices: TransitOffice[];
  grantedIds: string[];
  /** Estado operativo por OT (id → info). Ausente = se asume operable (no bloquea). */
  operationalById?: Record<string, OtOperationalInfo>;
  /** Persiste el cambio de grant (POST si enabled, DELETE si !enabled). */
  onToggleGrant: (officeId: string, enabled: boolean) => Promise<void>;
  /**
   * Ids de OT con los que la compañía tiene CONVENIO comercial. Distinto de `grantedIds`: aquel
   * habilita la radicación, este decide si el contrato de mandato lleva bloque de firma del mandatario.
   */
  agreementIds?: string[];
  /** Persiste el cambio de convenio. Sin este callback la columna no se pinta. */
  onToggleAgreement?: (officeId: string, active: boolean) => Promise<void>;
  /** Abre el modal unificado de configuración (bloqueos + restricciones) scoped a ese OT. */
  onOpenConfig: (office: TransitOffice) => void;
  /** Notificación opcional de error de persistencia (toast). */
  onError?: (message: string) => void;
}

/** Deriva si un OT es operable y, si no, la razón para el badge. */
function operability(
  info: OtOperationalInfo | undefined,
): { operable: boolean; reason: string | null } {
  if (!info) {
    return { operable: true, reason: null };
  }
  if (!info.hasTenant) {
    return { operable: false, reason: "Sin alta" };
  }
  if (info.estadoActivo !== true) {
    return { operable: false, reason: "Inactivo" };
  }
  return { operable: true, reason: null };
}

export function OTConfigTable({
  offices,
  grantedIds,
  agreementIds = [],
  onToggleAgreement,
  operationalById,
  onToggleGrant,
  onOpenConfig,
  onError,
}: OTConfigTableProps) {
  const [search, setSearch] = useState("");
  const [granted, setGranted] = useState<Set<string>>(() => new Set(grantedIds));
  const [pending, setPending] = useState<Set<string>>(() => new Set());
  // IDs con confirmación "Guardado" visible unos segundos tras persistir con éxito.
  const [justSaved, setJustSaved] = useState<Set<string>>(() => new Set());
  const [agreements, setAgreements] = useState<Set<string>>(() => new Set(agreementIds));
  const [pendingAgreement, setPendingAgreement] = useState<Set<string>>(() => new Set());

  /** Conmuta el convenio con UI optimista y rollback, igual que el grant. */
  const handleToggleAgreement = async (office: TransitOffice) => {
    const activar = !agreements.has(office.id);
    setAgreements((current) => {
      const next = new Set(current);
      if (activar) next.add(office.id);
      else next.delete(office.id);
      return next;
    });
    setPendingAgreement((current) => new Set(current).add(office.id));

    try {
      await onToggleAgreement!(office.id, activar);
    } catch {
      setAgreements((current) => {
        const next = new Set(current);
        if (activar) next.delete(office.id);
        else next.add(office.id);
        return next;
      });
      onError?.(`No se pudo ${activar ? "marcar" : "quitar"} el convenio con ${office.name}.`);
    } finally {
      setPendingAgreement((current) => {
        const next = new Set(current);
        next.delete(office.id);
        return next;
      });
    }
  };

  const filtered = useMemo(() => {
    const term = fold(search);
    if (!term) {
      return offices;
    }
    return offices.filter((o) => fold(o.name).includes(term) || fold(o.code).includes(term));
  }, [offices, search]);

  const handleToggleGrant = async (office: TransitOffice) => {
    const enable = !granted.has(office.id);

    // UI optimista.
    setGranted((current) => {
      const next = new Set(current);
      if (enable) {
        next.add(office.id);
      } else {
        next.delete(office.id);
      }
      return next;
    });
    setPending((current) => new Set(current).add(office.id));

    try {
      await onToggleGrant(office.id, enable);
      setJustSaved((current) => new Set(current).add(office.id));
      setTimeout(() => {
        setJustSaved((current) => {
          const next = new Set(current);
          next.delete(office.id);
          return next;
        });
      }, 2500);
    } catch (err) {
      // Rollback.
      setGranted((current) => {
        const next = new Set(current);
        if (enable) {
          next.delete(office.id);
        } else {
          next.add(office.id);
        }
        return next;
      });
      // HU #10518: si el backend rechaza el grant (422), mostrar su mensaje (OT sin
      // alta / inactivo); si no, el fallback genérico.
      const serverMessage =
        err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      onError?.(
        serverMessage ?? `No se pudo ${enable ? "habilitar" : "deshabilitar"} ${office.name}.`,
      );
    } finally {
      setPending((current) => {
        const next = new Set(current);
        next.delete(office.id);
        return next;
      });
    }
  };

  const columns: DataTableColumn<TransitOffice>[] = [
    {
      key: "name",
      header: "Organismo",
      render: (office) => <span className="font-semibold">{office.name}</span>,
    },
    {
      key: "code",
      header: "Código",
      render: (office) => <span className="font-mono opacity-60">{office.code}</span>,
    },
    {
      key: "estado",
      header: "Estado",
      render: (office) => {
        const checked = granted.has(office.id);
        const { operable, reason } = operability(operationalById?.[office.id]);
        // No se puede habilitar un OT no operable; si ya está habilitado, sí se permite
        // desmarcar (quitar el grant no tiene restricción — HU #10518).
        const blockEnable = !operable && !checked;
        const isPending = pending.has(office.id);
        const rowDisabled = isPending || blockEnable;
        const reasonId = `ot-${office.id}-reason`;
        return (
          <div className="flex flex-wrap items-center gap-2">
            <SwitchToggle
              checked={checked}
              disabled={rowDisabled}
              describedById={reason ? reasonId : undefined}
              onChange={() => void handleToggleGrant(office)}
              label={`${checked ? "Deshabilitar" : "Habilitar"} ${office.name}`}
            />
            {isPending ? (
              <span className="text-[10px] opacity-60">Guardando…</span>
            ) : justSaved.has(office.id) ? (
              <span
                role="status"
                className="flex items-center gap-1 text-[10px] font-semibold"
                style={{ color: "#0a8f8b" }}
              >
                <Check className="h-3 w-3" /> Guardado
              </span>
            ) : null}
            {reason && (
              <span
                id={reasonId}
                className="inline-block rounded-full border px-2 py-0.5 text-[10px] font-semibold"
                style={{ color: "#b25a00", borderColor: "#f0c38e", background: "#fff7ed" }}
              >
                {reason} en FLIT
              </span>
            )}
          </div>
        );
      },
    },
    // El convenio solo se ofrece si el contenedor sabe persistirlo. No depende del grant: son cosas
    // distintas y una compañía puede tener acuerdo comercial sin estar habilitada para radicar todavía.
    ...(onToggleAgreement
      ? [
          {
            key: "convenio",
            header: "Convenio",
            render: (office: TransitOffice) => {
              const checked = agreements.has(office.id);
              const isPending = pendingAgreement.has(office.id);
              return (
                <div className="flex flex-wrap items-center gap-2">
                  <SwitchToggle
                    checked={checked}
                    disabled={isPending}
                    onChange={() => void handleToggleAgreement(office)}
                    label={`${checked ? "Quitar" : "Marcar"} convenio con ${office.name}`}
                  />
                  {isPending && <span className="text-[10px] opacity-60">Guardando…</span>}
                </div>
              );
            },
          } satisfies DataTableColumn<TransitOffice>,
        ]
      : []),
    {
      key: "acciones",
      header: "Acciones",
      align: "right",
      render: (office) => {
        // Bloqueos y restricciones de consulta solo aplican a OT habilitados (mismo
        // criterio que antes: las matrices de bloqueos/restricciones solo listaban OT
        // habilitados; ahora se refleja como acción deshabilitada con motivo).
        const enabledForConfig = granted.has(office.id);
        return (
          <ActionsMenu
            ariaLabel={`Acciones para ${office.name}`}
            items={[
              {
                key: "config",
                label: "Configurar",
                icon: Settings2,
                onSelect: () => onOpenConfig(office),
                disabled: !enabledForConfig,
                disabledReason: "Habilita este organismo para configurar bloqueos y restricciones.",
              },
            ]}
          />
        );
      },
    },
  ];

  return (
    <section aria-label="Organismos de tránsito" className="space-y-3">
      <div>
        <h4 className="text-sm font-semibold">Organismos de Tránsito</h4>
        <p className="mt-0.5 text-[11px] opacity-60">
          Habilita cada organismo y, desde «⋯ Acciones», configura sus bloqueos y restricciones de
          consulta. Los cambios se guardan al instante; no requieren «Guardar todo».
        </p>
      </div>

      <div className="flex max-w-md items-center gap-2 rounded-xl border bg-white p-2.5 dark:bg-[#0B0F14]">
        <Search className="h-4 w-4 opacity-60" />
        <label htmlFor="ot-config-search" className="sr-only">
          Buscar organismo de tránsito
        </label>
        <input
          id="ot-config-search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Buscar por nombre o código…"
          className="flex-1 bg-transparent text-xs outline-none"
        />
      </div>

      <DataTable
        columns={columns}
        rows={filtered}
        getRowKey={(office) => office.id}
        emptyMessage="Ningún organismo coincide con la búsqueda."
        ariaLabel="Organismos de tránsito"
        minWidth={640}
      />
    </section>
  );
}

/** Normaliza para comparación: minúsculas + sin tildes españolas. */
function fold(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[áàäâã]/g, "a")
    .replace(/[éèëê]/g, "e")
    .replace(/[íìïî]/g, "i")
    .replace(/[óòöôõ]/g, "o")
    .replace(/[úùüû]/g, "u")
    .replace(/ñ/g, "n");
}
