/* ===========================================================================
   ТОиР-Проф — выгрузка таблицы в Excel (XLSX) без сторонних библиотек.

   XLSX — это ZIP-архив с несколькими XML-файлами. Для одного листа с
   заголовком и строками хватает пяти файлов, а сжатие не обязательно:
   записи кладутся методом STORE, и остаётся посчитать CRC-32. Так в
   бандл не попадает тяжёлая библиотека ради одной кнопки.

   Числа пишутся числовыми ячейками (по ним работают формулы и сортировка
   в Excel), строки — inline-строками, чтобы не собирать sharedStrings.
   =========================================================================== */

export type XlsxCell = string | number | null | undefined

const encoder = new TextEncoder()

/* ------------------------------- CRC-32 ---------------------------------- */

const CRC_TABLE = (() => {
  const table = new Uint32Array(256)
  for (let n = 0; n < 256; n++) {
    let c = n
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
    table[n] = c >>> 0
  }
  return table
})()

function crc32(data: Uint8Array): number {
  let crc = 0xffffffff
  for (let i = 0; i < data.length; i++) crc = CRC_TABLE[(crc ^ data[i]) & 0xff] ^ (crc >>> 8)
  return (crc ^ 0xffffffff) >>> 0
}

/* --------------------------------- ZIP ----------------------------------- */

interface ZipEntry {
  name: Uint8Array
  data: Uint8Array
  crc: number
  offset: number
}

/** ZIP с записями без сжатия (метод 0). Имена — UTF-8 (флаг 0x0800). */
function buildZip(files: { name: string; content: string }[]): Uint8Array {
  const chunks: Uint8Array[] = []
  const entries: ZipEntry[] = []
  let offset = 0

  for (const file of files) {
    const name = encoder.encode(file.name)
    const data = encoder.encode(file.content)
    const crc = crc32(data)
    const header = new DataView(new ArrayBuffer(30))
    header.setUint32(0, 0x04034b50, true)
    header.setUint16(4, 20, true)
    header.setUint16(6, 0x0800, true)
    header.setUint16(8, 0, true)
    header.setUint16(10, 0, true)
    header.setUint16(12, 0x21, true)
    header.setUint32(14, crc, true)
    header.setUint32(18, data.length, true)
    header.setUint32(22, data.length, true)
    header.setUint16(26, name.length, true)
    header.setUint16(28, 0, true)
    chunks.push(new Uint8Array(header.buffer), name, data)
    entries.push({ name, data, crc, offset })
    offset += 30 + name.length + data.length
  }

  const centralStart = offset
  for (const entry of entries) {
    const header = new DataView(new ArrayBuffer(46))
    header.setUint32(0, 0x02014b50, true)
    header.setUint16(4, 20, true)
    header.setUint16(6, 20, true)
    header.setUint16(8, 0x0800, true)
    header.setUint16(10, 0, true)
    header.setUint16(12, 0, true)
    header.setUint16(14, 0x21, true)
    header.setUint32(16, entry.crc, true)
    header.setUint32(20, entry.data.length, true)
    header.setUint32(24, entry.data.length, true)
    header.setUint16(28, entry.name.length, true)
    header.setUint32(42, entry.offset, true)
    chunks.push(new Uint8Array(header.buffer), entry.name)
    offset += 46 + entry.name.length
  }

  const end = new DataView(new ArrayBuffer(22))
  end.setUint32(0, 0x06054b50, true)
  end.setUint16(8, entries.length, true)
  end.setUint16(10, entries.length, true)
  end.setUint32(12, offset - centralStart, true)
  end.setUint32(16, centralStart, true)
  chunks.push(new Uint8Array(end.buffer))

  const result = new Uint8Array(offset + 22)
  let position = 0
  for (const chunk of chunks) {
    result.set(chunk, position)
    position += chunk.length
  }
  return result
}

/* --------------------------------- XML ----------------------------------- */

function escapeXml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    // Управляющие символы XML 1.0 не допускает — Excel откажется открыть файл.
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g, '')
}

/** Буквенное имя столбца: 0 → A, 25 → Z, 26 → AA. */
function columnName(index: number): string {
  let name = ''
  for (let n = index + 1; n > 0; n = Math.floor((n - 1) / 26)) {
    name = String.fromCharCode(65 + ((n - 1) % 26)) + name
  }
  return name
}

function cellXml(value: XlsxCell, ref: string, bold: boolean): string {
  const style = bold ? ' s="1"' : ''
  if (value === null || value === undefined || value === '') return ''
  if (typeof value === 'number' && Number.isFinite(value)) return `<c r="${ref}"${style}><v>${value}</v></c>`
  return `<c r="${ref}" t="inlineStr"${style}><is><t xml:space="preserve">${escapeXml(String(value))}</t></is></c>`
}

/** Имя листа в Excel: не длиннее 31 символа и без []:*?/\. */
function sheetName(title: string): string {
  const cleaned = title.replace(/[[\]:*?/\\]/g, ' ').trim()
  return (cleaned || 'Лист1').slice(0, 31)
}

/** Готовый XLSX-файл: заголовок жирным и закреплён, ширина столбцов по содержимому. */
export function buildXlsx(title: string, header: string[], rows: XlsxCell[][]): Uint8Array {
  const all: XlsxCell[][] = [header, ...rows]
  const widths = header.map((_, col) =>
    Math.min(60, Math.max(8, ...all.map((row) => String(row[col] ?? '').length + 2))),
  )

  const sheetRows = all
    .map((row, r) => {
      const cells = row.map((value, c) => cellXml(value, `${columnName(c)}${r + 1}`, r === 0)).join('')
      return `<row r="${r + 1}">${cells}</row>`
    })
    .join('')

  const cols = widths.map((w, i) => `<col min="${i + 1}" max="${i + 1}" width="${w}" customWidth="1"/>`).join('')

  const sheet =
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">' +
    '<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>' +
    `<cols>${cols}</cols><sheetData>${sheetRows}</sheetData></worksheet>`

  const workbook =
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" ' +
    'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">' +
    `<sheets><sheet name="${escapeXml(sheetName(title))}" sheetId="1" r:id="rId1"/></sheets></workbook>`

  const styles =
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">' +
    '<fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>' +
    '<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>' +
    '<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>' +
    '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>' +
    '<cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>' +
    '<xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs>' +
    '<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>' +
    '</styleSheet>'

  return buildZip([
    {
      name: '[Content_Types].xml',
      content:
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">' +
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>' +
        '<Default Extension="xml" ContentType="application/xml"/>' +
        '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>' +
        '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>' +
        '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>' +
        '</Types>',
    },
    {
      name: '_rels/.rels',
      content:
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>' +
        '</Relationships>',
    },
    { name: 'xl/workbook.xml', content: workbook },
    {
      name: 'xl/_rels/workbook.xml.rels',
      content:
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>' +
        '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>' +
        '</Relationships>',
    },
    { name: 'xl/worksheets/sheet1.xml', content: sheet },
    { name: 'xl/styles.xml', content: styles },
  ])
}
