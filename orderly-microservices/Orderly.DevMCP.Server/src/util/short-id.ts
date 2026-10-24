const ALPHABET = '23456789abcdefghjkmnpqrstuvwxyz';

/**
 * Generates an unambiguous, typed short identifier matching
 * BuildingBlocks.Identifiers.ShortIdGenerator:
 * - 31-character alphabet without ambiguous chars (0, O, 1, l, I, i, o)
 * - [prefix][8 random chars] (e.g. ord8gzkkd9k)
 */
export function generateShortId(prefix: string = 'ord', length: number = 8): string {
  const bytes = crypto.getRandomValues(new Uint8Array(length));
  let result = prefix.toLowerCase();
  for (let i = 0; i < length; i++) {
    result += ALPHABET[bytes[i]! % ALPHABET.length];
  }
  return result;
}
