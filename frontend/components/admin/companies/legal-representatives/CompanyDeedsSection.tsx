"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Eye, FileText, Loader2, RefreshCw } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { RowActions } from "@/components/atom/RowActions";
import { Modal } from "@/components/atom/Modal";
import {
  fetchDeedDetail,
  fetchDeeds,
  fetchRepresentedCompanies,
  saveDeed,
  type DeedFormInput,
  type DeedItem,
  type DeedSaved,
  type RepresentedCompany,
} from "@/lib/api/admin-deeds";
import { DeedsFormPanel, type DeedEditingRef } from "../deeds/DeedsFormPanel";
import { deedVigenciaLabel, deedVigenciaTone } from "@/components/operacion/ActiveDeedsCollapse";
import { formatFecha } from "@/lib/format/date";

/**
 * HU #11063 — escrituras por compañía como SECCIÓN PROPIA de la pestaña de representantes legales.
 *
 * El backend ya soportaba todo (vigencia, varias compañías por escritura, custodia del PDF): lo que
 * fallaba era el recorrido. El alta/edición vivía DENTRO del detalle de cada representante (pestaña →
 * fila → modal → escrituras), así que para subir una escritura había que saber por qué representante
 * entrar. Aquí se ve el panorama completo por compañía y se carga o reemplaza en un paso.
 *
 * El detalle del representante mantiene su propia vista de escrituras: sigue siendo útil para ver
 * "qué escrituras asoció ESTE representante". Esta sección responde la otra pregunta, que es la que el
 * negocio hacía: "¿qué compañías tienen escritura al día?".
 */

/** Una fila de la tabla: el par (compañía, escritura). Una escritura M:N aparece en cada compañía. */
interface DeedRow {
  company: RepresentedCompany;
  deed: DeedItem;
}

/** Compañía sin ninguna escritura registrada: se lista igual, porque es justo lo que hay que resolver. */
interface EmptyRow {
  company: RepresentedCompany;
  deed: null;
}

type Row = DeedRow | EmptyRow;

/** Días entre hoy (día calendario de Colombia) y la fecha de fin de vigencia. */
function diasRestantes(vigenciaHasta: string, hoy: Date): number {
  const hasta = new Date(`${vigenciaHasta}T00:00:00-05:00`);
  const ms = hasta.getTime() - hoy.getTime();
  return Math.ceil(ms / 86_400_000);
}

/**
 * Estado de vigencia de una escritura contra hoy. Mismo vocabulario que el detalle del representante
 * (`deedEstadoBadge`), recalculado aquí porque el listado de escrituras del tenant no lo proyecta.
 */
function estadoDe(deed: DeedItem, hoy: Date): { tone: "success" | "danger" | "info" | "neutral"; label: string } {
  if (!deed.isActive) return { tone: "neutral", label: "Inactiva" };
  const inicio = new Date(`${deed.vigenciaDesde}T00:00:00-05:00`);
  if (inicio.getTime() > hoy.getTime()) return { tone: "info", label: "Programada" };
  return diasRestantes(deed.vigenciaHasta, hoy) < 0
    ? { tone: "danger", label: "Vencida" }
    : { tone: "success", label: "Vigente" };
}

