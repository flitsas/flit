# Render de verificación de documentos

Genera el **mandato**, la **solicitud de trámite virtual** y el **FUR** con datos representativos, para
comprobar a ojo (y con medidas) lo que los tests no pueden garantizar: cuántas páginas ocupa el
documento, si el contenido invade el membrete, qué cuerpo tiene realmente el texto de las firmas y
dónde cae una marca del formulario.

Nació al ajustar el mandato a una sola hoja (HU #11034) y el sello de identidad del comprador
(HU #11035), donde los tests pasaban en verde mientras el PDF se desbordaba.

## Ejecutar

```bash
cd services/core-api/artifacts/render-documentos
dotnet run
```

Escribe `mandato.pdf`, `virtual.pdf` y `fur.pdf` en el directorio de salida, que imprime al terminar.

> Si tienes la API corriendo con `dotnet watch`, el build choca por los locks de `obj/bin`. Añade
> `--artifacts-path <ruta temporal>` para compilar aparte sin parar tu stack.

## Medir el resultado

Con [PyMuPDF](https://pymupdf.readthedocs.io/) (`pip install pymupdf`):

```python
import fitz

d = fitz.open("mandato.pdf")
print(d.page_count)                       # debe ser 1: el contrato cabe en una hoja

p = d[0]
bottom = max(b[3] for b in p.get_text("blocks"))
print(bottom, bottom / p.rect.height)     # hasta dónde llega el contenido

# Cuerpo real del texto de un sello de identidad, y posición de una marca del FUR
for blk in fitz.open("fur.pdf")[0].get_text("dict")["blocks"]:
    for line in blk.get("lines", []):
        for span in line["spans"]:
            if span["text"].startswith("Validación biométrica"):
                print(span["size"], span["bbox"])
```

## Qué NO es

No es una prueba automatizada: no corre en CI ni forma parte de `Flit.slnx`, así que el build de la
solución lo ignora. Es una herramienta de diagnóstico para usar a mano cuando se tocan los
generadores de PDF o las coordenadas del manifiesto del FUR.

Los datos de ejemplo son ficticios y están escritos en el propio `Program.cs`: vendedor y comprador
persona jurídica con representante legal, sellos de identidad en ambas partes y firmas visibles. Ajusta
ese escenario según lo que necesites comprobar.
