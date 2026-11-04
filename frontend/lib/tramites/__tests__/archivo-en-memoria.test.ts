import { describe, expect, it, vi } from 'vitest';
import {
  MENSAJE_ARCHIVO_NO_LEGIBLE,
  copiarArchivoEnMemoria,
  esLecturaDeArchivoDenegada,
} from '../archivo-en-memoria';

describe('copiarArchivoEnMemoria', () => {
  it('deja nombre, tipo y bytes en un File que ya no es el original', async () => {
    const original = new File(['impronta'], 'impronta.pdf', { type: 'application/pdf' });

    const copia = await copiarArchivoEnMemoria(original);

    expect(copia).not.toBe(original);
    expect(copia.name).toBe('impronta.pdf');
    expect(copia.type).toBe('application/pdf');
    expect(await copia.text()).toBe('impronta');
  });

  it('si el disco ya no deja leer el archivo, explica qué puede hacer el usuario', async () => {
    const original = new File(['x'], 'impronta.pdf', { type: 'application/pdf' });
    vi.spyOn(original, 'arrayBuffer').mockRejectedValue(
      new DOMException(
        'The requested file could not be read, typically due to permission problems that have occurred after a reference to a file was acquired.',
        'NotReadableError',
      ),
    );

    await expect(copiarArchivoEnMemoria(original)).rejects.toThrow(MENSAJE_ARCHIVO_NO_LEGIBLE);
  });
});

describe('esLecturaDeArchivoDenegada', () => {
  it('reconoce el NotReadableError del navegador', () => {
    expect(
      esLecturaDeArchivoDenegada(
        new DOMException('The requested file could not be read', 'NotReadableError'),
      ),
    ).toBe(true);
  });

  it('no confunde un error de red', () => {
    expect(esLecturaDeArchivoDenegada(new Error('Failed to fetch'))).toBe(false);
  });
});
