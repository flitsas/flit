import { INSIGNIA_CLS } from "@/components/parametrizacion/estilos";

/** «****» y los 4 últimos caracteres, como la API (AnalizadorCarga.Enmascarar). Un valor ya enmascarado no cambia. */
export function enmascararDocumento(documento: string): string {
  if (documento.startsWith("****")) return documento;
  return `****${documento.slice(-4)}`;
}

/**
 * Documento de persona declarado por el cliente (HU #13754, F02-C13): el tipo y el número enmascarado, con el rótulo
 * «Declarado por el cliente». La API ya lo entrega enmascarado; si llegara completo, aquí se vuelve a enmascarar para que
 * la pantalla nunca lo muestre entero.
 */
export function DocumentoDeclarado({ tipo, documento }: { tipo: string | null; documento: string | null }) {
  if (!documento) return null;
  const visible = enmascararDocumento(documento);
  return (
    <span className="inline-flex flex-wrap items-center gap-1.5">
      <span className="font-mono text-xs font-medium text-flit-primary dark:text-white">{tipo ? `${tipo} ${visible}` : visible}</span>
      <span className={INSIGNIA_CLS}>Declarado por el cliente</span>
    </span>
  );
}
