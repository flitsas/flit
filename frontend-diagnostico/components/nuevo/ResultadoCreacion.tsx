"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { PantallaResultado } from "@flit/ui/PantallaResultado";

export interface ResultadoCreacionProps {
  diagnosticoId: string;
  vehiculos: number;
}

/** Título del resultado: «Diagnóstico iniciado con N vehículos» (singular con uno). */
export function tituloIniciado(vehiculos: number): string {
  return `Diagnóstico iniciado con ${vehiculos} ${vehiculos === 1 ? "vehículo" : "vehículos"}`;
}

/**
 * Resultado de Nuevo diagnóstico (HU #14562, AC18/AC19), común a Express y Masivo: «Ver tablero», «Volver» al inicio y la
 * redirección de 5 s al tablero que «Quedarme aquí» o cualquier interacción detienen (WCAG 2.2.1, AC18). PantallaResultado
 * lleva el foco al título al aparecer y anuncia la cuenta y su cancelación en su región polite (auditoría M1).
 */
export function ResultadoCreacion({ diagnosticoId, vehiculos }: ResultadoCreacionProps) {
  const router = useRouter();
  const tablero = `/diagnosticos/${encodeURIComponent(diagnosticoId)}`;
  return (
    <PantallaResultado
      tono="exito"
      titulo={tituloIniciado(vehiculos)}
      mensaje="Las consultas ya están en curso. Puedes seguirlas en el tablero del lote."
      primaria={{ etiqueta: "Ver tablero", href: tablero, enlace: Link }}
      secundaria={{ etiqueta: "Volver", href: "/", enlace: Link }}
      redireccion={{ segundos: 5, destino: "el tablero del lote", onNavegar: () => router.push(tablero) }}
    />
  );
}
