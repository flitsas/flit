"use client";

// Ventana para anular un cobro de caja (configurable por organismo, 0 a 60 min): etiqueta «Anulable hasta 9:41 p. m.» y
// el reloj que oculta «Anular» cuando el instante pasa, aunque la lista no se haya recargado.
import { useEffect, useState } from "react";
import { formatHoraColombia } from "@/lib/format/date";

interface ConVentana {
  estado: "pagado" | "anulado";
  anulableHasta?: string | null;
}

const SIN_COBROS: readonly ConVentana[] = [];

function instante(valor: string | null | undefined): number | null {
  if (!valor) return null;
  const t = Date.parse(valor);
  return Number.isNaN(t) ? null : t;
}

/**
 * ¿La ventana de anulación ya se cerró? Solo si el servidor la informa (`anulableHasta` presente): `null` o un instante
 * pasado la cierran. Sin el campo (servidor anterior) rige únicamente `anulable`.
 */
export function ventanaCerrada(c: ConVentana, ahora: number): boolean {
  if (c.estado !== "pagado" || c.anulableHasta === undefined) return false;
  const t = instante(c.anulableHasta);
  return t === null || t <= ahora;
}

/** Hora actual que se actualiza justo cuando vence la próxima ventana abierta de la lista. */
export function useAhora(cobros: readonly ConVentana[] = SIN_COBROS): number {
  const [ahora, setAhora] = useState(() => Date.now());
  useEffect(() => {
    const ahoraReal = Date.now();
    if (ahoraReal - ahora > 1000) {
      // El reloj quedó atrás (lista nueva): se sincroniza y el efecto vuelve a correr con la hora al día.
      // eslint-disable-next-line react-hooks/set-state-in-effect -- sincroniza el reloj con la lista recién cargada
      setAhora(ahoraReal);
      return;
    }
    const futuros = cobros
      .filter((c) => c.estado === "pagado")
      .map((c) => instante(c.anulableHasta))
      .filter((t): t is number => t !== null && t > ahoraReal);
    if (futuros.length === 0) return;
    // setTimeout admite hasta ~24 días; la ventana máxima es de una hora.
    const espera = Math.min(...futuros) - ahoraReal + 250;
    const id = setTimeout(() => setAhora(Date.now()), espera);
    return () => clearTimeout(id);
  }, [cobros, ahora]);
  return ahora;
}

/** «Anulable hasta 9:41 p. m.» mientras la ventana siga abierta; nada en otro caso. */
export function EtiquetaAnulable({ cobro, ahora }: { cobro: ConVentana; ahora: number }) {
  if (cobro.estado !== "pagado") return null;
  const t = instante(cobro.anulableHasta);
  if (t === null || t <= ahora) return null;
  return (
    <span className="text-xs text-muted-foreground" data-testid="anulable-hasta">
      Anulable hasta {formatHoraColombia(new Date(t))}
    </span>
  );
}
