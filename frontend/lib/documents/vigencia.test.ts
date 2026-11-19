// Bug #14604 — un documento histórico (versión anterior) nunca cuenta como el vigente de la casilla.
import { describe, expect, it } from 'vitest';
import { findAttachmentByDocTipo } from './doc-tipo';
import { esVigente, soloHistoricos, soloVigentes } from './vigencia';

const vigente = { id: 'v', tipo: 'soat', isHistorico: false };
const historico = { id: 'h', tipo: 'soat', isHistorico: true };
const legado = { id: 'l', tipo: 'fur' }; // backend viejo: sin la bandera

describe('vigencia de adjuntos', () => {
  it('ausente o false es vigente; true es histórico', () => {
    expect(esVigente(vigente)).toBe(true);
    expect(esVigente(legado)).toBe(true);
    expect(esVigente(historico)).toBe(false);
  });

  it('separa vigentes de históricos', () => {
    const todos = [historico, vigente, legado];
    expect(soloVigentes(todos).map((a) => a.id)).toEqual(['v', 'l']);
    expect(soloHistoricos(todos).map((a) => a.id)).toEqual(['h']);
  });

  it('la casilla se empareja solo con el vigente, aunque el histórico venga primero', () => {
    expect(findAttachmentByDocTipo([historico, vigente], 'SOAT')?.id).toBe('v');
  });

  it('un histórico que ningún vigente reemplaza sigue contando (revocatoria marca todo como histórico)', () => {
    const revocado = [historico, { id: 'f', tipo: 'fur', isHistorico: true }];
    expect(findAttachmentByDocTipo(revocado, 'soat')?.id).toBe('h');
    expect(soloVigentes(revocado).map((a) => a.id)).toEqual(['h', 'f']);
    expect(soloHistoricos(revocado)).toEqual([]);
  });
});
