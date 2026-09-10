import { catalogDocumentName } from '@/lib/tramites/document-labels';

/**
 * Nombre del tipo tal como se creó en Documental, con el código al lado en menor jerarquía.
 */
export function DocumentCatalogCaption({
  nombre,
  codigo,
  className,
}: {
  nombre?: string | null;
  codigo: string;
  className?: string;
}) {
  const name = catalogDocumentName(codigo, nombre);
  const code = codigo.trim();
  return (
    <span className={className}>
      {name}
      {code ? (
        <>
          {' '}
          <span className="text-xs font-normal" style={{ color: 'var(--muted-foreground)' }}>
            ({code})
          </span>
        </>
      ) : null}
    </span>
  );
}
