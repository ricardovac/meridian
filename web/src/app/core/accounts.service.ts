import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, finalize, tap } from 'rxjs';
import { Account, CreateAccountRequest, LedgerEntry, Paged, Transfer } from './models';

const ACCOUNTS_URL = '/api/accounts';

@Injectable({ providedIn: 'root' })
export class AccountsService {
  private readonly http = inject(HttpClient);

  private readonly accountsSignal = signal<Account[]>([]);
  private readonly loadingSignal = signal(false);

  /** The current user's accounts; refreshed after every mutation. */
  readonly accounts = this.accountsSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();

  /** Reloads the accounts list into the {@link accounts} signal. */
  refresh(): void {
    this.loadingSignal.set(true);
    this.http
      .get<Account[]>(ACCOUNTS_URL)
      .pipe(finalize(() => this.loadingSignal.set(false)))
      .subscribe({
        next: (accounts) => this.accountsSignal.set(accounts),
        // Errors are already surfaced by the error interceptor.
        error: () => undefined,
      });
  }

  getById(id: string): Observable<Account> {
    return this.http.get<Account>(`${ACCOUNTS_URL}/${id}`);
  }

  create(request: CreateAccountRequest): Observable<Account> {
    return this.http.post<Account>(ACCOUNTS_URL, request).pipe(tap(() => this.refresh()));
  }

  deposit(accountId: string, amount: number): Observable<Transfer> {
    return this.http
      .post<Transfer>(
        `${ACCOUNTS_URL}/${accountId}/deposit`,
        { amount },
        { headers: { 'Idempotency-Key': crypto.randomUUID() } },
      )
      .pipe(tap(() => this.refresh()));
  }

  getEntries(accountId: string, page: number, pageSize: number): Observable<Paged<LedgerEntry>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<Paged<LedgerEntry>>(`${ACCOUNTS_URL}/${accountId}/entries`, { params });
  }
}
