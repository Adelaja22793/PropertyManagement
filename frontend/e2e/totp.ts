import { createHmac } from 'node:crypto'

/** The six-digit code an authenticator app shows now for this key (RFC 6238). */
export function totp(sharedKey: string) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of sharedKey.replace(/[\s=]/g, '').toUpperCase()) bits += alphabet.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)))
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 1000 / 30)))
  const hash = createHmac('sha1', key).update(counter).digest()
  const offset = hash[hash.length - 1] & 0x0f
  return String((hash.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, '0')
}
