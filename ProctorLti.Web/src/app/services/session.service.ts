import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface LaunchBoot {
  testRunnerUrl: string | null;
  userName: string | null;
  deploymentId: string | null;
  controlChannel: string;
}

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);

  getSession(id: string): Observable<LaunchBoot> {
    return this.http.get<LaunchBoot>(`/api/session/${encodeURIComponent(id)}`);
  }
}
