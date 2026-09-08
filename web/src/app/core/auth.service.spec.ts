import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AccountsService } from './accounts.service';
import { AuthService } from './auth.service';
import { Account } from './models';

const TOKEN_STORAGE_KEY = 'meridian.token';

const OTHER_USER_ACCOUNT: Account = {
  id: 'account-of-user-a',
  name: 'Conta do usuário A',
  currency: 'BRL',
  balance: 1000,
  isSystem: false,
  createdAt: '2026-01-15T12:00:00Z',
};

function fakeJwt(payload: Record<string, unknown>): string {
  const encode = (value: Record<string, unknown>): string =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode(payload)}.signature`;
}

describe('AuthService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'login', children: [] }]),
      ],
    });
  });

  it('starts unauthenticated when no token is stored', () => {
    const service = TestBed.inject(AuthService);

    expect(service.token()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
    expect(service.userEmail()).toBeNull();
  });

  it('stores the token and exposes auth state after login', () => {
    const service = TestBed.inject(AuthService);
    const httpMock = TestBed.inject(HttpTestingController);
    const token = fakeJwt({
      exp: Math.floor(Date.now() / 1000) + 3600,
      email: 'user@meridian.dev',
    });

    service.login({ email: 'user@meridian.dev', password: 'secret-123' }).subscribe();
    const request = httpMock.expectOne('/api/auth/login');
    expect(request.request.method).toBe('POST');
    request.flush({ token });

    expect(service.token()).toBe(token);
    expect(service.isAuthenticated()).toBe(true);
    expect(service.userEmail()).toBe('user@meridian.dev');
    expect(localStorage.getItem(TOKEN_STORAGE_KEY)).toBe(token);
    httpMock.verify();
  });

  it('discards an expired stored token on startup', () => {
    localStorage.setItem(TOKEN_STORAGE_KEY, fakeJwt({ exp: Math.floor(Date.now() / 1000) - 60 }));

    const service = TestBed.inject(AuthService);

    expect(service.token()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(TOKEN_STORAGE_KEY)).toBeNull();
  });

  it('clears token state on logout', () => {
    localStorage.setItem(
      TOKEN_STORAGE_KEY,
      fakeJwt({ exp: Math.floor(Date.now() / 1000) + 3600, email: 'user@meridian.dev' }),
    );
    const service = TestBed.inject(AuthService);
    expect(service.isAuthenticated()).toBe(true);

    service.logout();

    expect(service.token()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(TOKEN_STORAGE_KEY)).toBeNull();
  });

  it('clears the in-memory accounts on logout', () => {
    const service = TestBed.inject(AuthService);
    const accountsService = TestBed.inject(AccountsService);
    const httpMock = TestBed.inject(HttpTestingController);
    accountsService.refresh();
    httpMock.expectOne('/api/accounts').flush([OTHER_USER_ACCOUNT]);
    expect(accountsService.accounts()).toHaveLength(1);

    service.logout();

    expect(accountsService.accounts()).toEqual([]);
    httpMock.verify();
  });

  it('clears the previous session accounts when another user logs in', () => {
    const service = TestBed.inject(AuthService);
    const accountsService = TestBed.inject(AccountsService);
    const httpMock = TestBed.inject(HttpTestingController);
    accountsService.refresh();
    httpMock.expectOne('/api/accounts').flush([OTHER_USER_ACCOUNT]);
    expect(accountsService.accounts()).toHaveLength(1);

    service.login({ email: 'user-b@meridian.dev', password: 'secret-123' }).subscribe();
    httpMock.expectOne('/api/auth/login').flush({
      token: fakeJwt({ exp: Math.floor(Date.now() / 1000) + 3600, email: 'user-b@meridian.dev' }),
    });

    expect(accountsService.accounts()).toEqual([]);
    httpMock.verify();
  });

  it('clears the previous session accounts when another user registers', () => {
    const service = TestBed.inject(AuthService);
    const accountsService = TestBed.inject(AccountsService);
    const httpMock = TestBed.inject(HttpTestingController);
    accountsService.refresh();
    httpMock.expectOne('/api/accounts').flush([OTHER_USER_ACCOUNT]);
    expect(accountsService.accounts()).toHaveLength(1);

    service.register({ email: 'user-c@meridian.dev', password: 'secret-123' }).subscribe();
    httpMock.expectOne('/api/auth/register').flush({
      token: fakeJwt({ exp: Math.floor(Date.now() / 1000) + 3600, email: 'user-c@meridian.dev' }),
    });

    expect(accountsService.accounts()).toEqual([]);
    httpMock.verify();
  });
});
