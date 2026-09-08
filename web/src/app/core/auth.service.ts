import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { AuthResponse, Credentials } from './models';

interface JwtPayload {
  exp?: number;
  sub?: string;
  email?: string;
}

const TOKEN_STORAGE_KEY = 'meridian.token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly tokenSignal = signal<string | null>(this.readStoredToken());

  /** Raw JWT, or null when logged out. */
  readonly token = this.tokenSignal.asReadonly();

  readonly isAuthenticated = computed(() => {
    const token = this.tokenSignal();
    return token !== null && !this.isExpired(token);
  });

  readonly userEmail = computed(() => {
    const token = this.tokenSignal();
    if (token === null) {
      return null;
    }
    const payload = this.decodePayload(token);
    return payload?.email ?? payload?.sub ?? null;
  });

  login(credentials: Credentials): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/login', credentials)
      .pipe(tap(({ token }) => this.storeToken(token)));
  }

  register(credentials: Credentials): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/register', credentials)
      .pipe(tap(({ token }) => this.storeToken(token)));
  }

  logout(): void {
    localStorage.removeItem(TOKEN_STORAGE_KEY);
    this.tokenSignal.set(null);
    void this.router.navigate(['/login']);
  }

  private storeToken(token: string): void {
    localStorage.setItem(TOKEN_STORAGE_KEY, token);
    this.tokenSignal.set(token);
  }

  private readStoredToken(): string | null {
    const token = localStorage.getItem(TOKEN_STORAGE_KEY);
    if (token === null) {
      return null;
    }
    if (this.isExpired(token)) {
      localStorage.removeItem(TOKEN_STORAGE_KEY);
      return null;
    }
    return token;
  }

  private isExpired(token: string): boolean {
    const payload = this.decodePayload(token);
    if (payload?.exp === undefined) {
      return false;
    }
    return payload.exp * 1000 <= Date.now();
  }

  private decodePayload(token: string): JwtPayload | null {
    const parts = token.split('.');
    if (parts.length !== 3) {
      return null;
    }
    try {
      const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
      return JSON.parse(atob(base64)) as JwtPayload;
    } catch {
      return null;
    }
  }
}
