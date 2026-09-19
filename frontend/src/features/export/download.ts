/**
 * Offers a generated file as a download. The Blob is the caller's to keep: a failed or refused
 * download changes nothing about the document, and the same Blob can be offered again (`59`).
 */
export function downloadBlob(document: Document, name: string, blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = name;
  anchor.style.display = 'none';
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
