"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Pencil, RotateCcw, Send, Trash2, UserX } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Pagination } from "@/components/atom/Pagination";
import { RowActionsMenu } from "@/components/atom/RowActionsMenu";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import {
  createCompanyMandateSigner,
  deleteCompanyMandateSigner,
  fetchCompanyMandateSignerImpact,
  fetchCompanyMandateSigners,
  fetchCompanyTransitOffices,
  inactivateCompanyMandateSigner,
  reactivateCompanyMandateSigner,
  resendCompanyMandateSignerIdentity,
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
import {
  puedeCrearMandatarios,
  puedeEditarMandatario,
  puedeEliminarMandatario,
  tieneCandadoDelOrganismo,
} from "@/lib/plataforma/mandatario-permisos";
import {
  mensajeErrorAccion,
  mensajeResultadoBaja,
  mensajeResultadoReactivar,
  type AccionBaja,
} from "@/lib/plataforma/mandatario-baja";
import {
  mensajeErrorReenvio,
  mensajeReenvio,
  mensajeValidacionTrasGuardar,
  presentarValidacion,
  puedeReenviarValidacion,
  requiereValidacionPropia,
} from "@/lib/plataforma/mandatario-validacion";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { rlPrimaryCtaClass, rlPrimaryCtaStyle } from "../legal-representatives/rl-flit-styles";
import { CompanyMandatarioForm } from "./CompanyMandatarioForm";
import { MandatarioBajaDialog } from "./MandatarioBajaDialog";
import { MandatarioCandado } from "./MandatarioCandado";
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
  // HU #13140 — diálogo de confirmación previa para desactivar o eliminar.
  const [baja, setBaja] = useState<{ signer: MandateSigner; accion: AccionBaja } | null>(null);
  // HU #13139 — el Gestor/Radicador no ve crear, editar ni eliminar.
  const [canCreate] = useState(() => puedeCrearMandatarios(decodeJwtPayload(getToken())));

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
    const base = editing ? "Mandatario actualizado." : "Mandatario registrado.";
    const validacion = mensajeValidacionTrasGuardar(saved, input.email);
    show(validacion ? `${base} ${validacion}` : base, saved.identity === "failed" ? "error" : "success");
    await load();
    return saved;
  };

  // HU #13140 — la baja (desactivar o eliminar) la confirma el diálogo, que ya consultó el impacto.
  const ejecutarBaja = async (signer: MandateSigner, accion: AccionBaja, confirmarImpacto: boolean) => {
    const outcome =
      accion === "eliminar"
        ? await deleteCompanyMandateSigner(tenantId, signer.id, confirmarImpacto, networkHeadId)
        : await inactivateCompanyMandateSigner(tenantId, signer.id, networkHeadId);
    show(mensajeResultadoBaja(signer.fullName, accion, outcome), "success");
    await load();
  };

  const handleReactivar = async (signer: MandateSigner) => {
    setBusyId(signer.id);
    try {
      const result = await reactivateCompanyMandateSigner(tenantId, signer.id, networkHeadId);
      show(mensajeResultadoReactivar(signer.fullName, result), "success");
      await load();
    } catch (err) {
      show(mensajeErrorAccion(err), "error");
    } finally {
      setBusyId(null);
    }
  };

  // HU #13248 — «Reenviar validación»: la ficha y la fila usan la misma llamada.
  const reenviarValidacion = (signer: MandateSigner) =>
    resendCompanyMandateSignerIdentity(tenantId, signer.id, networkHeadId);

  const handleReenviarFila = async (signer: MandateSigner) => {
    setBusyId(signer.id);
    try {
      const result = await reenviarValidacion(signer);
      show(mensajeReenvio(result, signer.email), "success");
      await load();
    } catch (err) {
      show(mensajeErrorReenvio(err), "error");
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
      {offices.some((o) => o.formatName) ? (
        <ul className="space-y-1 text-sm text-[#59677D] dark:text-white/70" data-testid="formatos-contrato-compania">
          {offices.filter((o) => o.formatName).map((o) => (
            <li key={o.transitOfficeId}>
              Formato de contrato en {o.name}:{" "}
              <span className="font-medium text-[#162244] dark:text-white">{o.formatName}</span>
            </li>
          ))}
        </ul>
      ) : null}

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

      {canCreate && (
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
      )}

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
                  Validación
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
              {pg.paginar(signers).map((signer) => {
                // HU #13139 — candado: lo configuró el organismo y este actor no lo puede tocar.
                const candado = tieneCandadoDelOrganismo(signer);
                const puedeEditar = canCreate && puedeEditarMandatario(signer);
                return (
                <tr
                  key={signer.id}
                  className={`${candado ? "bg-[#EEF1F5] dark:bg-white/5" : "bg-white dark:bg-[#0B0F14]"} ${TABLA_ROW_HOVER_CLS}`}
                  data-candado={candado ? "true" : undefined}
                >
                  <td className={`rounded-l-xl border-y border-l px-4 py-3 ${signer.isActive ? "" : "opacity-60"}`} style={{ borderColor: "#DFE5ED" }}>
                    <span className="font-semibold">{signer.fullName}</span>
                    {candado && <MandatarioCandado />}
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
                  <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }} data-testid="mandatario-validacion-celda">
                    {requiereValidacionPropia(signer) ? (
                      <StatusBadge
                        tone={presentarValidacion(signer.identityStatus).tone}
                        label={presentarValidacion(signer.identityStatus).texto}
                        ariaLabel={`Validación: ${presentarValidacion(signer.identityStatus).texto}`}
                      />
                    ) : (
                      <span aria-label="No aplica">—</span>
                    )}
                  </td>
                  <td className={`border-y px-4 py-3 ${signer.isActive ? "" : "opacity-60"}`} style={{ borderColor: "#DFE5ED" }}>
                    <OrganismosDelMandatario
                      ids={signer.transitOfficeIds ?? []}
                      nombrePorId={officeNameById}
                      sinFirmaIds={sinFirmaPorSigner(signer)}
                      motivo={motivoSinFirma(signer)}
                    />
                  </td>
                  <td className="rounded-r-xl border-y border-r px-4 py-3 text-right" style={{ borderColor: "#DFE5ED" }}>
                    <RowActionsMenu
                      ariaLabel={`Acciones de ${signer.fullName}`}
                      subject={signer.fullName}
                      actions={!puedeEditar ? [] : [
                        {
                          icon: Pencil,
                          label: `Editar mandatario ${signer.fullName}`,
                          onClick: () => {
                            setEditing(signer);
                            setFormOpen(true);
                          },
                          tone: "primary",
                        },
                        ...(puedeReenviarValidacion(signer)
                          ? [
                              {
                                icon: Send,
                                label: `Reenviar validación a ${signer.fullName}`,
                                onClick: () => void handleReenviarFila(signer),
                                disabled: busyId === signer.id,
                              },
                            ]
                          : []),
                        signer.isActive
                          ? {
                              icon: UserX,
                              label: `Desactivar mandatario ${signer.fullName}`,
                              onClick: () => setBaja({ signer, accion: "desactivar" }),
                              tone: "danger",
                              disabled: busyId === signer.id,
                            }
                          : {
                              icon: RotateCcw,
                              label: `Reactivar mandatario ${signer.fullName}`,
                              onClick: () => void handleReactivar(signer),
                              disabled: busyId === signer.id,
                            },
                        ...(puedeEliminarMandatario(signer)
                          ? [
                              {
                                icon: Trash2,
                                label: `Eliminar mandatario ${signer.fullName}`,
                                onClick: () => setBaja({ signer, accion: "eliminar" }),
                                tone: "danger" as const,
                                disabled: busyId === signer.id,
                              },
                            ]
                          : []),
                      ]}
                    />
                  </td>
                </tr>
                );
              })}
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

      {baja && (
        <MandatarioBajaDialog
          signer={baja.signer}
          accion={baja.accion}
          loadImpact={(signal) =>
            fetchCompanyMandateSignerImpact(tenantId, baja.signer.id, signal, networkHeadId)
          }
          onConfirm={(confirmar) => ejecutarBaja(baja.signer, baja.accion, confirmar)}
          onClose={() => setBaja(null)}
          officeLabel={(id) => officeNameById.get(id) ?? "un organismo"}
          companyLabel={(id) => (id === tenantId ? "Esta compañía" : "Otra compañía de la red")}
        />
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
          onResend={reenviarValidacion}
        />
      )}
    </div>
  );
}

