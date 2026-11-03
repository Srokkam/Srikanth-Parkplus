import { formatPaise } from './money';

describe('formatPaise', () => {
  it('shows rupees with two decimals and Indian grouping', () => {
    expect(formatPaise(199900)).toBe('₹1,999.00');
    expect(formatPaise(1049400)).toBe('₹10,494.00');
    expect(formatPaise(10000000)).toBe('₹1,00,000.00');
  });

  it('keeps paise', () => {
    expect(formatPaise(49905)).toBe('₹499.05');
    expect(formatPaise(0)).toBe('₹0.00');
  });
});
