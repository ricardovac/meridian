import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AccountsService } from '../../core/accounts.service';
import { Account } from '../../core/models';

@Component({
  selector: 'app-deposit-dialog',
  imports: [
    CurrencyPipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './deposit-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DepositDialog {
  private readonly accountsService = inject(AccountsService);
  private readonly dialogRef = inject<MatDialogRef<DepositDialog, boolean>>(MatDialogRef);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly account = inject<Account>(MAT_DIALOG_DATA);
  protected readonly pending = signal(false);

  protected readonly form = new FormGroup({
    amount: new FormControl<number | null>(null, {
      validators: [Validators.required, Validators.min(0.01)],
    }),
  });

  protected submit(): void {
    if (this.pending()) {
      return;
    }
    const amount = this.form.controls.amount.value;
    if (this.form.invalid || amount === null) {
      this.form.markAllAsTouched();
      return;
    }
    this.pending.set(true);
    this.accountsService.deposit(this.account.id, amount).subscribe({
      next: () => {
        this.snackBar.open('Depósito realizado com sucesso.');
        this.dialogRef.close(true);
      },
      error: () => this.pending.set(false),
    });
  }
}
