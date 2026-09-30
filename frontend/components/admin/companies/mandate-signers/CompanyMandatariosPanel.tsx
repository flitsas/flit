"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Pencil, RotateCcw, UserX } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Pagination } from "@/components/atom/Pagination";
import { RowActions } from "@/components/atom/RowActions";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import {
  createCompanyMandateSigner,
  fetchCompanyMandateSigners,
  fetchCompanyTransitOffices,
  inactivateCompanyMandateSigner,
  reactivateCompanyMandateSigner,
  updateCompanyMandateSigner,
  type CompanyMandateSignerInput,
  type CompanyTransitOfficeOption,
  type MandateSigner,
} from "@/lib/api/admin-mandate-signers";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload, isSuperAdmin } from "@/lib/auth/jwt";
import { formatDocumentWithType } from "@/lib/display/document-number";
import {
  motivoSinFirma,
  organismosSinMedioDeFirma,
} from "@/lib/plataforma/mandatario-firma";
import { etiquetaModelo, modeloDe } from "@/lib/plataforma/mandatario-vigencia";
import { rlPrimaryCtaClass, rlPrimaryCtaStyle } from "../legal-representatives/rl-flit-styles";
import { CompanyMandatarioForm } from "./CompanyMandatarioForm";
import type { FuenteAsociadas } from "./MandatarioCompaniasAsociadas";
import { MandatarioVigenciaBadge } from "./MandatarioVigenciaBadge";

/**
 * HU #11202 — mandatarios gestionados desde el configurador de la COMPAÑÍA.
 *
 * Antes los registraba cada organismo de tránsito y elegía a qué compañías aplicaban. Se invierte: la
 * empresa da de alta a su mandatario una sola vez y marca en cuáles de sus organismos aplica, que es
 * como funciona en la práctica —el mandatario es de la empresa, no del organismo—.
 */
