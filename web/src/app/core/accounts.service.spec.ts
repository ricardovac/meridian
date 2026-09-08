import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AccountsService } from './accounts.service';
import { Account } from './models';

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function account(id: string, name: string): Account {
  return {
    id,
    name,
    currency: 'BRL',
    balance: 1000,
    isSystem: false,
    createdAt: '2026-01-15T12:00:00Z',
  };
}

describe('AccountsService', () => {
  let service: AccountsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AccountsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('exposes the loaded accounts in the signal after refresh', () => {
    service.refresh();
    httpMock.expectOne('/api/accounts').flush([account('a-1', 'Main')]);

    expect(service.accounts()).toHaveLength(1);
    expect(service.accounts()[0].name).toBe('Main');
    expect(service.loading()).toBe(false);
  });

  it('clears the accounts signal on reset', () => {
    service.refresh();
    httpMock.expectOne('/api/accounts').flush([account('a-1', 'Main'), account('a-2', 'Savings')]);
    expect(service.accounts()).toHaveLength(2);

    service.reset();

    expect(service.accounts()).toEqual([]);
    expect(service.loading()).toBe(false);
  });

  it('refreshes the accounts after a deposit', () => {
    service.deposit('a-1', 50).subscribe();

    const deposit = httpMock.expectOne('/api/accounts/a-1/deposit');
    expect(deposit.request.method).toBe('POST');
    deposit.flush({});

    httpMock.expectOne('/api/accounts').flush([account('a-1', 'Main')]);
    expect(service.accounts()).toHaveLength(1);
  });

  it('sends a different Idempotency-Key on each deposit', () => {
    const first = depositAndCaptureKey();
    const second = depositAndCaptureKey();

    expect(first).toMatch(UUID_V4);
    expect(second).toMatch(UUID_V4);
    expect(first).not.toBe(second);
  });

  function depositAndCaptureKey(): string | null {
    service.deposit('a-1', 50).subscribe();
    const deposit = httpMock.expectOne('/api/accounts/a-1/deposit');
    deposit.flush({});
    httpMock.expectOne('/api/accounts').flush([account('a-1', 'Main')]);
    return deposit.request.headers.get('Idempotency-Key');
  }
});
