import { CurrencyPipe, DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AccountsService } from '../../core/accounts.service';
import { Account, LedgerEntry } from '../../core/models';

const DEFAULT_PAGE_SIZE = 20;

@Component({
  selector: 'app-account-detail-page',
  imports: [
    CurrencyPipe,
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatTableModule,
  ],
  templateUrl: './account-detail-page.html',
  styleUrl: './account-detail-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountDetailPage {
  private readonly accountsService = inject(AccountsService);

  /** Route param bound via withComponentInputBinding. */
  readonly id = input.required<string>();

  protected readonly account = signal<Account | null>(null);
  protected readonly entries = signal<LedgerEntry[]>([]);
  protected readonly total = signal(0);
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly loading = signal(false);
  protected readonly entryColumns = ['createdAt', 'direction', 'amount', 'balanceAfter'];

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.loadAccount(id));
    });
    effect(() => {
      const id = this.id();
      const page = this.pageIndex() + 1;
      const pageSize = this.pageSize();
      untracked(() => this.loadEntries(id, page, pageSize));
    });
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  private loadAccount(id: string): void {
    this.accountsService.getById(id).subscribe({
      next: (account) => this.account.set(account),
      // Errors are already surfaced by the error interceptor.
      error: () => undefined,
    });
  }

  private loadEntries(id: string, page: number, pageSize: number): void {
    this.loading.set(true);
    this.accountsService
      .getEntries(id, page, pageSize)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (result) => {
          this.entries.set(result.items);
          this.total.set(result.total);
        },
        error: () => undefined,
      });
  }
}
