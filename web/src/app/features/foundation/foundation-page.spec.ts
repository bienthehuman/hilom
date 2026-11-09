import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { FoundationPage } from './foundation-page';

describe('FoundationPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FoundationPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });

  async function render(healthResponse: { body: string } | { error: true }) {
    const fixture = TestBed.createComponent(FoundationPage);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    const request = http.expectOne('/health/ready');
    if ('error' in healthResponse) {
      request.flush('down', { status: 503, statusText: 'Service Unavailable' });
    } else {
      request.flush(healthResponse.body);
    }

    await fixture.whenStable();
    fixture.detectChanges();
    http.verify();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the product name', async () => {
    const page = await render({ body: 'Healthy' });

    expect(page.querySelector('h1')?.textContent).toContain('Hilom');
  });

  it('shows the API as online when the health check passes', async () => {
    const page = await render({ body: 'Healthy' });

    expect(page.textContent).toContain('Online');
  });

  it('shows the API as unreachable when the health check fails', async () => {
    const page = await render({ error: true });

    expect(page.textContent).toContain('Unreachable');
  });
});
