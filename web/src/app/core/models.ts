/** Currencies supported by the platform. */
export type Currency = 'BRL' | 'USD' | 'EUR';

export type TransferStatus = 'Completed' | 'Failed';

export type LedgerDirection = 'Debit' | 'Credit';

export interface Account {
  id: string;
  name: string;
  currency: Currency;
  balance: number;
  isSystem: boolean;
  createdAt: string;
}

export interface Transfer {
  id: string;
  sourceAccountId: string;
  destinationAccountId: string;
  amount: number;
  currency: Currency;
  description: string;
  status: TransferStatus;
  createdAt: string;
}

export interface LedgerEntry {
  id: string;
  transferId: string;
  accountId: string;
  direction: LedgerDirection;
  amount: number;
  balanceAfter: number;
  createdAt: string;
}

/** Server-side paginated response envelope. */
export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Credentials {
  email: string;
  password: string;
}

export interface AuthResponse {
  token: string;
}

export interface CreateAccountRequest {
  name: string;
  currency: Currency;
}

export interface CreateTransferRequest {
  sourceAccountId: string;
  destinationAccountId: string;
  amount: number;
  description: string;
}

/** RFC 7807 problem details returned by the API on errors. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  type?: string;
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  return (
    typeof value === 'object' &&
    value !== null &&
    ('title' in value || 'detail' in value || 'type' in value || 'status' in value)
  );
}

export const INSUFFICIENT_FUNDS_TYPE = 'insufficient-funds';

export function isInsufficientFunds(problem: ProblemDetails | null): boolean {
  return problem?.type?.includes(INSUFFICIENT_FUNDS_TYPE) ?? false;
}
