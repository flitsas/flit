// Columna «Firmas» de Consultas (gestor, organismo y Super Admin): resumen «N de M · …» y, debajo,
// una línea por persona que debe firmar con su rol y su estado en un chip de color. Es la MISMA
// celda en las tres consolas y el mismo criterio que usa el Excel del navegador.

import type { ActorValidacionDto, FirmaPersona, MaxActoresPorRol } from "@/lib/api/company-queries";
import type { DataColumn } from "@/components/consultas/columns";
import { bloquesDelGrupo, estiloChipValidacion } from "@/components/atom/modules/_reportes/consultas/identidad-validacion";

/** Lo que una fila de Consultas trae para pintar la firma. */
export interface FilaConFirma {
  firmasResumen?: string | null;
  firmasPersonas?: FirmaPersona[] | null;
  actores?: ActorValidacionDto[] | null;
}

export type RolFirma = "Vendedor" | "Comprador";

const SIN_VALIDAR = "Sin validar";

/** Estado de firma → estado de validación cuyo chip ya existe (mismos colores del producto). */
const ESTADO_A_CHIP: Record<string, string> = {
  Firmó: "aprobado",
  Pendiente: "pendiente_envio",
  Rechazada: "rechazado",
  Expirada: "expirado",
  [SIN_VALIDAR]: "sin_validacion",
};

export function estiloChipFirma(estado: string): { background: string; color: string } {
  return estiloChipValidacion(ESTADO_A_CHIP[estado] ?? "sin_validacion");
}

/** Un texto desconocido o vacío se ve como «Sin validar»: no se inventa un estado. */
export function etiquetaFirma(estado: string | null | undefined): string {
  const e = estado?.trim();
  return e && e in ESTADO_A_CHIP ? e : SIN_VALIDAR;
}

/**
 * Las personas que aplican, vendedores primero (como el backend). Con `firmasPersonas` manda el
 * backend; sin él, si la fila sí calculó la firma (`firmasResumen`), salen los actores del trámite con
 * su `estadoFirma` o «Sin validar». Si la firma no se calculó no hay líneas.
 */
export function personasDeFirma(fila: FilaConFirma): FirmaPersona[] {
  if (fila.firmasPersonas) {
    return [...fila.firmasPersonas].sort(
      (a, b) => (a.rol === b.rol ? 0 : a.rol === "Vendedor" ? -1 : 1) || a.ordinal - b.ordinal,
    );
  }
  if (fila.firmasResumen == null) return [];
  const actores = fila.actores ?? [];
  const aPersona = (a: ActorValidacionDto): FirmaPersona => ({
    rol: a.rol === "vendedor" ? "Vendedor" : "Comprador",
    ordinal: a.ordinal,
    nombre: a.nombre,
    tipoDocumento: a.tipoDocumento,
    documento: a.documento,
    firmo: a.estadoFirma === "Firmó",
    estado: etiquetaFirma(a.estadoFirma),
    fechaFirma: a.fechaFirma ?? null,
  });
  return [
    ...actores.filter((a) => a.rol === "vendedor").sort((a, b) => a.ordinal - b.ordinal),
    ...actores.filter((a) => a.rol !== "vendedor").sort((a, b) => a.ordinal - b.ordinal),
  ].map(aPersona);
}

export function personasDelRol(fila: FilaConFirma, rol: RolFirma): FirmaPersona[] {
  return personasDeFirma(fila).filter((p) => p.rol === rol);
}

/** Máximo de personas por rol sobre las filas: el ancho de las columnas por persona del Excel. */
export function maxActoresDeFirma(filas: FilaConFirma[]): MaxActoresPorRol {
  let vendedor = 0;
  let comprador = 0;
  for (const f of filas) {
    vendedor = Math.max(vendedor, personasDelRol(f, "Vendedor").length);
    comprador = Math.max(comprador, personasDelRol(f, "Comprador").length);
  }
  return { vendedor, comprador, propietario: 0 };
}

/** Texto plano de la celda (CSV y respaldo): el resumen del backend o «—» si no se calculó. */
export function textoFirmas(fila: FilaConFirma): string {
  return fila.firmasResumen ?? "—";
}

/** Columna «Firmas» y sus dos columnas por persona, comunes al gestor y al organismo. */
export function etiquetaColumnaPersona(rol: RolFirma, n: number): string {
  return `Firma ${rol === "Vendedor" ? "vendedor" : "comprador"}${n > 1 ? ` ${n}` : ""}`;
}

export function FirmasCell({ fila }: { fila: FilaConFirma }) {
  if (fila.firmasResumen == null) return <span>—</span>;
  const personas = personasDeFirma(fila);
  return (
    <div data-testid="firmas-celda" className="min-w-[14rem] max-w-[22rem] whitespace-normal">
      <span className="font-semibold" data-testid="firmas-resumen">
        {fila.firmasResumen}
      </span>
      {personas.length > 0 && (
        <ul className="mt-1 space-y-1" aria-label="Firma por persona">
          {personas.map((p) => (
            <li
              key={`${p.rol}-${p.ordinal}`}
              className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-[12px]"
              data-testid="firma-persona"
            >
              <span className="min-w-0 truncate" title={p.nombre}>
                {p.nombre || "—"} <span className="text-muted-foreground">({p.rol})</span>
              </span>
              <span
                className="w-fit rounded-full px-2 py-0.5 text-[11px] font-semibold"
                style={estiloChipFirma(etiquetaFirma(p.estado))}
              >
                {etiquetaFirma(p.estado)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * Las tres columnas de firma del catálogo (idénticas en gestor, Super Admin y organismo):
 * «Firmas» (resumen) y «Firma vendedor» / «Firma comprador», que en el Excel se abren en una
 * columna por persona («Firma vendedor», «Firma vendedor 2»…), mínimo una.
 */
export function columnasDeFirma<TRow extends FilaConFirma>(group: string): DataColumn<TRow>[] {
  const porRol = (rol: RolFirma): DataColumn<TRow> => {
    const clave = rol === "Vendedor" ? "vendedor" : "comprador";
    return {
      id: `firma_${clave}`,
      label: etiquetaColumnaPersona(rol, 1),
      group,
      value: (r) => {
        if (r.firmasResumen == null) return "—";
        const lista = personasDelRol(r, rol);
        return lista.length === 0 ? "—" : lista.map((p) => etiquetaFirma(p.estado)).join("\n");
      },
      width: 16,
      expandirPorBloque: {
        grupo: `firma_${clave}`,
        // Sin el máximo del servidor/lote, una columna: el mismo mínimo que el backend.
        bloques: (max) => bloquesDelGrupo(max, clave),
        header: (n) => etiquetaColumnaPersona(rol, n),
        cell: (r, n) => personasDelRol(r, rol)[n - 1]?.estado ?? null,
      },
    };
  };
  return [
    {
      id: "firmado_por_todas",
      label: "Firmas",
      group,
      // `null`/ausente = el backend no calculó la firma: «—», no un «faltan» que afirmaría algo.
      value: (r) => textoFirmas(r),
      raw: (r) => textoFirmas(r),
      width: 26,
    },
    porRol("Vendedor"),
    porRol("Comprador"),
  ];
}
