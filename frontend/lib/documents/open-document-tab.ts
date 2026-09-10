/**
 * Apertura de documentos en pestaña nueva con loader del carrito
 * (mismo HTML que el resumen del trámite / ExpedienteVisor).
 */

function buildDocumentTabHtml(kind: "loading" | "error", message: string): string {
  const isError = kind === "error";
  const safeMsg = message.replace(/</g, "&lt;").replace(/"/g, "&quot;");
  const title = message.replace(/</g, "");
  const track = isError ? "#FF4E00" : "#557eff";
  const accent = isError ? "#FF4E00" : "#00dbd5";
  const textColor = isError ? "#FF4E00" : "#557eff";
  const animCss = isError
    ? ""
    : `
  .car-loader__car-spin {
    animation: car-loader-spin-ccw 2s linear infinite;
    transform-box: view-box;
    transform-origin: 50% 50%;
  }
  .car-loader__arc-spin {
    animation: car-loader-spin-cw 2s linear infinite;
    transform-box: view-box;
    transform-origin: 50% 50%;
  }
  .car-loader__dot {
    display: inline-block;
    animation: car-loader-dot-blink 1.2s infinite ease-in-out both;
  }`;

  return `<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>${title}</title>
<link rel="preconnect" href="https://fonts.googleapis.com"/>
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin/>
<link href="https://fonts.googleapis.com/css2?family=Poppins:wght@400&display=swap" rel="stylesheet"/>
<style>
  @keyframes car-loader-spin-cw { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
  @keyframes car-loader-spin-ccw { from { transform: rotate(0deg); } to { transform: rotate(-360deg); } }
  @keyframes car-loader-dot-blink { 0%, 100% { opacity: 0; } 50% { opacity: 1; } }
  html, body { margin: 0; height: 100%; background: #fcfbf8; }
  body {
    display: flex;
    align-items: center;
    justify-content: center;
    -webkit-font-smoothing: antialiased;
  }
  .car-loader__wrapper {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    line-height: 0;
  }
  .car-loader__wrapper > svg { display: block; }
  .car-loader__text {
    margin-top: 2px;
    font-family: "Poppins", ui-sans-serif, system-ui, sans-serif;
    font-weight: 400;
    font-size: 16px;
    color: ${textColor};
    letter-spacing: 0.02em;
    text-align: center;
    line-height: 1;
  }
  .car-loader__word { position: relative; display: inline-block; }
  .car-loader__dots {
    position: absolute;
    left: 100%;
    top: 0;
    white-space: nowrap;
  }
  ${animCss}
</style>
</head>
<body>
  <div class="car-loader__wrapper" role="status" aria-live="polite" aria-label="${safeMsg}">
    <svg width="240" height="240" viewBox="0 0 300 300" aria-hidden="true">
      <circle cx="150" cy="150" r="90" fill="none" stroke="${track}" stroke-width="45"/>
      <g class="car-loader__arc-spin" style="transform-origin: 150px 150px">
        <g transform="translate(115.6 264) scale(1)">
          <path
            d="M1.91,2.5c10,2.91,20.57,4.47,31.51,4.47,11.66,0,22.9-1.77,33.47-5.06"
            fill="none"
            stroke="${accent}"
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="3.82"
          />
        </g>
      </g>
      <g class="car-loader__car-spin" style="transform-origin: 150px 150px">
        <g transform="translate(126.8 238.3)">
          <path d="M28.98,23.24l1.65-2-23.84.24c-2.82.03-5.02-.89-5.92-3.78-1.18-3.78-1.18-8.19,0-11.97C1.78,2.84,3.99,1.92,6.8,1.95l23.84.23-1.74-2c1.37-.43,2.22-.08,3.01.96,1.71,2.26,5.84-.13,9.56,1.16s5,4.97,4.94,8.41c-.04,2.32.03,4.68-.88,6.56-2.86,5.88-7.47,3.78-12.86,4.14-1.11.99-1.19,2.53-3.68,1.83Z" fill="${accent}"/>
          <path d="M34.29,19.21l-6.11-.63-.02-13.71,6.02-.67c3.62,3.1,3.76,11.42.11,15.01Z" fill="#fff"/>
          <path d="M9.97,17.35c-1.36.32-3.75.15-4.25-1.26-.95-2.68-.95-6.07,0-8.76.49-1.39,2.85-1.52,4.19-1.3.47,3.84.44,7.2.06,11.32Z" fill="#fff"/>
          <path d="M18.71,5.69l-6.55-.07c1.87-1.38,3.88-1.36,6.42-1.49l.13,1.56Z" fill="#fff"/>
          <path d="M18.64,17.68l.06,1.59c-2.34-.07-4.43-.05-6.57-1.45l6.51-.14Z" fill="#fff"/>
          <path d="M26.49,4.76l-7.12.88-.08-1.53c2.29,0,4.37-.1,7.2.65Z" fill="#fff"/>
          <path d="M26.48,18.68c-2.26.64-4.33.59-7.13.62v-1.54s7.13.92,7.13.92Z" fill="#fff"/>
          <path d="M43.71,5.45l.05,3.33c-1.46-.76-1.86-2.49-1.6-4.4.28-.37,1.54.5,1.55,1.06Z" fill="#fff"/>
          <path d="M43.72,17.94c0,.53-1.12,1.36-1.51,1.23-.32-1.73-.12-3.51,1.53-4.53l-.03,3.31Z" fill="#fff"/>
          <path d="M3.38,5.73c-.12.86-.39,1.67-1.53,2.14-.07-.97.19-2.49.5-3.23.06-.14,1.22-.28,1.2-.14l-.17,1.23Z" fill="#fff"/>
          <path d="M3.33,17.37l.25,1.32c.04.21-1.08.31-1.18.11-.32-.65-.6-2.06-.64-3.1.87-.05,1.41.84,1.57,1.67Z" fill="#fff"/>
        </g>
      </g>
    </svg>
    <span class="car-loader__text">
      <span class="car-loader__word">
        ${isError ? safeMsg : "Cargando"}
        ${
          isError
            ? ""
            : `<span class="car-loader__dots" aria-hidden="true">
          <span class="car-loader__dot" style="animation-delay:0s">.</span>
          <span class="car-loader__dot" style="animation-delay:0.3s">.</span>
          <span class="car-loader__dot" style="animation-delay:0.6s">.</span>
        </span>`
        }
      </span>
    </span>
  </div>
</body>
</html>`;
}

export function openLoadingDocumentTab(): Window | null {
  const win = window.open("about:blank", "_blank");
  if (!win) return null;
  try {
    win.opener = null;
    win.document.open();
    win.document.write(buildDocumentTabHtml("loading", "Cargando documento…"));
    win.document.close();
  } catch {
    // Si el navegador no deja escribir en about:blank, igual usamos la ventana.
  }
  return win;
}

export function showDocumentTabError(win: Window | null, message = "No se pudo abrir el documento.") {
  if (!win || win.closed) return;
  try {
    win.document.open();
    win.document.write(buildDocumentTabHtml("error", message));
    win.document.close();
  } catch {
    win.close();
  }
}

export function openObjectUrlInWindow(url: string, existingWin: Window | null) {
  if (existingWin && !existingWin.closed) {
    existingWin.location.replace(url);
    try {
      existingWin.opener = null;
    } catch {
      // ignore
    }
    return;
  }
  const win = window.open(url, "_blank");
  if (win) {
    try {
      win.opener = null;
    } catch {
      // ignore
    }
    return;
  }
  const a = document.createElement("a");
  a.href = url;
  a.target = "_blank";
  a.rel = "noopener noreferrer";
  document.body.appendChild(a);
  a.click();
  a.remove();
}

function asInlinePreviewBlob(blob: Blob): Blob {
  const type = (blob.type || "").toLowerCase();
  // PDF e imágenes se muestran inline; respuestas API sin MIME → PDF.
  if (type === "application/pdf" || type.startsWith("image/")) {
    return blob;
  }
  return new Blob([blob], { type: "application/pdf" });
}

/**
 * Abre una pestaña con el carrito, obtiene el blob y lo muestra inline.
 * Conserva PDF e imágenes; sin MIME conocido asume PDF (preview FLIT).
 */
export async function openPdfBlobInNewTab(
  fetchPdf: () => Promise<Blob>,
  options?: { maximize?: boolean },
): Promise<void> {
  const win = openLoadingDocumentTab();
  try {
    const blob = await fetchPdf();
    const objectUrl = URL.createObjectURL(asInlinePreviewBlob(blob));
    openObjectUrlInWindow(objectUrl, win);
    if (options?.maximize) tryMaximizeWindow(win);
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 120_000);
  } catch {
    showDocumentTabError(win);
    throw new Error("document_preview_failed");
  }
}

function tryMaximizeWindow(win: Window | null) {
  if (!win || win.closed) return;
  try {
    win.moveTo(0, 0);
    win.resizeTo(window.screen.availWidth, window.screen.availHeight);
  } catch {
    /* el navegador puede bloquear resize de pestañas */
  }
}