const MAX_ORGANISMOS_VISIBLES = 3;

/**
 * Organismos de un mandatario: uno por línea (los que no puede firmar, en rojo), una sola frase con el motivo y
 * «+N más» plegable cuando son muchos. Antes se pegaban con comas y el aviso repetía los mismos nombres.
 * HU #11717 — se SEÑALA, no se inhabilita: los trámites en curso siguen emitiendo su mandato como hoy.
 */
function OrganismosDelMandatario({
  ids,
  nombrePorId,
  sinFirmaIds,
  motivo,
}: {
  ids: readonly string[];
  nombrePorId: ReadonlyMap<string, string>;
  sinFirmaIds: readonly string[];
  motivo: string;
}) {
  if (ids.length === 0) return <>—</>;
  const sinFirma = new Set(sinFirmaIds);
  const items = ids.map((id) => ({ id, nombre: nombrePorId.get(id) ?? id, bloqueado: sinFirma.has(id) }));
  const visibles = items.slice(0, MAX_ORGANISMOS_VISIBLES);
  const resto = items.slice(MAX_ORGANISMOS_VISIBLES);
  const fila = (o: (typeof items)[number]) => (
    <li key={o.id} className="leading-snug" style={o.bloqueado ? { color: "#E5484D" } : undefined}>
      {o.nombre}
    </li>
  );
  const todosBloqueados = sinFirmaIds.length > 0 && sinFirmaIds.length === ids.length;
  return (
    <div data-testid="mandatario-organismos">
      <ul className="space-y-0.5">{visibles.map(fila)}</ul>
      {resto.length > 0 && (
        <details className="mt-0.5 text-[11px]">
          <summary className="cursor-pointer font-semibold text-[#557EFF]">+{resto.length} más</summary>
          <ul className="mt-0.5 space-y-0.5 text-xs">{resto.map(fila)}</ul>
        </details>
      )}
      {sinFirmaIds.length > 0 && (
        <div className="mt-1 text-[11px] leading-tight" style={{ color: "#E5484D" }} title={motivo}>
          {todosBloqueados
            ? `No puede firmar todavía: ${motivo.toLowerCase()}`
            : `No puede firmar en los organismos marcados en rojo: ${motivo.toLowerCase()}`}
        </div>
      )}
    </div>
  );
}
