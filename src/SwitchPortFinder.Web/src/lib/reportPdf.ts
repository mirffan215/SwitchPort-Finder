import type { ReportResult } from '../api';

/** PDF with the chart (captured from the page, always rendered with the light palette for print) and the data table. Loaded on demand so the main bundle stays small. */
export async function exportReportPdf(chartEl: HTMLElement | null, title: string, subtitle: string, res: ReportResult) {
  const [{ jsPDF }, autoTableMod, html2canvasMod] = await Promise.all([import('jspdf'), import('jspdf-autotable'), import('html2canvas')]);
  const autoTable = autoTableMod.default; const html2canvas = html2canvasMod.default;
  const doc = new jsPDF({ orientation: 'landscape', unit: 'pt', format: 'a4' });
  const W = doc.internal.pageSize.getWidth(); const M = 36;
  doc.setFont('helvetica', 'bold'); doc.setFontSize(18); doc.setTextColor(15, 27, 45); doc.text(title || 'Report', M, 44);
  doc.setFont('helvetica', 'normal'); doc.setFontSize(9); doc.setTextColor(100, 110, 125);
  doc.text(`${subtitle}\nGenerated ${new Date(res.generatedAt).toLocaleString()} · ${res.totalRows} row(s)${res.truncated ? ' (list truncated)' : ''}`, M, 60);
  let y = 92;
  if (chartEl) {
    const canvas = await html2canvas(chartEl, { backgroundColor: '#ffffff', scale: 1.6, logging: false, onclone: (d) => { d.documentElement.setAttribute('data-theme', 'light'); } });
    const w = W - 2 * M; const h = Math.min((canvas.height / canvas.width) * w, 300);
    doc.addImage(canvas.toDataURL('image/jpeg', 0.9), 'JPEG', M, y, (canvas.width / canvas.height) * h > w ? w : (canvas.width / canvas.height) * h, h); y += h + 14;
  }
  const fmt = (v: string | number | null) => (v == null ? '' : typeof v === 'number' ? v.toLocaleString(undefined, { maximumFractionDigits: 2 }) : v);
  autoTable(doc, {
    startY: y, head: [res.columns.map((c) => (c.unit ? `${c.label} (${c.unit})` : c.label))], body: res.rows.slice(0, 1000).map((r) => r.map(fmt)),
    styles: { fontSize: 8, cellPadding: 3 }, headStyles: { fillColor: [14, 116, 144], textColor: 255 }, alternateRowStyles: { fillColor: [244, 247, 250] }, margin: { left: M, right: M },
  });
  const pages = doc.getNumberOfPages();
  for (let i = 1; i <= pages; i++) { doc.setPage(i); doc.setFontSize(8); doc.setTextColor(130); doc.text(`SwitchPort Finder · page ${i} of ${pages}`, W - M, doc.internal.pageSize.getHeight() - 18, { align: 'right' }); }
  doc.save(`${(title || 'report').replace(/[^a-z0-9-_]+/gi, '-').replace(/^-|-$/g, '') || 'report'}-${new Date().toISOString().slice(0, 10)}.pdf`);
}
