/**
 * Bug #14604 — tipo MIME a partir de la extensión, para abrir en el visor un adjunto del que solo se
 * conoce el nombre (p. ej. desde la línea de tiempo). Los adjuntos de trámite son PDF o imagen
 * (PDF, JPG, PNG, WEBP); sin extensión conocida se asume PDF, el caso común.
 */
export function mimetypeDe(filename: string): string {
  const ext = filename.trim().toLowerCase().split('.').pop() ?? '';
  switch (ext) {
    case 'jpg':
    case 'jpeg':
      return 'image/jpeg';
    case 'png':
      return 'image/png';
    case 'webp':
      return 'image/webp';
    default:
      return 'application/pdf';
  }
}
