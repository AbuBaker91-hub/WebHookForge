import { Injectable, signal, computed } from '@angular/core';
import { HttpClient }                   from '@angular/common/http';
import { Router }                       from '@angular/router';
import { Observable, tap }              from 'rxjs';
import { API }                          from '../constants/api.constants';
import {
  AuthResponse, CurrentUser,
  LoginRequest, RefreshRequest,
  RegisterRequest, RevokeRequest
} from '../models/auth.models';

/**
 * AuthService — manages authentication state.
 *
 * Token storage strategy:
 *   - Access token  → IN-MEMORY ONLY (never written to storage).
 *                     Eliminated on page refresh; silently renewed via refresh token.
 *   - Refresh token → sessionStorage (cleared when the browser tab closes;
 *                     NOT localStorage, so it doesn't persist across sessions or
 *                     expose long-lived credentials to other tabs).
 *
 * Why not localStorage for the access token?
 *   Any JS on the page (including injected scripts via XSS) can read localStorage.
 *   An in-memory token is invisible to scripts running outside Angular's execution context.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {

  // ── In-memory access token — never persisted ─────────────────
  private _accessToken: string | null = null;

  // ── Reactive user state via Angular signals ───────────────────
  private readonly _user = signal<CurrentUser | null>(null);
  readonly currentUser  = this._user.asReadonly();
  readonly isLoggedIn   = computed(() => this._user() !== null);

  private readonly REFRESH_KEY = 'wf_rt';  // sessionStorage key for refresh token

  constructor(private http: HttpClient, private router: Router) {}

  /** Called once on app init to restore the session from a stored refresh token. */
  restoreSession(): Observable<AuthResponse> | null {
    const rt = sessionStorage.getItem(this.REFRESH_KEY);
    if (!rt) return null;
    return this.refresh({ refreshToken: rt });
  }

  register(req: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(API.auth.register(), req)
      .pipe(tap(r => this.applySession(r)));
  }

  login(req: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(API.auth.login(), req)
      .pipe(tap(r => this.applySession(r)));
  }

  refresh(req: RefreshRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(API.auth.refresh(), req)
      .pipe(tap(r => this.applySession(r)));
  }

  saveAiSettings(provider: string | null, apiKey: string | null) {
    return this.http.put(API.auth.aiSettings(), { provider, apiKey }).pipe(
      tap(() => {
        const user = this._user();
        if (user) this._user.set({ ...user, aiProvider: provider as any });
      })
    );
  }

  logout(): void {
    const rt = sessionStorage.getItem(this.REFRESH_KEY);
    if (rt) {
      const body: RevokeRequest = { token: rt };
      this.http.post(API.auth.revoke(), body).subscribe();
    }
    this.clearSession();
    this.router.navigate(['/login']);
  }

  /** Read by the HTTP interceptor to attach the Bearer header. */
  getAccessToken(): string | null { return this._accessToken; }

  // ── Private ──────────────────────────────────────────────────

  private applySession(res: AuthResponse): void {
    this._accessToken = res.accessToken;
    sessionStorage.setItem(this.REFRESH_KEY, res.refreshToken);
    this._user.set(res.user);
  }

  private clearSession(): void {
    this._accessToken = null;
    sessionStorage.removeItem(this.REFRESH_KEY);
    this._user.set(null);
  }
}
