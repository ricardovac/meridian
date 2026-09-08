import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';

const TOKEN_STORAGE_KEY = 'meridian.token';

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
});
