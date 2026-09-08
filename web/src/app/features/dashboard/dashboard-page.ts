import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AccountsService } from '../../core/accounts.service';
import { Account, Transfer } from '../../core/models';
import { TransfersService } from '../../core/transfers.service';
import { AccountDialog } from './account-dialog';
import { DepositDialog } from './deposit-dialog';

const RECENT_TRANSFERS_PAGE_SIZE = 10;

@Component({
  selector: 'app-dashboard-page',
  imports: [
    CurrencyPipe,
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    MatTableModule,
  ],
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPage {
  private readonly accountsService = inject(AccountsService);
  private readonly transfersService = inject(TransfersService);
  private readonly dialog = inject(MatDialog);

  protected readonly accounts = this.accountsService.accounts;
  protected readonly accountsLoading = this.accountsService.loading;
  protected readonly recentTransfers = signal<Transfer[]>([]);
  protected readonly transfersLoading = signal(false);
  protected readonly transferColumns = ['createdAt', 'source', 'destination', 'amount', 'status'];

  private readonly accountNames = computed(
    () => new Map(this.accounts().map((account) => [account.id, account.name])),
  );

  constructor() {
    this.accountsService.refresh();
    this.loadRecentTransfers();
  }

  protected nameFor(accountId: string): string {
    return this.accountNames().get(accountId) ?? `${accountId.slice(0, 8)}…`;
  }

  protected openNewAccountDialog(): void {
    this.dialog.open(AccountDialog, { width: '420px' });
  }

  protected openDepositDialog(account: Account): void {
    this.dialog
      .open<DepositDialog, Account, boolean>(DepositDialog, { width: '420px', data: account })
      .afterClosed()
      .subscribe((deposited) => {
        if (deposited === true) {
          this.loadRecentTransfers();
        }
      });
  }

  private loadRecentTransfers(): void {
    this.transfersLoading.set(true);
    this.transfersService
      .list({ page: 1, pageSize: RECENT_TRANSFERS_PAGE_SIZE })
      .pipe(finalize(() => this.transfersLoading.set(false)))
      .subscribe({
        next: (page) => this.recentTransfers.set(page.items),
        // Errors are already surfaced by the error interceptor.
        error: () => undefined,
      });
  }
}
