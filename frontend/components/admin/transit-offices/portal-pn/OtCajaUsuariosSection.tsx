"use client";

// HU #14455 (Feature #14453, Épica #14210) — «Usuarios de caja» del organismo en el hub del OT: estado (activo, desactivado,
// invitación pendiente), crear, desactivar/reactivar y reenviar la invitación. Tabla del modelo de trámites: DataTable,
// «Acciones», paginación numerada con «Filas por página» y loader del carrito. El permiso real lo decide el servidor.
import { useCallback, useEffect, useMemo, useState } from "react";
import { Send, UserCheck, UserPlus, UserX } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ActionsMenu } from "@/components/atom/ActionsMenu";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { PnButton } from "@/components/admin/plataforma/persona-natural/pn-componentes";
import { usePermissions } from "@/hooks/usePermissions";
import { crearCajaApiOt, type CajaApi, type CajaUsuario, type CajaUsuarioEstado } from "@/lib/api/admin-ot-portal-caja";
import { formatFechaHora } from "@/lib/format/date";
import { cajaErrorMessage, cajaNoHabilitada } from "./cajaErrores";
import { OtCajaUsuarioModal } from "./OtCajaUsuarioModal";

export const CAJA_USUARIOS_CARGANDO = "Cargando usuarios de caja…";
export const CAJA_USUARIOS_VACIO = "Aún no hay usuarios de caja.";
const ESTADO_LABEL: Record<CajaUsuarioEstado, string> = {
  active: "Activo",
  suspended: "Desactivado",
  invited: "Invitación pendiente",
};

type Fila = CajaUsuario & { clave: string };

export interface OtCajaUsuariosSectionProps {
  transitOfficeId: string;
  /** Solo para pruebas: cliente ya resuelto. */
  api?: CajaApi;
}

export function OtCajaUsuariosSection({ transitOfficeId, api: apiProp }: OtCajaUsuariosSectionProps) {
  const { isSuperAdmin } = usePermissions();
  // El ot_admin no envía el organismo: lo toma el servidor de su token.
  const api = useMemo(
    () => apiProp ?? crearCajaApiOt(isSuperAdmin ? { transitOfficeId } : undefined),
    [apiProp, isSuperAdmin, transitOfficeId],
  );

  const [usuarios, setUsuarios] = useState<CajaUsuario[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [mensajeError, setMensajeError] = useState("");
  const [noHabilitada, setNoHabilitada] = useState(false);
  const [recarga, setRecarga] = useState(0);
  const [creando, setCreando] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);
  const [errorAccion, setErrorAccion] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const pg = usePaginacion("portal.caja.usuarios");

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- consulta del servidor: el estado de carga es intencional
    setStatus("loading");
    api
      .listarUsuarios(controller.signal)
      .then((lista) => {
        if (controller.signal.aborted) return;
        setUsuarios(lista);
        setNoHabilitada(false);
        setStatus("ready");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setMensajeError(cajaErrorMessage(err));
        setNoHabilitada(cajaNoHabilitada(err));
        setStatus("error");
      });
    return () => controller.abort();
  }, [api, recarga]);

  const recargar = useCallback(() => setRecarga((n) => n + 1), []);

  async function ejecutar(accion: () => Promise<unknown>, exito: string) {
    setOcupado(true);
    setAviso(null);
    setErrorAccion(null);
    try {
      await accion();
      setAviso(exito);
      recargar();
    } catch (err) {
      setErrorAccion(cajaErrorMessage(err));
    } finally {
      setOcupado(false);
    }
  }

  const filas: Fila[] = useMemo(
    () => usuarios.map((u, i) => ({ ...u, clave: u.userId ?? u.invitationId ?? `fila-${i}` })),
    [usuarios],
  );
  const visibles = pg.paginar(filas);

  const columns: DataTableColumn<Fila>[] = [
    { key: "nombre", header: "Nombre", render: (u) => u.fullName },
    { key: "correo", header: "Correo", render: (u) => u.email },
    { key: "estado", header: "Estado", render: (u) => ESTADO_LABEL[u.status] },
    { key: "creado", header: "Creado", render: (u) => formatFechaHora(u.createdAt, "") },
    {
      key: "acciones",
      header: "Acciones",
      align: "right",
      render: (u) => (
        <ActionsMenu
          ariaLabel={`Acciones para ${u.fullName}`}
          items={[
            ...(u.status === "invited" && u.invitationId
              ? [
                  {
                    key: "reenviar",
                    label: "Reenviar invitación",
                    icon: Send,
                    ariaLabel: `Reenviar invitación a ${u.fullName}`,
                    disabled: ocupado,
                    disabledReason: "Hay otra acción en curso.",
                    onSelect: () => void ejecutar(() => api.reenviar(u.invitationId!), "Invitación reenviada."),
                  },
                ]
              : []),
            ...(u.status === "active" && u.userId
              ? [
                  {
                    key: "desactivar",
                    label: "Desactivar",
                    icon: UserX,
                    tone: "danger" as const,
                    ariaLabel: `Desactivar a ${u.fullName}`,
                    disabled: ocupado,
                    disabledReason: "Hay otra acción en curso.",
                    onSelect: () => void ejecutar(() => api.suspender(u.userId!), "Usuario desactivado."),
                  },
                ]
              : []),
            ...(u.status === "suspended" && u.userId
              ? [
                  {
                    key: "reactivar",
                    label: "Reactivar",
                    icon: UserCheck,
                    ariaLabel: `Reactivar a ${u.fullName}`,
                    disabled: ocupado,
                    disabledReason: "Hay otra acción en curso.",
                    onSelect: () => void ejecutar(() => api.reactivar(u.userId!), "Usuario reactivado."),
                  },
                ]
              : []),
          ]}
        />
      ),
    },
  ];

  return (
    <section aria-labelledby="caja-usuarios-titulo" className="flex flex-col gap-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 id="caja-usuarios-titulo" className="text-base font-bold text-flit-primary dark:text-white">
            Usuarios de caja
          </h2>
          <p className="text-xs text-[#59677D] dark:text-white/70">
            Quienes cobran en la caja de tu organismo. Reciben una invitación por correo para activar su cuenta.
          </p>
        </div>
        <PnButton
          variant="primary"
          icon={UserPlus}
          disabled={noHabilitada || status === "loading"}
          onClick={() => setCreando(true)}
        >
          Crear usuario de caja
        </PnButton>
      </div>

      {aviso && <InlineAlert tone="success">{aviso}</InlineAlert>}
      {errorAccion && <InlineAlert tone="error">{errorAccion}</InlineAlert>}

      {status === "loading" ? (
        <CarLoaderModal label={CAJA_USUARIOS_CARGANDO} />
      ) : noHabilitada ? (
        <p role="status" className="px-4 py-10 text-center text-sm font-semibold text-flit-primary dark:text-white">
          {mensajeError}
        </p>
      ) : (
        <UiStateBoundary status={status} errorMessage={mensajeError} onRetry={recargar}>
          <DataTable<Fila>
            ariaLabel="Usuarios de caja"
            columns={columns}
            rows={visibles}
            getRowKey={(u) => u.clave}
            emptyContent={
              <p role="status" className="px-4 py-10 text-center text-sm font-semibold text-flit-primary dark:text-white">
                {CAJA_USUARIOS_VACIO}
              </p>
            }
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: filas.length,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
      )}

      {creando && (
        <OtCajaUsuarioModal
          onClose={() => setCreando(false)}
          onInvitar={async (v) => {
            await api.invitar(v);
            setCreando(false);
            setAviso("Invitación enviada.");
            recargar();
          }}
        />
      )}
    </section>
  );
}
