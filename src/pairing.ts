import { randomInt } from 'node:crypto';
import { issueToken } from './config.js';

interface PairingState {
  code: string;
  expiresAt: number;
}

let current: PairingState | null = null;

export function getPairingCode(): PairingState {
  const now = Date.now();
  if (!current || current.expiresAt < now + 30_000) {
    current = {
      code: String(randomInt(100000, 999999)),
      expiresAt: now + 10 * 60_000
    };
  }
  return current;
}

export function confirmPairing(code: string): { token: string } | null {
  if (!current || current.expiresAt < Date.now()) return null;
  if (String(code).trim() !== current.code) return null;
  current = null;
  return { token: issueToken() };
}
