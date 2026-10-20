// Limpieza del HTML del editor de plantillas al subconjunto que convierte el backend a PDF (HtmlAPdf): h1-h3, p, div,
// br, b/strong, i/em, u, ul/ol/li, table/tr/th/td, hr y style="text-align:…". Lo demás se desenvuelve (se conserva su
// texto) y los atributos se descartan. Las variables {{grupo.nombre}} son texto y pasan intactas.

const PERMITIDAS = new Set(["H1", "H2", "H3", "P", "DIV", "BR", "B", "STRONG", "I", "EM", "U", "UL", "OL", "LI", "TABLE", "TBODY", "THEAD", "TR", "TH", "TD", "HR"]);
const VACIAS = new Set(["BR", "HR"]);
const ALINEABLES = new Set(["H1", "H2", "H3", "P", "DIV", "TD", "TH", "LI"]);
const ALINEACION = /text-align\s*:\s*(left|center|right|justify)/i;

function limpiarNodo(nodo: Node, doc: Document): Node[] {
  if (nodo.nodeType === Node.TEXT_NODE) return [doc.createTextNode(nodo.textContent ?? "")];
  if (nodo.nodeType !== Node.ELEMENT_NODE) return [];
  const el = nodo as Element;
  const hijos = Array.from(el.childNodes).flatMap((h) => limpiarNodo(h, doc));
  const tag = el.tagName.toUpperCase();
  if (tag === "SCRIPT" || tag === "STYLE") return [];
  if (!PERMITIDAS.has(tag)) {
    // <span style="font-weight:bold"> y similares de algunos navegadores: se respetan como <b>/<i>/<u>.
    const estilo = el.getAttribute("style") ?? "";
    let envoltorio: Element | null = null;
    if (/font-weight\s*:\s*(bold|[6-9]00)/i.test(estilo)) envoltorio = doc.createElement("b");
    else if (/font-style\s*:\s*italic/i.test(estilo)) envoltorio = doc.createElement("i");
    else if (/text-decoration[^;]*underline/i.test(estilo)) envoltorio = doc.createElement("u");
    if (!envoltorio) return hijos;
    hijos.forEach((h) => envoltorio!.appendChild(h));
    return [envoltorio];
  }
  const limpio = doc.createElement(tag.toLowerCase());
  const alineacion = ALINEABLES.has(tag) ? ALINEACION.exec(el.getAttribute("style") ?? "")?.[1]?.toLowerCase() : undefined;
  if (alineacion && alineacion !== "left") limpio.setAttribute("style", `text-align:${alineacion}`);
  if (!VACIAS.has(tag)) hijos.forEach((h) => limpio.appendChild(h));
  return [limpio];
}

export function limpiarHtml(html: string): string {
  if (typeof DOMParser === "undefined") return html;
  const doc = new DOMParser().parseFromString(`<body>${html}</body>`, "text/html");
  const salida = doc.createElement("div");
  Array.from(doc.body.childNodes)
    .flatMap((n) => limpiarNodo(n, doc))
    .forEach((n) => salida.appendChild(n));
  return salida.innerHTML.replace(/&nbsp;/g, " ").trim();
}

/** Variables {{…}} que usa un texto (para resaltar las que no están en el catálogo antes de publicar). */
export function variablesUsadas(texto: string): string[] {
  const vistas: string[] = [];
  for (const m of texto.matchAll(/\{\{([^}]*)\}\}/g)) {
    let v = m[1].trim();
    // Bloques condicionales del motor: {{#si variable}}, {{#si variable == "x"}}, {{#sino}} y {{/si}} no son variables;
    // de {{#si …}} cuenta la variable que se evalúa.
    if (v === "#sino" || v === "/si") continue;
    if (v.startsWith("#si")) v = v.slice(3).trim().split(/\s|==|!=/)[0];
    if (v && !vistas.includes(v)) vistas.push(v);
  }
  return vistas;
}

export const TABLA_SIMPLE = "<table><tr><th>Campo</th><td>Valor</td></tr><tr><th>Campo</th><td>Valor</td></tr></table><p></p>";
