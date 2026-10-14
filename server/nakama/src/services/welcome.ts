import { credit, type LedgerPorts } from "./ledger";

/**
 * The one-time rewards of the first session (GDD 17.2, 676): finishing the tutorial and the first
 * live round each pay once per account, keyed in the ledger so a retry or a second device pays
 * nothing more. The amounts are not in the GDD (OQ-042).
 */
export const WELCOME_REWARDS = {
  tutorial: { coins: 100 },
  firstRound: { coins: 250 },
} as const;

export type WelcomeStep = keyof typeof WELCOME_REWARDS;

/** Pays a step once; the coins paid now, or 0 when it was paid before. */
export function payWelcome(ledger: LedgerPorts, userId: string, step: WelcomeStep): number {
  const coins = WELCOME_REWARDS[step].coins;
  const paid = credit(ledger, {
    userId,
    sourceId: `welcome:${step}`,
    reason: "welcome",
    coins,
    metadata: { step },
  });
  return paid.ok && paid.applied ? coins : 0;
}
