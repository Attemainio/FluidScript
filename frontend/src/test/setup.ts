// jsdom has no layout. CodeMirror measures text through Range.getClientRects, which jsdom does not
// implement, so the view's measure pass throws once per frame; an empty rect list is what a
// zero-size document reports and is enough for every test here, which reads state, not pixels.
if (typeof Range !== 'undefined') {
  const empty = (): DOMRectList =>
    ({
      length: 0,
      item: () => null,
      [Symbol.iterator]: [][Symbol.iterator],
    }) as unknown as DOMRectList;
  if (typeof Range.prototype.getClientRects !== 'function') {
    Range.prototype.getClientRects = empty;
  }
  if (typeof Range.prototype.getBoundingClientRect !== 'function') {
    Range.prototype.getBoundingClientRect = () => new DOMRect(0, 0, 0, 0);
  }
}
