"use client";

import { ModalAgregarConcepto, ModalConfirmar, ModalEvidencia, ModalMotivo } from "@/components/backoffice/ModalesSaneamiento";
import { rotuloConcepto } from "@/lib/saneamiento/estados";
import type { GestionSaneamientoEstado } from "./useGestionSaneamiento";

// PS-5 (CR-5): el modal abierto de la gestión (spec-ui §6.3–§6.5). Las transiciones con motivo usan ModalMotivo; el cierre
// como saneado y los «Quitar», ModalConfirmar; el alta de concepto y la evidencia, sus modales propios.

const MOTIVO: Record<"anular" | "reabrir" | "no_procede", { titulo: string; texto?: string; verbo: string }> = {
  anular: { titulo: "Anular el saneamiento", texto: "La compañía podrá volver a pedirlo.", verbo: "Anular" },
  reabrir: { titulo: "Reabrir el saneamiento", verbo: "Reabrir" },
  no_procede: { titulo: "Cerrar como no procede", verbo: "Cerrar como no procede" },
};

export function ModalesGestion({ g }: { g: GestionSaneamientoEstado }) {
  const { modal, ocupado, errorModal, cerrarModal } = g;
  if (!modal) return null;
  switch (modal.tipo) {
    case "anular":
    case "reabrir":
    case "no_procede": {
      const tipo = modal.tipo;
      return (
        <ModalMotivo
          key={tipo}
          {...MOTIVO[tipo]}
          ocupado={ocupado}
          error={errorModal}
          errorCampo={g.errorCampo}
          onCerrar={cerrarModal}
          onConfirmar={(m) => void g.transicion(tipo, m)}
          onRecargar={g.recargarDesdeModal}
        />
      );
    }
    case "saneado":
      return (
        <ModalConfirmar
          titulo="¿Cerrar como saneado?"
          texto="La compañía verá el saneamiento como saneado. El dictamen no cambia."
          verbo="Cerrar como saneado"
          ocupado={ocupado}
          error={errorModal}
          onCerrar={cerrarModal}
          onConfirmar={() => void g.transicion("saneado")}
          onRecargar={g.recargarDesdeModal}
        />
      );
    case "agregar":
      return <ModalAgregarConcepto ocupado={ocupado} error={errorModal} onCerrar={cerrarModal} onAgregar={(d) => void g.agregar(d)} />;
    case "evidencia":
      return (
        <ModalEvidencia
          concepto={rotuloConcepto(modal.renglon)}
          ocupado={ocupado}
          error={g.errorEvidencia}
          onCerrar={cerrarModal}
          onSubir={(archivo) => void g.subir(modal.renglon, archivo)}
        />
      );
    case "quitar-evidencia":
      return (
        <ModalConfirmar
          titulo="¿Quitar la evidencia?"
          texto={`${rotuloConcepto(modal.renglon)}. Quedará en el historial.`}
          verbo="Quitar"
          peligro
          ocupado={ocupado}
          error={errorModal}
          onCerrar={cerrarModal}
          onConfirmar={() => void g.confirmarQuitarEvidencia(modal.renglon)}
        />
      );
    case "quitar-renglon":
      return (
        <ModalConfirmar
          titulo="¿Quitar el concepto?"
          texto={`${rotuloConcepto(modal.renglon)}. Quedará en el historial.`}
          verbo="Quitar"
          peligro
          ocupado={ocupado}
          error={errorModal}
          onCerrar={cerrarModal}
          onConfirmar={() => void g.confirmarQuitarRenglon(modal.renglon)}
        />
      );
  }
}
