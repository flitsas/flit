"use client";

import { useEffect, useId, useState } from "react";
import { Button } from "@flit/ui/Button";
import { ListaDefinicion } from "@flit/ui/ListaDefinicion";
import { Cargador } from "@flit/ui/Loader";
import {
  ETIQUETA_ESTADO_APROVISIONAMIENTO,
  ETIQUETA_ESTADO_NIT,
  ETIQUETA_SIN_ADMIN,
  getTenantProductProvisioningPreview,
  type ProvisioningPreview,
} from "@/lib/api/platform";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { Modal } from "@/components/atom/Modal";

// HU #14695 · M19 (plan §1.3 P1, D-TAXID, D-EMAIL; platform.v1.yaml 1.1.0 `ProvisioningPreview`; spec-visual §3.11).
// Antes de encender Impuestos, el SuperAdmin ve el NIT base y el correo con que core-impuestos creará la compañía en el
// proveedor. SIN_ADMIN bloquea el CTA (el PUT respondería 409); un NIT que no es VALIDO solo advierte (supuesto S-C).

const PRODUCT_CODE = "impuestos";
/** El API del proveedor admite de 1 a 200 caracteres en la razón social (plan §1.3, S14). */
const MAX_RAZON_SOCIAL = 200;

type Estado = { fase: "cargando" } | { fase: "error" } | { fase: "lleno"; datos: ProvisioningPreview };

/** Se monta al abrirse (ProductosTab lo hace así): cada apertura empieza en «cargando» y vuelve a leer el alta previa. */
export interface DialogoActivacionImpuestosProps {
  open: boolean;
  tenantId: string;
  /** El PUT de activación está en curso. */
  confirmando: boolean;
  /** Error del PUT (p. ej. el 409 PROVISIONING_PREREQUISITE_MISSING): se muestra dentro del diálogo, que no se cierra. */
  error: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}

export function DialogoActivacionImpuestos({ open, tenantId, confirmando, error, onConfirm, onCancel }: DialogoActivacionImpuestosProps) {
  const uid = useId();
  const motivoBloqueoId = `${uid}-bloqueo`;
  const [estado, setEstado] = useState<Estado>({ fase: "cargando" });
  const [intento, setIntento] = useState(0);

  useEffect(() => {
    if (!open) return;
    let vivo = true;
    getTenantProductProvisioningPreview(tenantId, PRODUCT_CODE)
      .then((datos) => vivo && setEstado({ fase: "lleno", datos }))
      .catch(() => vivo && setEstado({ fase: "error" }));
    return () => {
      vivo = false;
    };
  }, [open, tenantId, intento]);

  const datos = estado.fase === "lleno" ? estado.datos : null;
  const bloqueado = datos?.blocking === "SIN_ADMIN";
  const ctaDeshabilitado = !datos || bloqueado || confirmando;

  return (
    <Modal
      open={open}
      onClose={onCancel}
      title="Activar Impuestos"
      description="Antes de encenderlo, revisa los datos con los que se creará la compañía en el proveedor."
      icono="pesos"
      size="md"
      closeOnBackdrop={false}
      busy={confirmando}
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="secondary" size="sm" onClick={onCancel} disabled={confirmando}>
            Cancelar
          </Button>
          <Button
            size="sm"
            cargando={confirmando}
            disabled={ctaDeshabilitado}
            aria-describedby={bloqueado ? motivoBloqueoId : undefined}
            onClick={onConfirm}
          >
            Activar Impuestos
          </Button>
        </div>
      }
    >
      <div className="space-y-3">
        {estado.fase === "cargando" && (
          <div role="status" className="flex items-center gap-2 text-sm" aria-busy="true">
            <Cargador />
            <span>Consultando los datos del alta…</span>
          </div>
        )}

        {estado.fase === "error" && (
          <InlineAlert
            tone="error"
            action={
              <Button
                variant="secondary"
                size="sm"
                onClick={() => {
                  setEstado({ fase: "cargando" });
                  setIntento((n) => n + 1);
                }}
              >
                Reintentar
              </Button>
            }
          >
            No se pudieron leer los datos del alta
          </InlineAlert>
        )}

        {datos && (
          <>
            <ListaDefinicion
              items={[
                { termino: "NIT que se enviará", valor: datos.taxIdBase ? <span className="tabular-nums">{datos.taxIdBase}</span> : null },
                { termino: "Razón social", valor: datos.legalName || null },
                {
                  termino: (
                    <>
                      <span>Correo de contacto</span>
                      <span className="block font-normal">Administrador de Compañía más antiguo</span>
                    </>
                  ),
                  valor: datos.contactEmail,
                },
              ]}
            />
            {bloqueado && (
              <InlineAlert tone="warning" id={motivoBloqueoId}>
                {ETIQUETA_SIN_ADMIN}
              </InlineAlert>
            )}
            {datos.nitStatus !== "VALIDO" && (
              <InlineAlert tone="warning">
                {`El NIT de la compañía requiere revisión: ${ETIQUETA_ESTADO_NIT[datos.nitStatus] ?? ETIQUETA_ESTADO_NIT.AMBIGUO}. Impuestos quedará en «${ETIQUETA_ESTADO_APROVISIONAMIENTO.REQUIERE_REVISION_NIT}» hasta que lo corrijas en «Editar compañía».`}
              </InlineAlert>
            )}
            {datos.legalName.length > MAX_RAZON_SOCIAL && (
              <InlineAlert tone="warning">
                La razón social pasa de 200 caracteres: el proveedor la rechazará. Acórtala en «Editar compañía».
              </InlineAlert>
            )}
          </>
        )}

        {error && <InlineAlert tone="error">{error}</InlineAlert>}
      </div>
    </Modal>
  );
}
