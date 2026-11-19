"use client";

// Secretaría de destino (traslado de cuenta): búsqueda por texto contra el catálogo de organismos operables. Solo dibuja y
// busca; quien lo monta decide cómo y cuándo guardar. Los cuatro estados: cargando, error con reintento, sin resultados y lista.
import { AlertTriangle } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Btn, Field } from "@/components/tramite/piezas";
import { SelectorBusqueda, type OpcionBusqueda } from "@/components/tramite/SelectorBusqueda";
import type { ClientePortal, OrganismoDestino } from "@/lib/api/cliente";
import { mensajeDe } from "@/lib/api/errores";

const ESPERA_BUSQUEDA_MS = 300;
const LIMITE = 20;
const CINCO_MINUTOS = 5 * 60 * 1000;

/** «Municipio, Departamento» de un organismo, sin repetir ni dejar comas sueltas. */
export function ubicacionDeOrganismo(o: Pick<OrganismoDestino, "municipio" | "departamento">): string {
  return [o.municipio?.trim(), o.departamento?.trim()].filter(Boolean).join(", ");
}

interface Props {
  cliente: ClientePortal;
  /** Id del organismo elegido (vacío = ninguno). */
  valor: string;
  /** Nombre del organismo elegido, si se conoce (el valor guardado solo trae el id). */
  nombreElegido?: string | undefined;
  onElegir: (organismo: OrganismoDestino) => void;
  etiqueta?: string;
  obligatorio?: boolean;
  error?: string | undefined;
  disabled?: boolean;
  onBlur?: () => void;
}

export function OrganismoDestinoField({ cliente, valor, nombreElegido, onElegir, etiqueta = "Secretaría de destino", obligatorio = true, error, disabled = false, onBlur }: Props) {
  const [texto, setTexto] = useState("");
  const [busqueda, setBusqueda] = useState("");
  const [elegido, setElegido] = useState<OrganismoDestino | null>(null);

  // La búsqueda al servidor espera a que la persona deje de escribir.
  useEffect(() => {
    const id = window.setTimeout(() => setBusqueda(texto.trim()), ESPERA_BUSQUEDA_MS);
    return () => window.clearTimeout(id);
  }, [texto]);

  const lista = useQuery({
    queryKey: ["pn", "organismos-destino", busqueda],
    queryFn: ({ signal }) => cliente.catalogos.organismosDestino(busqueda, LIMITE, { signal }),
    staleTime: CINCO_MINUTOS,
    placeholderData: (previa) => previa,
  });

  const opciones: OpcionBusqueda[] = (lista.data ?? []).map((o) => ({ codigo: o.id, etiqueta: o.nombre, detalle: ubicacionDeOrganismo(o) || undefined }));
  // Un organismo ya elegido sigue en la lista aunque la búsqueda actual no lo traiga.
  if (elegido && elegido.id === valor && !opciones.some((o) => o.codigo === valor)) opciones.unshift({ codigo: elegido.id, etiqueta: elegido.nombre, detalle: ubicacionDeOrganismo(elegido) || undefined });
  const buscando = lista.isFetching;

  return (
    <div aria-busy={lista.isPending}>
      <Field label={`${etiqueta}${obligatorio ? " *" : ""}`} error={error}>
        <SelectorBusqueda
          etiqueta={etiqueta}
          opciones={opciones}
          valor={valor}
          etiquetaElegida={elegido?.id === valor ? elegido.nombre : nombreElegido}
          invalido={Boolean(error)}
          disabled={disabled}
          cargando={buscando}
          marcador="Busca por nombre o municipio"
          sinResultados="No hay secretarías que coincidan."
          onBuscar={setTexto}
          onChange={(o) => {
            const organismo = lista.data?.find((x) => x.id === o.codigo) ?? (elegido?.id === o.codigo ? elegido : undefined);
            if (!organismo) return;
            setElegido(organismo);
            onElegir(organismo);
          }}
          onBlur={() => onBlur?.()}
        />
      </Field>
      <div aria-live="polite" className="mt-1 text-xs">
        {lista.isError && (
          <p role="alert" className="flex flex-wrap items-center gap-2 text-alert">
            <AlertTriangle aria-hidden className="h-3.5 w-3.5" />
            {mensajeDe(lista.error)}
            <Btn tone="secondary" onClick={() => void lista.refetch()}>
              Reintentar
            </Btn>
          </p>
        )}
      </div>
    </div>
  );
}
