# Generación ONE-TIME de plantillas blank FUR

Este script **no forma parte del runtime** de `core-api`. Solo produce los PDF blank
que usa `FurOverlayDocumentGenerator` (overlay PdfSharpCore).

Requisitos: Node.js, Chrome/Edge, proyecto BackCrud en `BACKCRUD_ROOT`.

```powershell
cd services/core-api/tools/fur-assets
npm install
$env:BACKCRUD_ROOT = "D:\FLIT\BackCrudTransfer_master"
npm run generate-blanks
```

Salida:

- `src/Flit.Infrastructure/Documents/Fur/Templates/fur-formulario-p1-blank.pdf`
- `src/Flit.Infrastructure/Documents/Fur/Templates/fur-instrucciones-p2-blank.pdf`

Versionar esos PDF en git tras regenerar.