export function CompanyMandatariosPanel({
  tenantId,
  networkHeadId,
}: {
  tenantId: string;
  networkHeadId?: string | null;
}) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  // Bug #13055 — paginación en cliente con filas por página, como el listado de trámites.
  const pg = usePaginacion();
  const [signers, setSigners] = useState<MandateSigner[]>([]);
  const [offices, setOffices] = useState<CompanyTransitOfficeOption[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<MandateSigner | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [signerList, officeList] = await Promise.all([
          fetchCompanyMandateSigners(tenantId, signal, networkHeadId),
          fetchCompanyTransitOffices(tenantId, signal, networkHeadId),
        ]);
        if (signal?.aborted) {
          return;
        }
        setSigners(signerList);
        setOffices(officeList);
        setStatus(signerList.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [tenantId, networkHeadId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const officeNameById = useMemo(
    () => new Map(offices.map((o) => [o.transitOfficeId, o.name])),
    [offices],
  );

  const handleSubmit = async (input: CompanyMandateSignerInput) => {
    const saved = editing
      ? await updateCompanyMandateSigner(tenantId, editing.id, input, networkHeadId)
      : await createCompanyMandateSigner(tenantId, input, networkHeadId);
    setFormOpen(false);
    setEditing(null);
    show(editing ? "Mandatario actualizado." : "Mandatario registrado.", "success");
    await load();
    return saved;
  };

  const handleToggleActivo = async (signer: MandateSigner) => {
    setBusyId(signer.id);
    try {
      if (signer.isActive) {
        await inactivateCompanyMandateSigner(tenantId, signer.id, networkHeadId);
        show(`${signer.fullName} quedó inactivo.`, "success");
      } else {
        await reactivateCompanyMandateSigner(tenantId, signer.id, networkHeadId);
        show(`${signer.fullName} vuelve a estar activo.`, "success");
      }
      await load();
    } catch {
      show("No se pudo cambiar el estado del mandatario.", "error");
    } finally {
      setBusyId(null);
    }
  };

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };

  /**
   * HU #11717 — organismos donde el mandatario está habilitado pero no podría firmar. Se calcula con
   * la misma regla que impone el backend al parametrizar, para que la consola no diga una cosa y el
   * guardado otra. Solo aplica a la Persona natural: la jurídica y el formato en blanco no firman.
   */
  const sinFirmaPorSigner = (signer: MandateSigner) =>
    modeloDe(signer) === "natural" && signer.isActive
      ? organismosSinMedioDeFirma(signer.transitOfficeIds ?? [], signer)
      : [];

  const sinOrganismos = offices.length === 0;

  // HU #13181 — lista de compañías asociables según el perfil: el Super Admin busca entre todas (por
  // la ruta del organismo); el Admin de Compañía ve solo sus hijas.
  const [esSuperAdmin] = useState(() => isSuperAdmin(decodeJwtPayload(getToken())));
  const asociadas: FuenteAsociadas | undefined =
    esSuperAdmin && offices[0]
      ? { modo: "ot", transitOfficeId: offices[0].transitOfficeId }
      : undefined;

  return (
    <div className="space-y-4">
      {sinOrganismos && (
        <p
          className="rounded-xl border px-3 py-2 text-xs"
          style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)", color: "#8a6000" }}
          role="status"
        >
          Esta compañía todavía no tiene organismos de tránsito habilitados. Habilítalos en la matriz
          de organismos antes de registrar mandatarios: sin organismo no hay dónde aplicarlos.
        </p>
      )}

      <div className="flex justify-end">
        <button
          type="button"
          className={rlPrimaryCtaClass}
          style={rlPrimaryCtaStyle}
          onClick={openCreate}
          disabled={sinOrganismos}
        >
          Nuevo mandatario
        </button>
      </div>

      {/* Bug #13055 — tabla homologada con la de Trámites: loader del carrito, cabecera y filas de
          table-styles y acciones con RowActions (antes: gris genérico y botones de texto). */}
      {status === "loading" ? (
        <CarLoaderModal label="Cargando mandatarios…" />
      ) : (
      <UiStateBoundary
        status={status}
        emptyMessage="Esta compañía no tiene mandatarios registrados."
        errorMessage="No se pudieron cargar los mandatarios."
        onRetry={() => void load()}
        skeletonRows={4}
      >
        <div className="overflow-x-auto">
          <table
            className="text-xs"
            style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
          >
            <caption className="sr-only">Mandatarios de la compañía</caption>
            <thead>
              <tr>
                <th
                  scope="col"
                  className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`}
                  style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                >
                  Mandatario
                </th>
                <th
                  scope="col"
                  className={`${TABLA_HEADER_CELL_CLS}`}
                  style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                >
                  Documento
                </th>
                <th
                  scope="col"
                  className={`${TABLA_HEADER_CELL_CLS}`}
                  style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                >
                  Modelo
                </th>
                <th
                  scope="col"
                  className={`${TABLA_HEADER_CELL_CLS}`}
                  style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                >
                  Vigencia
                </th>
                <th
                  scope="col"
                  className={`${TABLA_HEADER_CELL_CLS}`}
                  style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                >
                  Organismos
                </th>
                <th
                  scope="col"
                  className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`}
                  style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                >
                  Acciones
                </th>
              </tr>
            </thead>
            <tbody>
              {pg.paginar(signers).map((signer) => (
                <tr key={signer.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                  <td className={`rounded-l-xl border-y border-l px-4 py-3 ${signer.isActive ? "" : "opacity-60"}`} style={{ borderColor: "#DFE5ED" }}>
                    <span className="font-semibold">{signer.fullName}</span>
                  </td>
                  <td className={`border-y px-4 py-3 font-mono ${signer.isActive ? "" : "opacity-60"}`} style={{ borderColor: "#DFE5ED" }}>
                    {formatDocumentWithType(signer.documentType, signer.documentNumber)}
                  </td>
                  <td className={`border-y px-4 py-3 ${signer.isActive ? "" : "opacity-60"}`} style={{ borderColor: "#DFE5ED" }}>
                    {etiquetaModelo(signer)}
                  </td>
                  <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                    <MandatarioVigenciaBadge signer={signer} />
                  </td>
                  <td className={`border-y px-4 py-3 ${signer.isActive ? "" : "opacity-60"}`} style={{ borderColor: "#DFE5ED" }}>
                    {(signer.transitOfficeIds ?? []).length === 0
                      ? "—"
                      : (signer.transitOfficeIds ?? [])
                          .map((id) => officeNameById.get(id) ?? id)
                          .join(", ")}
                    {/* HU #11717 — se SEÑALA, no se inhabilita: los trámites en curso siguen
                        emitiendo su mandato como hoy. */}
                    {sinFirmaPorSigner(signer).length > 0 && (
                      <div
                        className="mt-1 text-[11px] leading-tight"
                        style={{ color: "#E5484D" }}
                        title={motivoSinFirma(signer)}
                      >
                        No puede firmar en{" "}
                        {sinFirmaPorSigner(signer)
                          .map((id) => officeNameById.get(id) ?? id)
                          .join(", ")}
                        : {motivoSinFirma(signer).toLowerCase()}
                      </div>
                    )}
                  </td>
                  <td className="rounded-r-xl border-y border-r px-4 py-3 text-right" style={{ borderColor: "#DFE5ED" }}>
                    <RowActions
                      actions={[
                        {
                          icon: Pencil,
                          label: `Editar mandatario ${signer.fullName}`,
                          onClick: () => {
                            setEditing(signer);
                            setFormOpen(true);
                          },
                          tone: "primary",
                        },
                        signer.isActive
                          ? {
                              icon: UserX,
                              label: `Inactivar mandatario ${signer.fullName}`,
                              onClick: () => void handleToggleActivo(signer),
                              tone: "danger",
                              disabled: busyId === signer.id,
                            }
                          : {
                              icon: RotateCcw,
                              label: `Reactivar mandatario ${signer.fullName}`,
                              onClick: () => void handleToggleActivo(signer),
                              disabled: busyId === signer.id,
                            },
                      ]}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <Pagination
          page={pg.page}
          pageSize={pg.pageSize}
          totalCount={signers.length}
          onPageChange={pg.setPage}
          onPageSizeChange={pg.setPageSize}
          noun="mandatarios"
        />
      </UiStateBoundary>
      )}

      {formOpen && (
        <CompanyMandatarioForm
          tenantId={tenantId}
          networkHeadId={networkHeadId}
          offices={offices}
          asociadas={asociadas}
          ownerCompanyIds={[tenantId]}
          editing={editing}
          onCancel={() => {
            setFormOpen(false);
            setEditing(null);
          }}
          onSubmit={handleSubmit}
        />
      )}
    </div>
  );
}
