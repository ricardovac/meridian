import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { Credentials } from '../../core/models';

const MIN_PASSWORD_LENGTH = 8;

@Component({
  selector: 'app-login-page',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './login-page.html',
  styleUrl: './login-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly isRegisterMode = signal(false);
  protected readonly pending = signal(false);
  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;

  protected readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  protected toggleMode(): void {
    this.isRegisterMode.update((value) => !value);
    const password = this.form.controls.password;
    password.setValidators(
      this.isRegisterMode()
        ? [Validators.required, Validators.minLength(MIN_PASSWORD_LENGTH)]
        : [Validators.required],
    );
    password.updateValueAndValidity();
  }

  protected submit(): void {
    if (this.pending()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const credentials: Credentials = this.form.getRawValue();
    const request$ = this.isRegisterMode()
      ? this.auth.register(credentials)
      : this.auth.login(credentials);

    this.pending.set(true);
    request$.subscribe({
      next: () => void this.router.navigate(['/dashboard']),
      error: () => this.pending.set(false),
    });
  }
}
