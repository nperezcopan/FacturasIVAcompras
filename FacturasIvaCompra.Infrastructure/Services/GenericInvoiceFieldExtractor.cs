using System.Text.RegularExpressions;
using System.Linq;
using FacturasIvaCompra.Domain.Entities;
using FacturasIvaCompra.Domain.Interfaces;
using FacturasIvaCompra.Domain.Models;
using FacturasIvaCompra.Infrastructure.Utils;
using Microsoft.Extensions.Configuration;

namespace FacturasIvaCompra.Infrastructure.Services
{
    /// <summary>
    /// Extrae los campos fiscales de una factura de compra por heurísticas/regex genéricas,
    /// sin depender de un template por proveedor. Soporta cuatro familias de layout observadas:
    /// la de referencia original (tipo Movistar, etiqueta y valor adyacentes: "Fecha de
    /// emisión: dd/mm/aaaa"), la de comprobantes generados por el portal de AFIP/ARCA
    /// ("Comprobantes en línea"), donde PdfPig extrae las etiquetas agrupadas por un lado y
    /// los valores por otro (no quedan adyacentes en el texto) — los regex "Arca*" son
    /// fallbacks que se prueban solo cuando el patrón "etiqueta adyacente" no matchea —, la
    /// de proveedores con membrete de imagen (p.ej. STOP LOSS Bureau de Reaseguros), donde el
    /// CUIT/razón social del emisor y las etiquetas "Subtotal"/"TOTAL"/"IVA Insc." no están en
    /// el texto nativo (son parte del gráfico de fondo) y solo aparecen si BatchInvoiceProcessingService
    /// agrega una pasada de OCR de página completa — los regex "*Bare*"/"*Insc*" son los
    /// fallbacks para esa familia —, y la de constancias de retención SUSS ("REG.GRAL
    /// RET.SUSS", p.ej. Vicente Trapani S.A.), que NO es una factura de compra AFIP: no tiene
    /// tipo de comprobante A/B/C ni discrimina IVA, y su único importe es el retenido. Se
    /// carga igual en AFIP_Citi_Compra con un Tipo_Comprobante_CC marcador (ver
    /// TipoComprobanteRetencionSuss) por decisión explícita de negocio, no porque encaje en el
    /// esquema CITI Compras / RG 3685.
    /// </summary>
    public class GenericInvoiceFieldExtractor : IInvoiceFieldExtractor
    {
        private readonly IConfiguration _configuration;

