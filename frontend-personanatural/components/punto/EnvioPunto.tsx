"use client";

// Pasos 4 · Resumen y 5 · Envío del canal OT. Monta los MISMOS renderers del portal web (ResumenTramite y EnvioTramite)
// con un contexto del wizard propio del punto (como RequisitosPunto con FormularioRequisitos). La radicación es la del portal
// web (POST /solicitudes/{id}/radicar con la sesión del borrador); el pago por pasarela o caja queda para entregas futuras.
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle } from "lucide-react";
import { useCallback, useMemo } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { ProveedorWizard, type ApiWizard } from "@/components/tramite/ContextoWizard";
import { Btn, Card, Nav } from "@/components/tramite/piezas";
import { EnvioTramite } from "@/components/tramite/renderers/EnvioTramite";
import { ResumenTramite } from "@/components/tramite/renderers/ResumenTramite";
import { claveSolicitud, useSolicitud } from "@/components/tramite/useSolicitud";
import type { ClientePortal, DefinicionTipo } from "@/lib/api/cliente";
import { mensajeDeFallo } from "@/lib/api/errores";
import type { GrupoPaso, SeccionWizard } from "@/lib/tramite/pasos";

export type PasoEnvioPunto = "resumen" | "envio";

type Props = {
  cliente: ClientePortal;
  solicitudId: string;
  tipoCodigo: string;
  paso: PasoEnvioPunto;
  onPaso: (paso: PasoEnvioPunto) => void;
  onAtras: () => void;
  onIrActores: () => void;
  onTerminar: () => void;
};

const SECCION_RESUMEN: SeccionWizard = { clave: "resumen", sectionType: "summary", paso: null };
const SECCION_ENVIO: SeccionWizard = { clave: "envio", sectionType: "submission", paso: null };
const SIN_ESTADO = () => undefined;

export function EnvioPunto({ cliente, solicitudId, tipoCodigo, paso, onPaso, onAtras, onIrActores, onTerminar }: Props) {
  const queryClient = useQueryClient();
  const borrador = useSolicitud(cliente, solicitudId);
  const solicitud = borrador.data;
  const definicion = useQuery<DefinicionTipo>({
    queryKey: ["pn", "punto", "definicion", tipoCodigo, solicitud?.organismoId ?? ""],
    queryFn: ({ signal }) => cliente.catalogos.definicion(tipoCodigo, { organismoId: solicitud?.organismoId ?? "", signal }),
    enabled: Boolean(solicitud),
    staleTime: Infinity,
    retry: false,
  });

  const recargar = useCallback(() => queryClient.invalidateQueries({ queryKey: claveSolicitud(solicitudId) }), [queryClient, solicitudId]);
  const irAPaso = useCallback(
    (grupo: GrupoPaso) => {
      if (grupo === "resumen" || grupo === "envio") onPaso(grupo);
      else if (grupo === "actores") onIrActores();
      else onAtras();
    },
    [onPaso, onAtras, onIrActores],
  );
  // Los fallos de radicación y de sondeo se muestran en la propia pantalla (motivo bajo el botón); en el punto no hay sesión que reabrir.

  const api = useMemo<ApiWizard | null>(() => {
    if (!solicitud || !definicion.data) return null;
    return {
      cliente,
      definicion: definicion.data,
      solicitud,
      contexto: null,
      recargar,
      guardarConVersion: async (escribir) => {
        const vigente = queryClient.getQueryData<typeof solicitud>(claveSolicitud(solicitudId)) ?? solicitud;
        queryClient.setQueryData(claveSolicitud(solicitudId), await escribir(vigente.version));
      },
      registrarGuardado: () => undefined,
      reportarError: SIN_ESTADO,
      irAPaso,
      canal: "punto",
      alTerminar: onTerminar,
    };
  }, [cliente, solicitud, definicion.data, recargar, queryClient, solicitudId, irAPaso, onTerminar]);

  if (borrador.isPending || (solicitud && definicion.isPending)) {
    return (
      <Card title={paso === "resumen" ? "Resumen" : "Envío"} id="punto-envio-cargando">
        <div className="relative min-h-[12rem]" aria-busy="true">
          <MobilityLoader mensaje="Cargando el resumen…" />
        </div>
      </Card>
    );
  }

  if (!api) {
    const mensaje = mensajeDeFallo(borrador.error ?? definicion.error);
    return (
      <Card title="Resumen" id="punto-envio-error">
        <div role="alert" className="flex flex-col items-start gap-2 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
          <p className="flex gap-2">
            <AlertTriangle aria-hidden className="h-5 w-5 shrink-0" />
            <span>{mensaje}</span>
          </p>
          <Btn
            tone="secondary"
            onClick={() => {
              void borrador.refetch();
              void definicion.refetch();
            }}
          >
            Reintentar
          </Btn>
        </div>
        <div className="mt-4">
          <Nav onBack={onAtras} onNext={onAtras} nextLabel="Volver" />
        </div>
      </Card>
    );
  }

  return (
    <ProveedorWizard value={api}>
      {paso === "resumen" ? (
        <ResumenTramite seccion={SECCION_RESUMEN} activa intentoAvance={0} onEstado={SIN_ESTADO} />
      ) : (
        <EnvioTramite seccion={SECCION_ENVIO} activa intentoAvance={0} onEstado={SIN_ESTADO} />
      )}
    </ProveedorWizard>
  );
}
