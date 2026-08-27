/**
 * RUES a veces entrega la razón social más cláusulas societarias en el mismo campo,
 * separadas por coma (p. ej. "BANCOLOMBIA S.A., ADEMÁS PODRÁ GIRAR…").
 * Si hay coma, el wizard usa solo el tramo anterior.
 */
export function shortRuesRazonSocial(raw: string | null | undefined): string {
  const text = (raw ?? '').trim();
  if (!text) return '';
  const comma = text.indexOf(',');
  if (comma < 0) return text;
  const head = text.slice(0, comma).trim();
  return head || text;
}
