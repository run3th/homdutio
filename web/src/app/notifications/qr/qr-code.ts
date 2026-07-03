import qrcode from 'qrcode-generator';

/**
 * Encode `text` into a QR module matrix (`true` = dark cell) using the dependency-free `qrcode-generator`
 * (push-notifications, Settings QR). Type `0` auto-selects the smallest version that fits; error-correction
 * level `M` is the usual scan-robust default. The returned matrix excludes the quiet zone — the renderer
 * ({@link QrComponent}) adds the margin. Kept as a pure function so it's trivially unit-testable.
 */
export function qrMatrix(text: string): boolean[][] {
  const qr = qrcode(0, 'M');
  qr.addData(text);
  qr.make();

  const count = qr.getModuleCount();
  const matrix: boolean[][] = [];
  for (let row = 0; row < count; row++) {
    const cells: boolean[] = [];
    for (let col = 0; col < count; col++) {
      cells.push(qr.isDark(row, col));
    }
    matrix.push(cells);
  }
  return matrix;
}