        public GenericInvoiceFieldExtractor(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private static readonly Regex FechaEmisionRegex =
            new(@"Fecha\s+de\s+emisi[oó]n\s*:?\s*(\d{1,2}/\d{1,2}/\d{2,4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Plantilla ARCA: "Período Facturado Desde: Hasta: Fecha de Vto. para el pago:" imprime
        // sus 3 valores juntos en una línea ("11/06/2026 11/06/2026 11/06/2026") y la Fecha de
        // Emisión (cuya etiqueta aparece mucho antes en el texto) es el 4° valor, en la línea
        // siguiente, sin etiqueta adyacente.
        private static readonly Regex FechaEmisionArcaRegex =
            new(@"\d{1,2}/\d{1,2}/\d{2,4}\s+\d{1,2}/\d{1,2}/\d{2,4}\s+\d{1,2}/\d{1,2}/\d{2,4}\s*[\r\n]+\s*(\d{1,2}/\d{1,2}/\d{2,4})",
                RegexOptions.Compiled);

        // Plantilla STOP LOSS: la fecha de emisión no tiene ninguna etiqueta adyacente ("Bs.
        // As., dd/mm/aaaa" — el "Bs. As.," es parte del membrete de imagen, no texto). Último
        // recurso: se toma la primera fecha "suelta" del documento, tolerando el espacio que
        // PdfPig/OCR pegan junto a las barras ("18/ 05/ 2026").
        private static readonly Regex FechaBareRegex =
            new(@"(?<!\d)(\d{1,2})\s*/\s*(\d{1,2})\s*/\s*(\d{2,4})(?!\d)", RegexOptions.Compiled);

        // Plantilla RET.SUSS: la fecha va con la etiqueta "Fecha" sola (sin "de emisión"), p.ej.
        // "Fecha  30/4/2026". Se prueba antes que FechaBareRegex (que toma la primera fecha
        // suelta del documento sin importar etiqueta) porque este comprobante también imprime
        // otra fecha suelta más arriba en el membrete (inicio de actividad del emisor), que
        // FechaBareRegex tomaría por error si no hay una etiqueta más específica para preferir.
        private static readonly Regex FechaEtiquetaBareRegex =
            new(@"\bFecha\s*:?\s*(\d{1,2}\s*/\s*\d{1,2}\s*/\s*\d{2,4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Facturas de comisiones (ej. agente/broker) cuyo concepto dice "COMISIONES JULIO 2026",
        // o variantes con texto intermedio como "POR COMISIONES DE SEGUROS MES DE JUNIO DE 2026":
        // la comisión facturada corresponde a un mes distinto al de emisión del comprobante, y
        // Fecha_Caja_Banco_CC debe respetar el mes/año del concepto en vez del de emisión
        // (decisión de negocio explícita). Se tolera hasta 150 caracteres cualesquiera (incl.
        // saltos de línea) entre "COMISIONES" y el mes: en la plantilla ARCA, el texto de la
        // celda "Producto/Servicio" queda partido en dos líneas por el OCR ("...MES DE" /
        // "JUNIO DE 2026"), con las demás columnas de esa misma fila (cantidad, precio, alícuota,
        // subtotal) intercaladas en el medio por el orden de lectura reconstruido — de ahí que no
        // alcance con tolerar solo unas pocas palabras. El año es opcional: si no aparece, se usa
        // el año de Fecha_Comprobante_CC como fallback (ver Extract). El (?!\d) evita que un
        // número largo pegado al concepto (p.ej. "COMISIONES JUNIO 106406", un código de
        // liquidación) se interprete como los primeros 4 dígitos de un año inexistente (daba
        // Anio_CC = 1064).
        private static readonly Regex ComisionesConceptoRegex =
            new(@"COMISIONES[\s\S]{0,150}?(ENERO|FEBRERO|MARZO|ABRIL|MAYO|JUNIO|JULIO|AGOSTO|SEPTIEMBRE|SETIEMBRE|OCTUBRE|NOVIEMBRE|DICIEMBRE)(?:\s+(\d{4})(?!\d))?",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Nota: PdfPig suele pegar palabras sin espacio en los puntos donde el layout original
        // usaba posicionamiento en vez de un glifo de espacio (p.ej. "Factura3108-00172981",
        // "N° 1ORIGINAL"). Como dígito y letra son ambos \w para .NET regex, un \b tradicional
        // no detecta el límite ahí — se usan lookaround (?<!\d)/(?!\d) en su lugar.
        private static readonly Regex CodigoComprobanteRegex =
            new(@"C[oó]d(?:igo)?\.?\s*N[°ºo]?\.?\s*(\d{1,2})(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Plantilla ARCA: el código AFIP se imprime como "COD. 01" / "COD. 04" / "COD. 011",
        // sin el "N°" que exige CodigoComprobanteRegex. Se tolera un espacio entre cada letra
        // porque en la plantilla STOP LOSS PdfPig separa "cod." en "c" + "od." (fuente de paso
        // ancho: ver comentario de clase), quedando "c od. 01" en el texto reconstruido.
        private static readonly Regex CodigoComprobanteArcaRegex =
            new(@"\bC\s*O\s*D\.?\s*(\d{1,3})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TipoDocumentoLetraRegex =
            new(@"(FACTURA|NOTA\s+DE\s+CR[EÉ]DITO|NOTA\s+DE\s+D[EÉ]BITO)(?![A-Za-zÀ-ÿ])[^A-Z]{0,15}\b([ABC])\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ComprobanteNumeroEtiquetadoRegex =
            new(@"(?:Comp\.?\s*N[°º]?\.?|N[°º]\.?)\s*:?\s*(\d{4,5})[\s\-]+(\d{6,8})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Plantilla ARCA: "Punto de Venta: 00004 Comp. Nro: 00000321" — la etiqueta "Comp.
        // Nro:" queda entre los dos números (no antes de ambos ni después de ambos).
        private static readonly Regex ComprobanteNumeroArcaRegex =
            new(@"Punto\s+de\s+Venta\s*:?\s*(\d{4,5})\s*(?:Comp\.?\s*N(?:ro|[°º])\.?\s*:?\s*)?(\d{6,8})",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Se tolera un espacio junto al guión por el mismo motivo que ComprobanteNumeroConLetraRegex
        // (plantilla RET.SUSS: "0000 - 00022129", sin etiqueta "N°"/"Comp." que active ese regex).
        private static readonly Regex ComprobanteNumeroGenericoRegex =
            new(@"(?<!\d)(\d{4,5})\s*-\s*(\d{6,8})(?!\d)", RegexOptions.Compiled);

        // Plantilla STOP LOSS: "Nº A00002- 00008787" — la letra del comprobante (A/B/C) queda
        // pegada al punto de venta, sin la etiqueta "Comp. Nro" que exige
        // ComprobanteNumeroEtiquetadoRegex. Se tolera el espacio que PdfPig deja junto al
        // guión (son dos "palabras" distintas para PdfPig aunque no haya separación visual).
        private static readonly Regex ComprobanteNumeroConLetraRegex =
            new(@"N[°º]\.?\s*:?\s*[A-C]?\s*(\d{4,5})\s*-\s*(\d{6,8})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Se tolera un espacio junto a cada guión por el mismo motivo que ComprobanteNumeroConLetraRegex.
        private static readonly Regex CuitConGuionesRegex =
            new(@"C\.?\s*U\.?\s*I\.?\s*T\.?\s*:?\s*(\d{2}\s*-\s*\d{8}\s*-\s*\d{1})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CuitSinGuionesRegex =
            new(@"C\.?\s*U\.?\s*I\.?\s*T\.?\s*:?\s*(\d{11})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Plantilla ARCA: al estar la etiqueta "CUIT:" separada de su valor, puede terminar
        // adyacente al CUIT de CUALQUIERA de los dos (emisor o Copan, el comprador — siempre
        // el mismo, configurado en CompanySettings:CuitPropio). Fallback: se toman todos los
        // CUIT "pelados" (11 dígitos) del texto y se descarta el de Copan por exclusión.
        private static readonly Regex CuitBareRegex = new(@"\b\d{11}\b", RegexOptions.Compiled);

        // Plantilla STOP LOSS: el CUIT del emisor está en el membrete de imagen, sin texto
        // seleccionable — solo aparece si se agrega una pasada de OCR, y a veces la etiqueta
        // "CUIT" sale mal reconocida (p.ej. "C.U.LT."), por lo que CuitConGuionesRegex tampoco
        // la encuentra. Último recurso: cualquier CUIT "con guiones" del texto que no sea el
        // propio (mismo criterio de exclusión que CuitBareRegex).
        private static readonly Regex CuitBareConGuionesRegex =
            new(@"\b\d{2}\s*-\s*\d{8}\s*-\s*\d{1}\b", RegexOptions.Compiled);

        // Monto tolerante a un espacio suelto pegado a cada separador — "6, 242. 55" en vez de
        // "6,242.55" — típico del OCR (falla de kerning al reconocer el glifo). A diferencia de
        // MontoPattern no exige separador de miles ni 2 decimales exactos, porque acá el
        // objetivo es capturar el monto completo (para que EsArNumberParser.TryParseMonto lo
        // interprete después), no delimitar campos pegados sin separador. El primer grupo usa
        // \d+ (no \d{1,3}) porque el monto puede venir sin separador de miles — con el tope en 3
        // dígitos, un monto como "3788820,78" matcheaba solo "378" y truncaba el importe.
        private const string MontoOcrPattern = @"\d+(?:[.,]\s?\d+)*";

        private static readonly Regex TotalAPagarRegex =
            new($@"Total\s+a\s+[Pp]agar\s*:?\s*\(?\$?\)?\s*({MontoOcrPattern})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ImporteTotalRegex =
            new($@"Importe\s+Total\s*:?\s*\$?\s*({MontoOcrPattern})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Plantilla STOP LOSS: la etiqueta es "TOTAL" sola, sin "Importe"/"a Pagar" delante.
        private static readonly Regex TotalBareRegex =
            new($@"\bTOTAL\s*:?\s*\$?\s*({MontoOcrPattern})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex NetoRegex =
            new($@"(?:Importe\s+Neto\s+Gravado|Neto\s+Gravado|Base\s+Imponible|Subtotal)\s*:?\s*\$?\s*({MontoOcrPattern})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Monto es-AR con separador de miles "." y 2 decimales tras ",": p.ej. "52.645,00".
        // Se usa como bloque para "desempaquetar" filas de tabla que PdfPig pega sin separador
        // (p.ej. "52.645,0027,0014.214,15"): al exigir exactamente 2 dígitos tras la coma en
        // cada número, el motor de regex encuentra dónde termina cada campo aunque no haya
        // espacio entre ellos.
        private const string MontoPattern = @"\d{1,3}(?:\.\d{3})*,\d{2}";

        // Captura la alícuota de IVA y, a continuación, la base imponible de esa misma fila
        // (IVA <alícuota>% <base>): permite derivar Neto_CC cuando no hay etiqueta "Neto
        // Gravado"/"Base Imponible" separada (caso factura Movistar).
        private static readonly Regex PorcIvaRegex =
            new($@"IVA\s*(21|27|10[.,]5|5|2[.,]5)(?:[.,]\d{{1,2}})?\s*%\.?\s*({MontoPattern})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Plantilla ARCA: el pie de página lista TODAS las alícuotas posibles con su importe de
        // IVA (no la base): "IVA 27%: $ 0,00" / "IVA 21%: $ 393736,15" / etc., la mayoría en
        // 0,00. Se usa como fallback cuando PorcIvaRegex (formato "IVA 21% <base>" sin
        // separador, sin las demás alícuotas listadas) no matchea, tomando la única fila con
        // importe distinto de cero. A diferencia de MontoPattern, estos montos NO llevan
        // separador de miles ("393736,15", no "393.736,15"): se usa el mismo patrón laxo
        // ([\d\.,]+) que ImporteTotalRegex/NetoRegex.
        private static readonly Regex PorcIvaTotalesRegex =
            new(@"IVA\s*(21|27|10[.,]5|5|2[.,]5)\s*%\s*:?\s*\$?\s*([\d\.,]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Plantilla STOP LOSS: "IVA Insc. 21  1.083,42" — el símbolo "%" no está (se pierde
        // seguido en el OCR, que es de donde sale esta etiqueta: ver comentario de clase). El
        // importe es el de IVA (no la base), igual que PorcIvaTotalesRegex.
        private static readonly Regex PorcIvaInscRegex =
            new($@"IVA\s+Insc\.?\s*(21|27|10[.,]5|5|2[.,]5)\s*%?\.?\s*:?\s*\$?\s*({MontoOcrPattern})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // La fila de percepción suele venir como "Percepción I.V.A. <base><alícuota><importe>"
        // pegados sin separador; se descartan base y alícuota y se captura el tercer monto.
        private static readonly Regex PercepcionIvaRegex =
            new($@"Percepci[oó]n(?:\s+de)?\s+I\.?\s*V\.?\s*A\.?\.?\s*(?:{MontoPattern})(?:{MontoPattern})({MontoPattern})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // PdfPig no devuelve saltos de línea dentro de una misma página (Page.Text es un único
        // bloque continuo), así que la razón social no puede aislarse "por línea": se busca por
        // sufijo de razón social típico (S.A., SRL, Cooperativa, etc.) cerca del inicio del
        // texto, donde suele imprimirse el emisor en el membrete.
        private static readonly Regex RazonSocialEtiquetadaRegex =
            new(@"Raz[oó]n\s+Social\s*:?\s*([A-Za-zÀ-ÿ0-9&\.,\s]{3,60}?)(?=\s{2,}|CUIT|C\.U\.I\.T|Cod|Domicilio|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RazonSocialPorSufijoRegex =
            new(@"([A-ZÀ-Þ][A-Za-zÀ-ÿ&\.\-]*(?:\s+[A-Za-zÀ-ÿ0-9&\.\-]+){0,6}?\s+(?:S\.A\.|SA\b|S\.R\.L\.|SRL\b|COOPERATIVA(?:\s+DE\s+\w+)*|LIMITADA))", RegexOptions.Compiled);

        // Plantilla ARCA: con el texto reordenado por posición (ver PdfPigTextExtractor), la
        // PRIMERA aparición de "Razón Social:" en el texto es siempre la del emisor (su bloque
        // de detalle se imprime antes que el del comprador); la del comprador aparece más
        // adelante como parte de "Apellido y Nombre / Razón Social:". Más confiable que buscar
        // por sufijo societario, que puede apuntar al comprador cuando el emisor es una persona
        // física sin razón social con sufijo. El valor puede compartir línea con otro campo
        // ("Razón Social: NOMBRE Fecha de Emisión: ..."), por eso el corte contempla varias
        // etiquetas conocidas además del fin de línea.
        private static readonly Regex RazonSocialArcaRegex =
            new(@"Raz[oó]n\s+Social\s*:\s*([^\r\n]*?)(?=\s*(?:Fecha\b|CUIT\b|Condici[oó]n\b|Domicilio\b|\r|\n|$))",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const int VentanaBusquedaRazonSocial = 1200;

        // Plantilla RET.SUSS: constancia de retención SUSS (p.ej. "REG.GRAL RET.SUSS" de
        // Vicente Trapani S.A.) — no es una factura de compra AFIP, ver comentario de clase.
        private static readonly Regex RetencionSussSenalRegex =
            new(@"REG\.?\s*GRAL\.?\s*RET\.?\s*SUSS|CONSTANCIA\s+DE\s+RETENCI[OÓ]N", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Código marcador interno para Tipo_Comprobante_CC en constancias RET.SUSS: no es un
        // código AFIP de la tabla CITI Compras (ver AfipCbteTypeMapper, que solo cubre
        // Factura/Nota/Recibo A/B/C). Se eligió "900" por estar fuera del rango AFIP real
        // (1-99) para que sea identificable en reportes. Es una decisión de negocio explícita:
        // cargar estas constancias en AFIP_Citi_Compra no es correcto para la RG 3685, pero se
        // hace igual para no perder el registro de la retención.
        private const string TipoComprobanteRetencionSuss = "900";

        // Plantilla RET.SUSS: el importe retenido no tiene etiqueta "Total"/"Importe Total" —
        // la columna de la tabla dice solo "IMPORTE", y su valor puede no quedar adyacente a esa
        // etiqueta en el texto reconstruido. Se toma el ÚLTIMO monto con formato es-AR completo
        // (separador de miles + 2 decimales) del documento: es el importe retenido, que se repite
        // en letras ("Son Pesos: ...") y en el círculo del pie, después de la "Base de cálculo"
        // de la tabla. Solo se usa como fallback cuando ya se confirmó la señal RET.SUSS, para no
        // arriesgar falsos positivos en las demás familias de layout.
        private static readonly Regex MontoEsArRegex = new($"({MontoPattern})", RegexOptions.Compiled);

        public InvoiceExtractionResult Extract(string text, string sourceFileName)
        {
            var result = new InvoiceExtractionResult
            {
                RawText = text,
                SourceFileName = sourceFileName
            };
            var factura = result.Factura;
            text ??= string.Empty;
            var esRetencionSuss = RetencionSussSenalRegex.IsMatch(text);

            // Fecha_Comprobante_CC / Mes_CC / Anio_CC
            var fechaMatch = FechaEmisionRegex.Match(text);
            if (!fechaMatch.Success) fechaMatch = FechaEmisionArcaRegex.Match(text);
            var fechaCruda = fechaMatch.Success ? fechaMatch.Groups[1].Value : null;
            if (fechaCruda == null)
            {
                var fechaEtiquetaBareMatch = FechaEtiquetaBareRegex.Match(text);
                if (fechaEtiquetaBareMatch.Success)
                {
                    fechaCruda = fechaEtiquetaBareMatch.Groups[1].Value;
                }
            }
            if (fechaCruda == null)
            {
                var fechaBareMatch = FechaBareRegex.Match(text);
                if (fechaBareMatch.Success)
                {
                    fechaCruda = $"{fechaBareMatch.Groups[1].Value}/{fechaBareMatch.Groups[2].Value}/{fechaBareMatch.Groups[3].Value}";
                }
            }

            if (fechaCruda != null && EsArNumberParser.TryParseFecha(fechaCruda, out var fecha))
            {
                factura.Fecha_Comprobante_CC = fecha;
                factura.Mes_CC = fecha.Month;
                factura.Anio_CC = fecha.Year;
            }
            else
            {
                result.MissingFields.Add(nameof(FacturaCompra.Fecha_Comprobante_CC));
            }

            // MesComisionDetectado: ver comentario de ComisionesConceptoRegex.
            var comisionesMatch = ComisionesConceptoRegex.Match(text);
            if (comisionesMatch.Success && EsArNumberParser.TryParseMesEs(comisionesMatch.Groups[1].Value, out var mesComision))
            {
                var anioComisionGroup = comisionesMatch.Groups[2];
                var anioComision = anioComisionGroup.Success
                    ? int.Parse(anioComisionGroup.Value)
                    : factura.Fecha_Comprobante_CC.Year;
                if (anioComision > 0)
                {
                    result.MesComisionDetectado = new DateTime(anioComision, mesComision, 1);
                }
            }

            // Fecha_Caja_Banco_CC: no se extrae del PDF, se calcula (primer domingo del mes de
            // referencia — el de la comisión si se detectó, si no el de emisión) ya en la
            // extracción para que se vea en la previsualización en vez de quedar vacía hasta
            // aprobar el lote. Sigue siendo editable en la grilla antes de aprobar.
            if (!result.MissingFields.Contains(nameof(FacturaCompra.Fecha_Comprobante_CC)))
            {
                var mesReferencia = result.MesComisionDetectado ?? factura.Fecha_Comprobante_CC;
                factura.Fecha_Caja_Banco_CC = PrimerDomingoDelMes(mesReferencia);
            }

            // Tipo_Comprobante_CC — char(3) en SQL: código AFIP zero-padded (ver AfipCbteTypeMapper).
            // Excepción: constancias RET.SUSS no tienen tipo de comprobante AFIP (no son una
            // factura); se usa el código marcador TipoComprobanteRetencionSuss.
            int tipoComprobante;
            if (esRetencionSuss)
            {
                factura.Tipo_Comprobante_CC = TipoComprobanteRetencionSuss;
                tipoComprobante = 0;
            }
            else if (!TryExtractTipoComprobante(text, out tipoComprobante))
            {
                result.MissingFields.Add(nameof(FacturaCompra.Tipo_Comprobante_CC));
            }
            else
            {
                factura.Tipo_Comprobante_CC = tipoComprobante.ToString("D3");
            }

            // Punto_Venta_CC (char(5)) / Nro_Comprobante_CC (char(20)) — se completan con ceros
            // a la izquierda solo hasta 4 y 8 dígitos respectivamente (no hasta el ancho total
            // de columna, que se dejó con margen).
            var comprobanteMatch = ComprobanteNumeroEtiquetadoRegex.Match(text);
            if (!comprobanteMatch.Success)
                comprobanteMatch = ComprobanteNumeroArcaRegex.Match(text);
            if (!comprobanteMatch.Success)
                comprobanteMatch = ComprobanteNumeroConLetraRegex.Match(text);
            if (!comprobanteMatch.Success)
                comprobanteMatch = ComprobanteNumeroGenericoRegex.Match(text);

            if (comprobanteMatch.Success
                && int.TryParse(comprobanteMatch.Groups[1].Value, out var puntoVenta)
                && long.TryParse(comprobanteMatch.Groups[2].Value, out var nroComprobante))
            {
                factura.Punto_Venta_CC = puntoVenta.ToString("D4");
                factura.Nro_Comprobante_CC = nroComprobante.ToString("D8");
            }
            else
            {
                result.MissingFields.Add(nameof(FacturaCompra.Punto_Venta_CC));
                result.MissingFields.Add(nameof(FacturaCompra.Nro_Comprobante_CC));
            }

            // CUIT_Emisor_CC (char(11)) / Nro_Vendedor_CC (char(20)) — ambos guardan el CUIT
            // sin guiones: con guiones ("30-67881435-7", 13 caracteres) no entra en char(11).
            var cuitPropio = _configuration["CompanySettings:CuitPropio"];
            var cuit = ExtractCuit(text, cuitPropio);
            if (cuit != null)
            {
                var cuitSinGuiones = cuit.Replace("-", "");
                factura.CUIT_Emisor_CC = cuitSinGuiones;
                factura.Nro_Vendedor_CC = cuitSinGuiones;
            }
            else
            {
                result.MissingFields.Add(nameof(FacturaCompra.CUIT_Emisor_CC));
            }

            // Denominacion_Emisor_CC / Nombre_Vendedor_CC
            var razonSocial = ExtractRazonSocial(text);
            if (!string.IsNullOrWhiteSpace(razonSocial))
            {
                factura.Denominacion_Emisor_CC = razonSocial;
                factura.Nombre_Vendedor_CC = razonSocial;
            }
            else
            {
                result.MissingFields.Add(nameof(FacturaCompra.Denominacion_Emisor_CC));
            }

            // Importe_Total_Operacion_CC — se toma la ÚLTIMA aparición de la etiqueta: en
            // resúmenes de cuenta (facturas de servicios) la primera "Total a Pagar" suele ser
            // un encabezado de tabla seguido por el saldo anterior, no el total real de la
            // factura; el total real se repite igual varias veces hacia el pie del documento.
            var totalMatch = LastMatchOrDefault(TotalAPagarRegex, text);
            if (!totalMatch.Success) totalMatch = LastMatchOrDefault(ImporteTotalRegex, text);
            if (!totalMatch.Success) totalMatch = LastMatchOrDefault(TotalBareRegex, text);
            if (!totalMatch.Success && esRetencionSuss) totalMatch = LastMatchOrDefault(MontoEsArRegex, text);
            if (totalMatch.Success && EsArNumberParser.TryParseMonto(totalMatch.Groups[1].Value, out var total))
            {
                factura.Importe_Total_Operacion_CC = total;
            }
            else
            {
                result.MissingFields.Add(nameof(FacturaCompra.Importe_Total_Operacion_CC));
            }

            // Porc_Iva_CC (se calcula antes que Neto_CC porque esta última puede derivarse
            // de la misma fila "IVA <alícuota>% <base>" cuando no hay etiqueta explícita).
            // Excepción: en Factura C (código AFIP 11) el emisor es Monotributista y no
            // discrimina IVA, por lo que el % de IVA nunca aparece en el comprobante — no es
            // un dato faltante a revisar, es 0 por definición. Misma lógica para constancias
            // RET.SUSS, que directamente no tienen IVA discriminado (no son una factura).
            var porcIvaMatch = Match.Empty;
            if (tipoComprobante == 11 || esRetencionSuss)
            {
                factura.Porc_Iva_CC = 0;
            }
            else
            {
                porcIvaMatch = PorcIvaRegex.Match(text);
                if (porcIvaMatch.Success && EsArNumberParser.TryParseMonto(porcIvaMatch.Groups[1].Value, out var porcIva))
                {
                    factura.Porc_Iva_CC = porcIva;
                }
                else if (TryExtractPorcIvaDeTotales(text, out var porcIvaTotales))
                {
                    factura.Porc_Iva_CC = porcIvaTotales;
                }
                else if (TryExtractPorcIvaInsc(text, out var porcIvaInsc))
                {
                    factura.Porc_Iva_CC = porcIvaInsc;
                }
                else
                {
                    result.MissingFields.Add(nameof(FacturaCompra.Porc_Iva_CC));
                }
            }

            // Neto_CC — no aplica a constancias RET.SUSS (no es una factura con neto gravado;
            // la "Base de cálculo" de la tabla es la base de la retención, no un neto de venta).
            var netoMatch = NetoRegex.Match(text);
            if (esRetencionSuss)
            {
                factura.Neto_CC = 0;
            }
            else if (netoMatch.Success && EsArNumberParser.TryParseMonto(netoMatch.Groups[1].Value, out var neto))
            {
                factura.Neto_CC = neto;
            }
            else if (porcIvaMatch.Success && EsArNumberParser.TryParseMonto(porcIvaMatch.Groups[2].Value, out var netoDerivado))
            {
                factura.Neto_CC = netoDerivado;
            }
            else
            {
                result.MissingFields.Add(nameof(FacturaCompra.Neto_CC));
            }

            // Importe_Perc_IVA_CC — su ausencia es válida (muchas facturas no la tienen), no se marca como faltante.
            var percepcionMatch = PercepcionIvaRegex.Match(text);
            if (percepcionMatch.Success && EsArNumberParser.TryParseMonto(percepcionMatch.Groups[1].Value, out var percepcion))
            {
                factura.Importe_Perc_IVA_CC = percepcion;
            }

            // Validación cruzada Total/Neto/IVA: cada campo se extrae con su propio regex y
            // puede "acertar" por separado (matchear una etiqueta válida) pero igual entregar
            // una combinación matemáticamente imposible — p.ej. una etiqueta "Total" que
            // matcheó un número de otra parte del documento (cuota, página, etc.) en vez del
            // total real. Si Total no cierra contra Neto + IVA% (+ percepción), se confía en
            // Neto/IVA% (etiquetas más específicas y menos propensas a falsos positivos que las
            // de Total, ver comentario de Importe_Total_Operacion_CC más arriba) y se recalcula
            // Total, marcándolo como corregido para que el usuario lo revise antes de aprobar
            // en vez de aprobar a ciegas un valor mal interpretado.
            // Se omite si Neto_CC es 0 (constancias RET.SUSS u otro caso sin neto real: la
            // fórmula no tiene sentido) o si Total/Neto/Porc_Iva no se pudieron extraer.
            if (factura.Neto_CC > 0
                && !result.MissingFields.Contains(nameof(FacturaCompra.Importe_Total_Operacion_CC))
                && !result.MissingFields.Contains(nameof(FacturaCompra.Neto_CC))
                && !result.MissingFields.Contains(nameof(FacturaCompra.Porc_Iva_CC)))
            {
                var totalCalculado = factura.Neto_CC * (1 + factura.Porc_Iva_CC / 100m) + factura.Importe_Perc_IVA_CC;
                var tolerancia = Math.Max(1.00m, totalCalculado * 0.01m);
                if (Math.Abs(factura.Importe_Total_Operacion_CC - totalCalculado) > tolerancia)
                {
                    factura.Importe_Total_Operacion_CC = Math.Round(totalCalculado, 2);
                    result.CorrectedFields.Add(nameof(FacturaCompra.Importe_Total_Operacion_CC));
                }
            }

            return result;
        }

        private static DateTime PrimerDomingoDelMes(DateTime referencia)
        {
            var primerDiaDelMes = new DateTime(referencia.Year, referencia.Month, 1);
            var diasHastaDomingo = ((int)DayOfWeek.Sunday - (int)primerDiaDelMes.DayOfWeek + 7) % 7;
            return primerDiaDelMes.AddDays(diasHastaDomingo);
        }

        private static bool TryExtractTipoComprobante(string text, out int tipoComprobanteCC)
        {
            var codigoMatch = CodigoComprobanteRegex.Match(text);
            if (codigoMatch.Success
                && int.TryParse(codigoMatch.Groups[1].Value, out var codigoImpreso)
                && AfipCbteTypeMapper.TryMapCodigo(codigoImpreso, out tipoComprobanteCC))
            {
                return true;
            }

            var codigoArcaMatch = CodigoComprobanteArcaRegex.Match(text);
            if (codigoArcaMatch.Success
                && int.TryParse(codigoArcaMatch.Groups[1].Value, out var codigoArca)
                && AfipCbteTypeMapper.TryMapCodigo(codigoArca, out tipoComprobanteCC))
            {
                return true;
            }

            var letraMatch = TipoDocumentoLetraRegex.Match(text);
            if (letraMatch.Success)
            {
                var tipoDocumento = letraMatch.Groups[1].Value;
                var letra = letraMatch.Groups[2].Value[0];
                if (AfipCbteTypeMapper.TryMapPorLetra(tipoDocumento, letra, out tipoComprobanteCC))
                {
                    return true;
                }
            }

            tipoComprobanteCC = 0;
            return false;
        }

        private static string? ExtractCuit(string text, string? cuitPropio)
        {
            var conGuiones = CuitConGuionesRegex.Match(text);
            if (conGuiones.Success)
            {
                var valor = QuitarEspacios(conGuiones.Groups[1].Value);
                if (string.IsNullOrEmpty(cuitPropio) || valor.Replace("-", "") != cuitPropio)
                    return valor;
            }

            var sinGuiones = CuitSinGuionesRegex.Match(text);
            if (sinGuiones.Success)
            {
                var digits = sinGuiones.Groups[1].Value;
                if (string.IsNullOrEmpty(cuitPropio) || digits != cuitPropio)
                    return $"{digits.Substring(0, 2)}-{digits.Substring(2, 8)}-{digits.Substring(10, 1)}";
            }

            if (!string.IsNullOrEmpty(cuitPropio))
            {
                // Fallback (plantilla ARCA): ninguna etiqueta "CUIT:" quedó adyacente a un valor
                // que no sea el propio (Copan) — se toma el primer CUIT "pelado" del texto distinto
                // del configurado en CompanySettings:CuitPropio.
                var candidato = CuitBareRegex.Matches(text)
                    .Cast<Match>()
                    .Select(m => m.Value)
                    .FirstOrDefault(v => v != cuitPropio);

                if (candidato != null)
                    return $"{candidato.Substring(0, 2)}-{candidato.Substring(2, 8)}-{candidato.Substring(10, 1)}";

                // Fallback (plantilla STOP LOSS): el CUIT del emisor está en el membrete de
                // imagen y solo llega si se agregó una pasada de OCR, cuya etiqueta "CUIT"
                // puede salir mal reconocida (ver comentario de CuitBareConGuionesRegex) — se
                // toma el primer CUIT "con guiones" del texto distinto del propio. Se prueba
                // después del fallback "pelado" de arriba para no alterar el comportamiento ya
                // validado de la plantilla ARCA.
                var candidatoConGuiones = CuitBareConGuionesRegex.Matches(text)
                    .Cast<Match>()
                    .Select(m => QuitarEspacios(m.Value))
                    .FirstOrDefault(v => v.Replace("-", "") != cuitPropio);

                if (candidatoConGuiones != null)
                    return candidatoConGuiones;
            }

            return null;
        }

        private static string QuitarEspacios(string value) => Regex.Replace(value, @"\s+", "");

        private static string ExtractRazonSocial(string text)
        {
            var arca = RazonSocialArcaRegex.Match(text);
            if (arca.Success)
                return arca.Groups[1].Value.Trim();

            var etiquetada = RazonSocialEtiquetadaRegex.Match(text);
            if (etiquetada.Success)
                return etiquetada.Groups[1].Value.Trim();

            var ventana = text[..Math.Min(VentanaBusquedaRazonSocial, text.Length)];
            var porSufijo = RazonSocialPorSufijoRegex.Match(ventana);
            if (porSufijo.Success)
                return porSufijo.Groups[1].Value.Trim();

            return string.Empty;
        }

        private static bool TryExtractPorcIvaDeTotales(string text, out decimal porcIva)
        {
            foreach (Match m in PorcIvaTotalesRegex.Matches(text))
            {
                if (EsArNumberParser.TryParseMonto(m.Groups[1].Value, out porcIva)
                    && EsArNumberParser.TryParseMonto(m.Groups[2].Value, out var importe)
                    && importe > 0)
                {
                    return true;
                }
            }

            porcIva = 0;
            return false;
        }

        private static bool TryExtractPorcIvaInsc(string text, out decimal porcIva)
        {
            var match = PorcIvaInscRegex.Match(text);
            if (match.Success && EsArNumberParser.TryParseMonto(match.Groups[1].Value, out porcIva))
                return true;

            porcIva = 0;
            return false;
        }

        private static Match LastMatchOrDefault(Regex regex, string text)
        {
            Match? last = null;
            foreach (Match m in regex.Matches(text))
                last = m;
            return last ?? Match.Empty;
        }
    }
}
