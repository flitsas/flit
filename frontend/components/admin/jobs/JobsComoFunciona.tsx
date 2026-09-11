"use client";

import { useState } from "react";
import { HelpCircle } from "lucide-react";
import { CreateButton } from "@/components/atom/CreateButton";
import { Modal } from "@/components/atom/Modal";

/**
 * Ayuda SuperAdmin de Procesos periódicos: botón compacto (va en `ModuleTitle.action`)
 * y el instructivo en modal, para no restar alto a la tabla.
 */
export function JobsComoFunciona() {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="inline-flex h-9 shrink-0 items-center gap-1.5 rounded-xl border border-[#DFE5ED] bg-white px-3 text-xs font-semibold text-[#557EFF] transition hover:bg-[#EFF6FF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-[#0B0F14] dark:hover:bg-white/5"
      >
        <HelpCircle className="h-4 w-4" aria-hidden="true" />
        Cómo funciona
      </button>

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Cómo funciona"
        icon={HelpCircle}
        size="xl"
        description="Qué hace esta pantalla, qué pasa al guardar y qué no conviene confundir."
        footer={
          <div className="flex justify-end overflow-visible pr-1">
            <CreateButton label="Entendido" onClick={() => setOpen(false)} />
          </div>
        }
      >
        <div className="grid grid-cols-1 gap-x-8 gap-y-4 sm:grid-cols-2">
          <Punto titulo="Aquí no se ejecuta nada a mano">
            Los procesos de ICT y Quipux ya trabajan solos. Esta pantalla sirve para ver si están
            activos, cada cuánto corren y cuándo fue la última vez, y para entrar a cambiar esa
            cadencia.
          </Punto>

          <Punto titulo="La cadencia ICT se configura una sola vez">
            Las filas son el estado de cada proceso (intervalo, último run). No se configura una
            por una. El botón Configurar cadencia ICT abre una sola pantalla para horario,
            intervalos y lotes de todos.
          </Punto>

          <Punto titulo="Guardar aplica en menos de un minuto">
            No hay que pedir un despliegue. Tras Guardar, FLIT toma los valores nuevos en el
            siguiente paso del proceso. Si el catálogo aún muestra el número viejo, espera un
            momento y recarga.
          </Punto>

          <Punto titulo="Quipux también tiene un solo formulario">
            Radicar y Consultar estado son dos procesos. Configurar Quipux abre Integración
            Quipux una vez: interruptor, direcciones y claves. Aquí no se ven contraseñas.
          </Punto>

          <Punto titulo="«Último run» es el historial, no lo que acabas de guardar">
            Cuenta la última vez que el proceso corrió. «Sin ejecución» es normal si todavía no ha
            tocado horario o si ese proceso no deja historial (Retención de logs). Para más detalle
            usa Reportes ICT.
          </Punto>

          <Punto titulo="Consultas RUNT no es Confirmación RUNT">
            Consultas RUNT pide datos del vehículo en el pre-trámite. Confirmación RUNT es el paso
            de Plataforma después de aprobar un trámite. No se configuran juntas.
          </Punto>
        </div>
      </Modal>
    </>
  );
}

function Punto({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="text-sm font-semibold text-[#162744] dark:text-white">{titulo}</p>
      <p className="mt-0.5 text-sm text-[#59677D] dark:text-white/70">{children}</p>
    </div>
  );
}
