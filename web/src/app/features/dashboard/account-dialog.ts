import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AccountsService } from '../../core/accounts.service';
import { Currency } from '../../core/models';

@Component({
  selector: 'app-account-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
  ],
  templateUrl: './account-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountDialog {
  private readonly accountsService = inject(AccountsService);
  private readonly dialogRef = inject<MatDialogRef<AccountDialog, boolean>>(MatDialogRef);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly pending = signal(false);
  protected readonly currencies: Currency[] = ['BRL', 'USD', 'EUR'];

  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(80)],
    }),
    currency: new FormControl<Currency>('BRL', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  protected submit(): void {
    if (this.pending()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.pending.set(true);
    this.accountsService.create(this.form.getRawValue()).subscribe({
      next: () => {
        this.snackBar.open('Conta criada com sucesso.');
        this.dialogRef.close(true);
      },
      error: () => this.pending.set(false),
    });
  }
}
