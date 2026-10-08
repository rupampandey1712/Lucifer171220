import { humanize, money, nights, pluralize } from './format';

describe('format helpers', () => {
  it('formats money with currency and never uses floating drift', () => {
    expect(money(1234.5, 'EUR')).toMatch(/1,234\.50|1\.234,50/);
    expect(money(9000, 'JPY')).not.toMatch(/\.00/);
    expect(money(null)).toBe('—');
  });

  it('counts nights and pluralizes', () => {
    expect(nights('2030-01-01', '2030-01-04')).toBe(3);
    expect(pluralize(1, 'night')).toBe('1 night');
    expect(pluralize(2, 'guest')).toBe('2 guests');
  });

  it('humanizes enum names', () => {
    expect(humanize('BedAndBreakfast')).toBe('Bed And Breakfast');
    expect(humanize('EntirePlace')).toBe('Entire Place');
  });
});
