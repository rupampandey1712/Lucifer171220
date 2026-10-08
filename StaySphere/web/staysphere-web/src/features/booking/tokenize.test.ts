import { tokenizeTestCard } from './BookingPages';

describe('tokenizeTestCard', () => {
  it('maps simulator test cards to provider tokens so card numbers never reach the API', () => {
    expect(tokenizeTestCard('4242 4242 4242 4242')).toBe('tok_4242');
    expect(tokenizeTestCard('4000-0000-0000-0002')).toBe('tok_0002');
    expect(tokenizeTestCard('4000000000000119')).toBe('tok_0119');
  });

  it('never forwards unknown numbers', () => {
    const token = tokenizeTestCard('5555 5555 5555 4444');
    expect(token).toBe('tok_invalid');
    expect(token).not.toContain('5555');
  });
});
