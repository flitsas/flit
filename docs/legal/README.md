# Normativa de referencia

El texto oficial vive **una sola vez** en el repositorio, dentro de los estáticos que sirve la
aplicación:

- `frontend/public/legal/resolucion-20233040017145-2023-mintransporte.pdf`
  → servido en `/legal/resolucion-20233040017145-2023-mintransporte.pdf`

**Resolución 20233040017145 del 28 de abril de 2023** (Ministerio de Transporte), que modifica la
Resolución 20223040045295 de 2022 y habilita la virtualidad de los trámites del Registro Nacional
Automotor. Diario Oficial 52386 del 5 de mayo de 2023.
Publicación oficial: <https://www.alcaldiabogota.gov.co/sisjur/normas/Norma1.jsp?i=155039>

Es la **fuente principal** del Centro de Ayuda y de DR-FLIT: el resumen por temas está en
`frontend/lib/manual/articles/normativa.ts` y la premisa de uso, en
`docs/plan-tecnico-dr-flit-v3.md` §6 (la norma es la base del criterio de las respuestas, no solo
el contenido del apartado «Normativa»).

Al reemplazar o adicionar una norma: sustituir el PDF en `frontend/public/legal/`, actualizar el
resumen y las citas del manual, y correr `frontend/lib/manual/__tests__/normativa.test.ts`.
