import { TestBed } from '@angular/core/testing';

import { QrComponent } from './qr.component';
import { qrMatrix } from './qr-code';

describe('qrMatrix', () => {
  it('produces a square, non-empty module matrix for a URL', () => {
    const matrix = qrMatrix('https://homdutio.example');
    expect(matrix.length).toBeGreaterThan(0);
    expect(matrix.every((row) => row.length === matrix.length)).toBe(true);
    // A finder pattern's top-left corner is always a dark module.
    expect(matrix[0][0]).toBe(true);
  });

  it('encodes different inputs into different matrices', () => {
    const a = JSON.stringify(qrMatrix('https://a.example'));
    const b = JSON.stringify(qrMatrix('https://b.example/different-path'));
    expect(a).not.toBe(b);
  });
});

describe('QrComponent', () => {
  function render(data: string, size = 150) {
    TestBed.configureTestingModule({ imports: [QrComponent] });
    const fixture = TestBed.createComponent(QrComponent);
    fixture.componentRef.setInput('data', data);
    fixture.componentRef.setInput('size', size);
    fixture.detectChanges();
    return fixture;
  }

  it('renders an svg sized to the input, filling the box edge-to-edge with dark modules', () => {
    const fixture = render('https://homdutio.example', 150);
    const svg = (fixture.nativeElement as HTMLElement).querySelector('svg') as SVGSVGElement;

    expect(svg.getAttribute('width')).toBe('150');
    expect(svg.getAttribute('height')).toBe('150');
    // viewBox is exactly the module grid (no baked-in quiet zone — the frame supplies it).
    const total = fixture.componentInstance.total();
    const moduleCount = qrMatrix('https://homdutio.example').length;
    expect(total).toBe(moduleCount);
    expect(svg.getAttribute('viewBox')).toBe(`0 0 ${total} ${total}`);
    // One backdrop rect + at least one dark-module rect.
    expect(svg.querySelectorAll('rect').length).toBeGreaterThan(1);
  });
});
