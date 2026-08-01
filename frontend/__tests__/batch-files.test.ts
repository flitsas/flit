import { describe, expect, it } from 'vitest';
import {
  archivosDesdeArrastre,
  esArchivoAdmitido,
  filtrarUtiles,
  soloPrimerNivel,
} from '@/lib/batch-files';

function file(name: string, relativePath?: string): File {
  const f = new File(['x'], name);
  if (relativePath !== undefined) {
    Object.defineProperty(f, 'webkitRelativePath', { value: relativePath });
  }
  return f;
}

describe('esArchivoAdmitido', () => {
  it.each(['a.pdf', 'A.PDF', 'foto.jpg', 'foto.jpeg', 'escaneo.png', 'docs.zip'])(
    'acepta %s',
    (name) => {
      expect(esArchivoAdmitido(name)).toBe(true);
    },
  );

  it.each(['nota.txt', 'hoja.xlsx', 'imagen.webp', 'sin-extension'])(
    'rechaza %s',
    (name) => {
      // WEBP se admite como adjunto pero el modelo de visión no lo lee.
      expect(esArchivoAdmitido(name)).toBe(false);
    },
  );
});

describe('filtrarUtiles', () => {
  it('descarta la basura del sistema de archivos', () => {
    const files = [
      file('soat.pdf'),
      file('.DS_Store'),
      file('__MACOSX'),
      file('notas.txt'),
      file('impronta.png'),
    ];

    expect(filtrarUtiles(files).map((f) => f.name)).toEqual(['soat.pdf', 'impronta.png']);
  });

  it('deja el lote vacío si nada sirve', () => {
    expect(filtrarUtiles([file('.DS_Store'), file('hoja.xlsx')])).toEqual([]);
  });
});

describe('soloPrimerNivel', () => {
  it('se queda con los archivos de la carpeta elegida y descarta subcarpetas', () => {
    const files = [
      file('soat.pdf', 'tramite/soat.pdf'),
      file('impronta.pdf', 'tramite/impronta.pdf'),
      file('viejo.pdf', 'tramite/historico/viejo.pdf'),
      file('mas-viejo.pdf', 'tramite/historico/2024/mas-viejo.pdf'),
    ];

    expect(soloPrimerNivel(files).map((f) => f.name)).toEqual(['soat.pdf', 'impronta.pdf']);
  });

  it('no toca archivos sueltos sin ruta relativa', () => {
    const files = [file('soat.pdf'), file('impronta.pdf')];

    expect(soloPrimerNivel(files)).toHaveLength(2);
  });
});

// ── Arrastre ─────────────────────────────────────────────────────────────────

/** Entrada de archivo de la File System API de arrastre. */
function fileEntry(name: string) {
  return {
    isFile: true,
    isDirectory: false,
    file: (cb: (f: File) => void) => cb(file(name)),
  };
}

/** Entrada de directorio: `readEntries` entrega por tandas hasta devolver una vacía. */
function dirEntry(hijos: ReturnType<typeof fileEntry>[], porTanda = 100) {
  let cursor = 0;
  return {
    isFile: false,
    isDirectory: true,
    createReader: () => ({
      readEntries: (cb: (entries: unknown[]) => void) => {
        const tanda = hijos.slice(cursor, cursor + porTanda);
        cursor += tanda.length;
        cb(tanda);
      },
    }),
  };
}

function dataTransfer(entries: unknown[], files: File[] = []): DataTransfer {
  return {
    items: entries.map((entry) => ({ webkitGetAsEntry: () => entry })),
    files,
  } as unknown as DataTransfer;
}

describe('archivosDesdeArrastre', () => {
  it('lee archivos sueltos arrastrados', async () => {
    const dt = dataTransfer([fileEntry('soat.pdf'), fileEntry('impronta.pdf')]);

    const files = await archivosDesdeArrastre(dt);

    expect(files.map((f) => f.name)).toEqual(['soat.pdf', 'impronta.pdf']);
  });

  it('entra un nivel en una carpeta arrastrada', async () => {
    const dt = dataTransfer([dirEntry([fileEntry('soat.pdf'), fileEntry('rtm.pdf')])]);

    const files = await archivosDesdeArrastre(dt);

    expect(files.map((f) => f.name)).toEqual(['soat.pdf', 'rtm.pdf']);
  });

  it('lee la carpeta completa aunque el navegador la entregue por tandas', async () => {
    // readEntries devuelve los hijos a trozos; quedarse con el primero pierde archivos.
    const hijos = Array.from({ length: 250 }, (_, i) => fileEntry(`doc${i}.pdf`));
    const dt = dataTransfer([dirEntry(hijos, 100)]);

    const files = await archivosDesdeArrastre(dt);

    expect(files).toHaveLength(250);
  });

  it('filtra la basura también en el arrastre', async () => {
    const dt = dataTransfer([
      dirEntry([fileEntry('soat.pdf'), fileEntry('.DS_Store'), fileEntry('notas.txt')]),
    ]);

    const files = await archivosDesdeArrastre(dt);

    expect(files.map((f) => f.name)).toEqual(['soat.pdf']);
  });

  it('mezcla archivos sueltos y carpetas en el mismo arrastre', async () => {
    const dt = dataTransfer([
      fileEntry('factura.pdf'),
      dirEntry([fileEntry('soat.pdf')]),
    ]);

    const files = await archivosDesdeArrastre(dt);

    expect(files.map((f) => f.name)).toEqual(['factura.pdf', 'soat.pdf']);
  });

  it('cae a dataTransfer.files cuando el navegador no expone entradas', async () => {
    const dt = {
      items: [],
      files: [file('soat.pdf'), file('.DS_Store')],
    } as unknown as DataTransfer;

    const files = await archivosDesdeArrastre(dt);

    expect(files.map((f) => f.name)).toEqual(['soat.pdf']);
  });
});
