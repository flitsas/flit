// PDF de una página, de solo texto, generado en el navegador (sin dependencias): el comprobante del trámite con los datos visibles.
// Fuente Helvetica estándar con WinAnsi (cubre tildes y ñ del español); cada línea es una operación de texto.

const codificar = (texto: string): number[] => {
  const bytes: number[] = [];
  for (const ch of texto) {
    const c = ch.codePointAt(0) ?? 63;
    if (c === 0x28 || c === 0x29 || c === 0x5c) bytes.push(0x5c);
    bytes.push(c < 256 ? c : 63);
  }
  return bytes;
};

/** Arma el PDF. Las líneas largas se cortan a ~90 caracteres y se paginan de 52 en 52. */
export function armarPdf(lineas: string[]): Uint8Array {
  const partidas = lineas.flatMap((l) => (l.length <= 90 ? [l] : (l.match(/.{1,90}(\s|$)/g) ?? [l]).map((t) => t.trim())));
  const paginas: string[][] = [];
  for (let i = 0; i < partidas.length; i += 52) paginas.push(partidas.slice(i, i + 52));
  if (paginas.length === 0) paginas.push([""]);

  const objetos: number[][] = [];
  const texto = (s: string) => Array.from(s, (c) => c.charCodeAt(0));
  const n = paginas.length;
  // 1 catálogo, 2 páginas, 3 fuente, luego (página, contenido) por cada página.
  objetos.push(texto("<< /Type /Catalog /Pages 2 0 R >>"));
  objetos.push(texto(`<< /Type /Pages /Kids [${paginas.map((_, i) => `${4 + i * 2} 0 R`).join(" ")}] /Count ${n} >>`));
  objetos.push(texto("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
  paginas.forEach((p, i) => {
    objetos.push(texto(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents ${5 + i * 2} 0 R >>`));
    const contenido: number[] = [...texto("BT /F1 11 Tf 14 TL 50 800 Td\n")];
    p.forEach((linea, j) => {
      if (j > 0) contenido.push(...texto("T*\n"));
      contenido.push(...texto("("), ...codificar(linea), ...texto(") Tj\n"));
    });
    contenido.push(...texto("ET"));
    objetos.push([...texto(`<< /Length ${contenido.length} >>\nstream\n`), ...contenido, ...texto("\nendstream")]);
  });

  const salida: number[] = [...texto("%PDF-1.4\n")];
  const posiciones: number[] = [];
  objetos.forEach((o, i) => {
    posiciones.push(salida.length);
    salida.push(...texto(`${i + 1} 0 obj\n`), ...o, ...texto("\nendobj\n"));
  });
  const xref = salida.length;
  salida.push(...texto(`xref\n0 ${objetos.length + 1}\n0000000000 65535 f \n`));
  posiciones.forEach((p) => salida.push(...texto(`${String(p).padStart(10, "0")} 00000 n \n`)));
  salida.push(...texto(`trailer\n<< /Size ${objetos.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`));
  return Uint8Array.from(salida);
}

/** Descarga el PDF en el navegador. */
export function descargarPdf(nombreArchivo: string, lineas: string[]): void {
  const blob = new Blob([armarPdf(lineas) as BlobPart], { type: "application/pdf" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = nombreArchivo;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
