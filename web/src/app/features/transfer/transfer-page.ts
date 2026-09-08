import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { AccountsService } from '../../core/accounts.service';
import { CreateTransferRequest, isInsufficientFunds, isProblemDetails } from '../../core/models';
import { TransfersService } from '../../core/transfers.service';

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function differentAccountsValidator(group: AbstractControl): ValidationErrors | null {
  const source: unknown = group.get('sourceAccountId')?.value;
  const destination: unknown = group.get('destinationAccountId')?.value;
  const sameAccount = typeof source === 'string' && source !== '' && source === destination;
  return sameAccount ? { sameAccount: true } : null;
}

@Component({
  selector: 'app-transfer-page',
  imports: [
    CurrencyPipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
  ],
  templateUrl: './transfer-page.html',
  styleUrl: './transfer-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TransferPage {
  private readonly accountsService = inject(AccountsService);
  private readonly transfersService = inject(TransfersService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);

  protected readonly accounts = this.accountsService.accounts;
  protected readonly pending = signal(false);
  protected readonly insufficientFundsError = signal<string | null>(null);

  protected readonly form = new FormGroup(
    {
      sourceAccountId: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required],
      }),
      destinationAccountId: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.pattern(UUID_PATTERN)],
      }),
      amount: new FormControl<number | null>(null, {
        validators: [Validators.required, Validators.min(0.01)],
      }),
      description: new FormControl('', {
        nonNullable: true,
        validators: [Validators.maxLength(200)],
      }),
    },
    { validators: [differentAccountsValidator] },
  );

  constructor() {
    this.accountsService.refresh();
  }

  protected submit(): void {
    if (this.pending()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { sourceAccountId, destinationAccountId, amount, description } = this.form.getRawValue();
    if (amount === null) {
      return;
    }
    const request: CreateTransferRequest = {
      sourceAccountId,
      destinationAccountId,
      amount,
      description,
    };

    this.pending.set(true);
    this.insufficientFundsError.set(null);
    this.transfersService.create(request).subscribe({
      next: () => {
        this.snackBar.open('Transferência realizada com sucesso.');
        void this.router.navigate(['/dashboard']);
      },
      error: (error: unknown) => {
        this.pending.set(false);
        if (error instanceof HttpErrorResponse) {
          const problem = isProblemDetails(error.error) ? error.error : null;
          if (isInsufficientFunds(problem)) {
            this.insufficientFundsError.set(
              problem?.detail ?? 'Saldo insuficiente na conta de origem.',
            );
          }
        }
      },
    });
  }
}