export function CompanyDeedsSection({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [deeds, setDeeds] = useState<DeedItem[]>([]);
  const [companies, setCompanies] = useState<RepresentedCompany[]>([]);
  // Compañía elegida para el alta/reemplazo; abre el DeedsFormPanel con ella fija.
  const [formCompany, setFormCompany] = useState<RepresentedCompany | null>(null);
  const [formEditing, setFormEditing] = useState<DeedEditingRef | null>(null);
  // Selector de compañía previo al alta (solo cuando hay más de una).
  const [pickerOpen, setPickerOpen] = useState(false);
  const [viewing, setViewing] = useState<string | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [page, companyList] = await Promise.all([
          fetchDeeds(tenantId, 1, 200, signal),
          fetchRepresentedCompanies(tenantId, signal),
        ]);
        if (signal?.aborted) return;
        setDeeds(page.data);
        setCompanies(companyList);
        setStatus(companyList.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  // Filas por compañía: sus escrituras (más reciente primero) o una fila vacía si no tiene ninguna.
  const rows = useMemo<Row[]>(() => {
    const out: Row[] = [];
    for (const company of companies) {
      const suyas = deeds
        .filter((d) => d.representedCompanyIds.includes(company.id))
        .sort((a, b) => b.vigenciaHasta.localeCompare(a.vigenciaHasta));
      if (suyas.length === 0) {
        out.push({ company, deed: null });
      } else {
        for (const deed of suyas) out.push({ company, deed });
      }
    }
    return out;
  }, [companies, deeds]);

  const hoy = useMemo(() => new Date(), []);

  const openAlta = () => {
    if (companies.length === 1) {
      setFormEditing(null);
      setFormCompany(companies[0]);
      return;
    }
    setPickerOpen(true);
  };

  const openReemplazo = (row: DeedRow) => {
    setFormEditing({
      id: row.deed.id,
      description: row.deed.description,
      vigenciaDesde: row.deed.vigenciaDesde,
      vigenciaHasta: row.deed.vigenciaHasta,
    });
    setFormCompany(row.company);
  };

  const closeForm = () => {
    setFormCompany(null);
    setFormEditing(null);
  };

  const handleSubmit = (input: DeedFormInput): Promise<DeedSaved> =>
    saveDeed(tenantId, formEditing ? formEditing.id : null, input);

  const handleSaved = () => {
    const editaba = formEditing !== null;
    closeForm();
    show(editaba ? "Escritura actualizada." : "Escritura cargada.", "success");
    void load();
  };

  // Abre el PDF en una pestaña nueva con la URL prefirmada del backend (vida corta).
  const handleVer = async (deedId: string) => {
    setViewing(deedId);
    try {
      const detail = await fetchDeedDetail(tenantId, deedId);
      if (detail.viewUrl) {
        window.open(detail.viewUrl, "_blank", "noopener,noreferrer");
      } else {
        show("La escritura no tiene un PDF disponible para ver.", "error");
      }
    } catch {
      show("No se pudo abrir la escritura.", "error");
    } finally {
      setViewing(null);
    }
  };

  return (
    <div className="space-y-3">
      {/* El título de la sección lo pone la pestaña (`RepresentativesAndVaultTab`): aquí solo la acción. */}
      <div className="flex items-center justify-between gap-2">
        <p className="text-[11px] opacity-60">
          Estado de vigencia de la escritura de cada compañía representada.
        </p>
        <button
          type="button"
          onClick={openAlta}
          disabled={companies.length === 0}
          className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: "#557EFF" }}
          title={
            companies.length === 0
              ? "Registra primero un representante legal con su compañía."
              : undefined
          }
        >
          Cargar escritura
        </button>
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="Aún no hay compañías registradas: se derivan del directorio de representantes legales."
        errorMessage="No se pudieron cargar las escrituras."
        onRetry={() => void load()}
        skeletonRows={3}
      >
        <div className="overflow-x-auto">
          <table className="w-full border-separate border-spacing-y-2 text-xs">
            <thead>
              <tr className="text-left text-[10px] font-semibold uppercase text-foreground">
                <th className="px-4 py-2">Compañía</th>
                <th className="px-4 py-2">Escritura</th>
                <th className="px-4 py-2">Vigencia</th>
                <th className="px-4 py-2">Estado</th>
                <th className="px-4 py-2 text-right">Acciones</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const key = `${row.company.id}-${row.deed?.id ?? "sin"}`;
                return (
                  <tr key={key}>
                    <td
                      className="rounded-l-xl border-y border-l border-[#DFE5ED] px-4 py-3 dark:border-white/10"
                    >
                      <span className="block font-semibold">{row.company.name}</span>
                      <span className="block font-mono text-[10px] opacity-60">
                        NIT {row.company.nit}
                      </span>
                    </td>
                    {row.deed === null ? (
                      <>
                        <td
                          className="border-y border-[#DFE5ED] px-4 py-3 opacity-60 dark:border-white/10"
                          colSpan={3}
                        >
                          Sin escritura registrada
                        </td>
                        <td
                          className="rounded-r-xl border-y border-r border-[#DFE5ED] px-4 py-3 text-right dark:border-white/10"
                        >
                          <button
                            type="button"
                            onClick={() => {
                              setFormEditing(null);
                              setFormCompany(row.company);
                            }}
                            className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold"
                            style={{ color: "#557EFF", borderColor: "#557EFF" }}
                            aria-label={`Cargar escritura de ${row.company.name}`}
                          >
                            Cargar
                          </button>
                        </td>
                      </>
                    ) : (
                      <>
                        <td className="border-y border-[#DFE5ED] px-4 py-3 dark:border-white/10">
                          <span className="flex items-center gap-1.5">
                            <FileText className="h-3.5 w-3.5 shrink-0 opacity-50" aria-hidden="true" />
                            {row.deed.description}
                          </span>
                        </td>
                        <td
                          className="border-y border-[#DFE5ED] px-4 py-3 font-mono opacity-70 dark:border-white/10"
                        >
                          {formatFecha(row.deed.vigenciaDesde)} – {formatFecha(row.deed.vigenciaHasta)}
                        </td>
                        <td className="border-y border-[#DFE5ED] px-4 py-3 dark:border-white/10">
                          {(() => {
                            const estado = estadoDe(row.deed, hoy);
                            const dias = diasRestantes(row.deed.vigenciaHasta, hoy);
                            return (
                              <span className="flex flex-wrap items-center gap-1.5">
                                <StatusBadge tone={estado.tone} label={estado.label} />
                                {/* Días restantes solo mientras siga vigente: en una vencida el
                                    número sería un negativo sin significado para el gestor. */}
                                {estado.label === "Vigente" && (
                                  <StatusBadge
                                    tone={deedVigenciaTone(dias)}
                                    label={deedVigenciaLabel(dias)}
                                  />
                                )}
                              </span>
                            );
                          })()}
                        </td>
                        <td
                          className="rounded-r-xl border-y border-r border-[#DFE5ED] px-4 py-3 text-right dark:border-white/10"
                        >
                          <RowActions
                            actions={[
                              {
                                icon: viewing === row.deed.id ? Loader2 : Eye,
                                label: `Ver la escritura de ${row.company.name}`,
                                onClick: () => void handleVer(row.deed.id),
                                disabled: viewing === row.deed.id,
                              },
                              {
                                icon: RefreshCw,
                                label: `Reemplazar la escritura de ${row.company.name}`,
                                onClick: () => openReemplazo(row),
                                tone: "primary",
                              },
                            ]}
                          />
                        </td>
                      </>
                    )}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>

      {/* Elección de compañía previa al alta, solo con varias registradas. */}
      {pickerOpen && (
        <Modal
          open
          onClose={() => setPickerOpen(false)}
          title="¿De qué compañía es la escritura?"
          icon={FileText}
          size="sm"
        >
          <ul className="mt-2 space-y-2">
            {companies.map((c) => (
              <li key={c.id}>
                <button
                  type="button"
                  onClick={() => {
                    setPickerOpen(false);
                    setFormEditing(null);
                    setFormCompany(c);
                  }}
                  className="w-full rounded-xl border border-[#DFE5ED] px-3 py-2 text-left text-xs transition hover:border-[#557EFF] dark:border-white/10"
                >
                  <span className="block font-semibold">{c.name}</span>
                  <span className="block font-mono text-[10px] opacity-60">NIT {c.nit}</span>
                </button>
              </li>
            ))}
          </ul>
        </Modal>
      )}

      <DeedsFormPanel
        open={formCompany !== null}
        editing={formEditing}
        company={
          formCompany && { id: formCompany.id, name: formCompany.name, nit: formCompany.nit }
        }
        onClose={closeForm}
        onSubmit={handleSubmit}
        onSaved={handleSaved}
        onError={(message) => show(message, "error")}
      />
    </div>
  );
}
