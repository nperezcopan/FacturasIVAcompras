# FacturasIvaCompra

Aplicación de escritorio (WinForms, .NET 8) que automatiza la carga del libro **CITI Compras** (RG AFIP 3685) a partir de facturas de compra en PDF: escanea una carpeta, extrae los campos fiscales de cada comprobante (con OCR como fallback), permite revisar/corregir el resultado en una grilla, y graba el lote aprobado en SQL Server (`dbo.AFIP_Citi_Compra`), moviendo cada PDF a una carpeta de procesados.

## Índice

- [Arquitectura](#arquitectura)
- [Flujo end-to-end](#flujo-end-to-end)
- [Extracción de campos (PDF → FacturaCompra)](#extracción-de-campos-pdf--facturacompra)
  - [Resolución de Nro_Proveedor por CUIT](#resolución-de-nroproveedor-por-cuit)
- [OCR (fallback para PDFs escaneados)](#ocr-fallback-para-pdfs-escaneados)
- [Persistencia y validación de duplicados](#persistencia-y-validación-de-duplicados)
- [Movimiento de archivos](#movimiento-de-archivos)
- [UI (WinForms)](#ui-winforms)
- [Configuración](#configuración-appsettingsjson)
- [Logging](#logging)
- [Cómo correr la app](#cómo-correr-la-app)
- [Estructura de carpetas](#estructura-de-carpetas)
- [Limitaciones conocidas / deuda técnica](#limitaciones-conocidas--deuda-técnica)

## Arquitectura

Arquitectura en capas clásica (Clean/Onion light), 4 proyectos, dependencias en una sola dirección:

```
FacturasIvaCompra.UI  ──depende de──>  FacturasIvaCompra.Application  ──depende de──>  FacturasIvaCompra.Domain
        │                                        ▲
        └──depende de──> FacturasIvaCompra.Infrastructure ──depende de──┘
```

- **Domain**: entidades e interfaces puras, sin paquetes NuGet, sin dependencias de infraestructura (`FacturaCompra`, `IFacturaCompraRepository`, `IFileMover`, `IInvoiceFieldExtractor`, `IOcrService`, `IPdfRenderer`, `IPdfTextExtractor`).
- **Application**: orquestación de casos de uso (`BatchInvoiceProcessingService`, `ApprovalService`) y sus modelos de transporte (`InvoicePreviewRow`, `ApprovalResult`, `ProcessingProgress`). Depende solo de abstracciones de Domain.
- **Infrastructure**: implementaciones concretas — acceso a SQL Server (ADO.NET puro, sin ORM), OCR (Tesseract), renderizado de PDF (PDFium), extracción de texto nativo (PdfPig), movimiento de archivos en disco.
- **UI**: WinForms + `Microsoft.Extensions.Hosting` como composition root (inyección de dependencias, logging, configuración).

No hay tests automatizados, ni CI/CD, ni Docker configurado en el repo.

## Flujo end-to-end

1. **Procesar carpeta** (`BtnProcesarCarpeta_Click` → `BatchInvoiceProcessingService.ProcessFolderAsync`): escanea `FolderSettings:SourceFolder` (solo `*.pdf` en el nivel raíz, no recursivo), extrae texto de cada PDF (nativo o vía OCR) y aplica `GenericInvoiceFieldExtractor` para poblar una fila de previsualización (`InvoicePreviewRow`) por archivo. **No graba nada en SQL ni mueve archivos.** Si un archivo individual falla, no se pierde: queda igual en la grilla con todos sus campos críticos marcados como pendientes de revisión.
2. **Revisión en grilla** (`dgvPreview`, panel de preview): las celdas con campos críticos no encontrados se pintan de amarillo; las columnas que nunca se intentan extraer (defaults de negocio, ver [Extracción de campos](#extracción-de-campos-pdf--facturacompra)) se pintan de gris. El usuario puede editar cualquier celda inline antes de aprobar.
3. **Aprobar lote** (`BtnAprobarLote_Click` → `ApprovalService.ApproveBatchAsync`):
   - Separa filas con `Nro_Comprobante_CC` duplicado (dentro del lote o ya existente en la base) — ver [Validación de duplicados](#persistencia-y-validación-de-duplicados).
   - Inserta las filas válidas en SQL Server en una única transacción todo-o-nada (`FacturaCompraSqlRepository.InsertBatchAsync`).
   - Solo si el commit fue exitoso, mueve cada PDF insertado a `FolderSettings:ProcessedFolder` (`FileMoverService.MoveToProcessed`). Los PDFs de filas rechazadas por duplicado **no se mueven** — quedan en la carpeta origen (si se corrige el dato y se vuelve a procesar la carpeta, se recargan).
   - Se muestra un `MessageBox` con el resultado: cuántas se insertaron, cuáles se rechazaron por duplicado (con motivo) y cualquier error al mover un archivo puntual.
4. **Rechazar lote** (`BtnRechazarLote_Click`): descarta la grilla completa sin grabar nada; los PDFs quedan en la carpeta origen.

## Extracción de campos (PDF → FacturaCompra)

`GenericInvoiceFieldExtractor` (Infrastructure) extrae los campos fiscales por **heurísticas y expresiones regulares genéricas**, sin depender de un template por proveedor (diseñado a partir de una factura Movistar de referencia; se espera ajuste con facturas reales de otros proveedores).

Puntos clave de diseño:
- PdfPig a veces concatena palabras sin espacio donde el PDF original usaba posicionamiento en vez de un glifo de espacio (ej. `"Factura3108-00172981"`); por eso los regex usan lookaround (`(?<!\d)`/`(?!\d)`) en vez de `\b`.
- El importe total usa la **última** coincidencia de "Total a Pagar" en el texto, no la primera (en facturas de servicios la primera suele ser saldo anterior).
- Los campos AFIP se graban zero-padded al ancho exacto de columna `char(N)` de SQL Server: `Tipo_Comprobante_CC` → `D3`, `Punto_Venta_CC` → `D5`, `Nro_Comprobante_CC` → `D20`. El mapeo de tipo de comprobante (código impreso o texto "FACTURA/NOTA DE CRÉDITO/NOTA DE DÉBITO" + letra A/B/C) a código AFIP vive en `AfipCbteTypeMapper` (Infrastructure/Utils).
- Los CUIT se guardan **sin guiones** (con guiones son 13 caracteres, no entran en `char(11)`).

**Campos críticos** (si no se pueden extraer, la fila queda marcada como pendiente de revisión, celda amarilla): `Fecha_Comprobante_CC`, `Tipo_Comprobante_CC`, `Punto_Venta_CC`, `Nro_Comprobante_CC`, `CUIT_Emisor_CC`, `Denominacion_Emisor_CC`, `Importe_Total_Operacion_CC`, `Neto_CC`, `Porc_Iva_CC`.

**Campos que nunca se intentan extraer del PDF** (quedan en su default de negocio, celda gris): `Nro_Despacho_CC`, `Codigo_Vendedor_CC`, `Codigo_Moneda_CC`, `Tipo_Cambio_CC`, `Cantidad_Alicuota_IVA_CC`, `Codigo_Operacion_CC`, `Codigo_Fiscal_CC`, `Otro_Tributo_CC`, `IVA_Comision_CC`, `Ret_Iva_CC`, `Fecha_Caja_Banco_CC`, y varios importes de percepción/exención. `Importe_Perc_IVA_CC` es la única excepción "opcional" entre los campos que sí se buscan: si no aparece en el PDF no se marca como faltante.

`Nro_Proveedor` es un caso aparte: no se extrae del PDF, pero tampoco queda en un default fijo — se resuelve por CUIT contra `dbo.PROVEEDORES` (ver [Resolución de Nro_Proveedor](#resolución-de-nroproveedor-por-cuit)).

### Resolución de Nro_Proveedor por CUIT

`FacturaCompraSqlRepository.GetNrosProveedorPorCuitAsync` busca, para el conjunto de CUIT del lote, el `NRO_PROVEEDOR` correspondiente en `dbo.PROVEEDORES` (misma base que `dbo.AFIP_Citi_Compra`). Se ejecuta dos veces:
- Al procesar la carpeta (`BatchInvoiceProcessingService.ProcessFolderAsync`), para que la grilla de previsualización ya muestre el número resuelto.
- Al aprobar el lote (`ApprovalService.ApproveBatchAsync`), como red de seguridad por si el usuario editó el CUIT a mano en la grilla.

**Detalle importante de formato**: `dbo.PROVEEDORES.CUIT` es `char(12)` y guarda el CUIT con un `"0"` de relleno adelante (ej. el CUIT real `30500051929`, de 11 dígitos, vive como `"030500051929"`), mientras que `CUIT_Emisor_CC` son los 11 dígitos limpios. Por eso la comparación es contra `RIGHT(RTRIM(CUIT), 11)`, no contra el campo completo — comparar el campo completo hacía que el cruce fallara siempre, para el 100% de los proveedores.

Si un CUIT no tiene fila asociada en `dbo.PROVEEDORES` (proveedor no registrado en esa tabla maestra), `Nro_Proveedor` queda en `null` silenciosamente — no se marca como campo pendiente de revisión (celda amarilla). Es esperable para proveedores nuevos que todavía no fueron dados de alta ahí.

## OCR (fallback para PDFs escaneados)

Si un PDF no tiene texto embebido (PdfPig no extrae nada de ninguna página), `BatchInvoiceProcessingService` cae a un flujo de OCR:

1. **Render PDF → imagen**: `PdfiumRenderer` (Infrastructure, sobre `PDFiumSharp`/`PDFium.WindowsV2`) rasteriza cada página al DPI configurado (`OcrSettings:OcrResolutionDpi`, default 300).
2. **OCR en dos pasadas** (optimización de performance, para no rasterizar+OCR-ear la página completa siempre):
   - **Pasada 1 (rápida)**: renderiza solo la franja vertical 45%–100% de la página y corre OCR ahí.
   - Si el texto obtenido contiene alguna señal típica de factura (`CUIT`, `FACTURA`, `TOTAL`), se usa ese resultado.
   - **Pasada 2 (completa)**: solo si la pasada parcial no encontró ninguna señal, se renderiza y OCR-ea la página completa.
3. **Motor OCR**: `TesseractOcrService` (Infrastructure, paquete `Tesseract` 5.2.0, motor local — no hay dependencia de servicios cloud). Usa el idioma de `OcrSettings:OcrLanguage` (default `"spa"`) y busca el modelo en `tessdata/{idioma}.traineddata` junto al ejecutable; **si no existe, lo descarga automáticamente** desde el repo oficial de tessdata en GitHub.

Todas las operaciones de render/OCR son sincrónicas y CPU-bound, por eso se ejecutan dentro de `Task.Run` para no bloquear el hilo de UI.

## Persistencia y validación de duplicados

`FacturaCompraSqlRepository` (Infrastructure) usa **ADO.NET puro** (`Microsoft.Data.SqlClient`, sin ORM) contra la tabla `dbo.AFIP_Citi_Compra`. El esquema real de esa tabla (columnas `char` de ancho fijo, tipos, `Cod_CC` como identity) está documentado en `sp_help.rpt` en la raíz del repo — es la fuente de verdad usada para diseñar `FacturaCompra` y los `Truncate()` defensivos antes de cada insert.

**Inserción del lote**: `InsertBatchAsync` abre una única transacción SQL y hace un `INSERT` por fila; si cualquiera falla, se hace rollback del lote completo (todo o nada).

**Validación de unicidad por `Nro_Comprobante_CC`** (`ApprovalService.ApproveBatchAsync`), aplicada *antes* del insert:
1. **Duplicados dentro del mismo lote**: se agrupan las filas por `Nro_Comprobante_CC`; se conserva la primera aparición de cada grupo y se rechazan las siguientes.
2. **Duplicados contra la base**: sobre las filas restantes, se consulta `FacturaCompraSqlRepository.GetExistingComprobantesAsync` (un `SELECT ... WHERE RTRIM(Nro_Comprobante_CC) IN (...)` parametrizado, comparando sin el padding de espacios del `char(20)`) y se rechaza cualquier fila cuyo número ya exista.
3. Solo las filas que pasan ambos filtros se insertan; las rechazadas se listan en `ApprovalResult.DuplicateInvoices` (archivo, número de comprobante y motivo) y se muestran al usuario en el `MessageBox` de resultado. Sus PDFs no se mueven de la carpeta origen.

> **Nota de diseño**: la unicidad implementada es literalmente sobre `Nro_Comprobante_CC`. La clave "real" de un comprobante AFIP suele ser la combinación *Tipo de Comprobante + Punto de Venta + Nro. Comprobante + CUIT Emisor* (el mismo número de comprobante puede repetirse legítimamente entre proveedores distintos). Si en el futuro aparecen falsos positivos de "duplicado" entre proveedores distintos con el mismo número, ampliar `GetExistingComprobantesAsync` para comparar por esa tupla completa.

## Movimiento de archivos

`FileMoverService` (Infrastructure) mueve (no copia) cada PDF aprobado de la carpeta origen a `FolderSettings:ProcessedFolder` (la crea si no existe). Si ya existe un archivo con el mismo nombre en destino, agrega un sufijo numérico incremental (`_1`, `_2`, ...) hasta encontrar un nombre libre — nunca sobrescribe.

## UI (WinForms)

`MainForm` es una clase `partial` dividida en tres archivos:
- `MainForm.cs`: lógica de eventos (handlers de los 3 botones, formateo condicional de celdas, callback de progreso).
- `MainForm.Layout.cs`: construcción de controles — dos paneles superpuestos (`pnlProcesamiento` y `pnlPreview`, ambos `Dock=Fill`) que se alternan por `Visible` (`MostrarPanelProcesamiento()` / `MostrarPanelPreview()`), sin recrear controles. El grid de preview (`dgvPreview`) tiene 33 columnas definidas explícitamente (una por cada propiedad de `InvoicePreviewRow`, salvo `SourceFileName` que es read-only).
- `MainForm.Styles.cs`: constantes de color (fondo, campo crítico faltante en amarillo, default de negocio en gris) y el set de columnas que se consideran "default de negocio" a efectos de formateo.

El panel de procesamiento muestra el log en vivo: `CustomLogger` (UI/Logging) expone un evento estático `OnLog` al que `MainForm` se suscribe para volcar cada línea logueada al `RichTextBox` (`rtbLogs`), además de escribirla a `logs/app.log`.

## Configuración (`appsettings.json`)

Ubicado en `FacturasIvaCompra.UI/appsettings.json` (se copia al output dir en build). Claves:

```json
{
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft": "Warning", "Microsoft.Hosting.Lifetime": "Information" }
  },
  "ConnectionStrings": {
    "AfipCitiCompra": "Server=<servidor>;Database=<basededatos>;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "FolderSettings": {
    "SourceFolder": "<ruta absoluta a la carpeta con los PDFs a procesar>",
    "ProcessedFolder": "<ruta absoluta donde se mueven los PDFs ya aprobados>"
  },
  "OcrSettings": {
    "OcrLanguage": "spa",
    "OcrResolutionDpi": 300
  }
}
```

No hay `appsettings.Development.json` ni variantes por entorno — un solo archivo, valores de entorno local puestos directamente (no hay manejo de secretos vía user-secrets o variables de entorno todavía).

## Logging

- Consola y Debug output (vía `Microsoft.Extensions.Logging`, estándar).
- Archivo: `logs/app.log` junto al ejecutable (formato `[yyyy-MM-dd HH:mm:ss] [LogLevel] mensaje`, con stacktrace en errores). Escritura con lock estático, falla silenciosamente si el archivo está bloqueado por otro proceso.
- Ambos canales se alimentan del mismo pipeline de `ILogger` inyectado por DI; el archivo además retransmite al `RichTextBox` de la UI vía el evento `CustomLogger.OnLog`.

## Cómo correr la app

Requisitos: Windows, .NET 8 SDK, acceso a un SQL Server con la tabla `dbo.AFIP_Citi_Compra` (ver `sp_help.rpt` para el esquema).

```powershell
# Compilar toda la solución
dotnet build FacturasIvaCompra.slnx

# Correr la UI (ajustar antes FacturasIvaCompra.UI/appsettings.json:
# ConnectionStrings:AfipCitiCompra, FolderSettings:SourceFolder, FolderSettings:ProcessedFolder)
dotnet run --project FacturasIvaCompra.UI
```

El primer arranque en una carpeta sin `tessdata/spa.traineddata` descarga automáticamente el modelo de idioma desde GitHub (requiere conexión a internet); si falla la descarga, hay que colocarlo manualmente en `tessdata/` junto al ejecutable.

## Estructura de carpetas

```
FacturasIvaCompra/
├─ FacturasIvaCompra.slnx
├─ sp_help.rpt                     # esquema real de dbo.AFIP_Citi_Compra (fuente de verdad)
├─ factura_ejemplo.pdf             # PDF de muestra para pruebas manuales
├─ FacturasIvaCompra.Domain/
│  ├─ Entities/FacturaCompra.cs
│  ├─ Interfaces/ (IFacturaCompraRepository, IFileMover, IInvoiceFieldExtractor,
│  │               IOcrService, IPdfRenderer, IPdfTextExtractor)
│  └─ Models/InvoiceExtractionResult.cs
├─ FacturasIvaCompra.Application/
│  ├─ Models/ (ApprovalResult, InvoicePreviewRow, ProcessingProgress)
│  └─ Services/ (ApprovalService, BatchInvoiceProcessingService)
├─ FacturasIvaCompra.Infrastructure/
│  ├─ Repositories/FacturaCompraSqlRepository.cs
│  ├─ Services/ (FileMoverService, GenericInvoiceFieldExtractor,
│  │             PdfPigTextExtractor, PdfiumRenderer, TesseractOcrService)
│  └─ Utils/ (AfipCbteTypeMapper, EsArNumberParser)
└─ FacturasIvaCompra.UI/
   ├─ Program.cs                   # composition root (DI, logging, arranque)
   ├─ MainForm.cs / .Layout.cs / .Styles.cs
   ├─ Logging/CustomLogger.cs
   └─ appsettings.json
```

## Limitaciones conocidas / deuda técnica

- **Sin tests automatizados ni CI/CD**: no hay proyecto de tests, ni workflows de GitHub Actions, ni Docker. Cualquier cambio se verifica manualmente contra `factura_ejemplo.pdf` u otras facturas reales.
- **Extracción por regex sin template por proveedor**: funciona bien para el layout de referencia (factura tipo Movistar); facturas con formatos muy distintos pueden requerir ajustar los regex de `GenericInvoiceFieldExtractor`.
- **`ProcessingProgress` tiene campos vestigiales**: `TotalCopied` y `TotalFolderNotFound` existen en el modelo pero `BatchInvoiceProcessingService` nunca los completa (quedan en 0). Además `CurrentPage`/`TotalPages` en este flujo representan "archivo actual / total de archivos" del lote, no páginas de un PDF individual — nombres heredados de un diseño anterior, pueden confundir.
- **Unicidad de comprobante simplificada**: ver nota en [Persistencia y validación de duplicados](#persistencia-y-validación-de-duplicados) — es por `Nro_Comprobante_CC` solo, no por la tupla completa de clave AFIP.
- **Sin manejo de secretos**: la cadena de conexión vive en texto plano en `appsettings.json` (mitigado en este caso porque usa `Trusted_Connection=True`, pero igual conviene moverla a `dotnet user-secrets` o variables de entorno si se despliega fuera de la máquina de desarrollo).
