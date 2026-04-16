import { Component, OnInit, signal }    from '@angular/core';
import { RouterOutlet, RouterLink,
         RouterLinkActive, Router }       from '@angular/router';
import { FormsModule }                   from '@angular/forms';
import { WorkspaceService }              from '../core/services/workspace.service';
import { AuthService }                   from '../core/services/auth.service';
import { Workspace }                     from '../core/models/workspace.models';
import { SvgIconDirective }              from '../shared/directives/svg-icon.directive';

/**
 * Authenticated shell: renders the sidebar (workspace switcher + nav) and the
 * main content area where child routes are projected via <router-outlet>.
 */
@Component({
  selector:    'app-shell',
  standalone:  true,
  imports:     [RouterOutlet, RouterLink, RouterLinkActive, FormsModule, SvgIconDirective],
  templateUrl: './shell.component.html',
  styleUrls:   ['./shell.component.scss']
})
export class ShellComponent implements OnInit {

  workspaces   = signal<Workspace[]>([]);
  activeWs     = signal<Workspace | null>(null);
  loading      = signal(true);
  menuOpen     = signal(false);
  readonly user = this.auth.currentUser;

  showSettings     = signal(false);
  selectedProvider = 'Claude';
  apiKeyInput      = '';
  savingKey        = signal(false);
  keyError         = signal('');
  keySaved         = signal(false);

  constructor(
    private ws:     WorkspaceService,
    private auth:   AuthService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.ws.getAll().subscribe({
      next: list => {
        this.workspaces.set(list);
        if (list.length > 0) this.activeWs.set(list[0]);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  selectWorkspace(ws: Workspace): void {
    this.activeWs.set(ws);
    this.menuOpen.set(false);
    this.router.navigate(['/workspaces', ws.id]);
  }

  openSettings(): void {
    this.selectedProvider = this.user()?.aiProvider ?? 'Claude';
    this.apiKeyInput      = '';
    this.keyError.set('');
    this.keySaved.set(false);
    this.showSettings.set(true);
  }

  saveAiSettings(): void {
    const key = this.apiKeyInput.trim();
    if (!key) { this.keyError.set('API key is required.'); return; }
    this.savingKey.set(true);
    this.keyError.set('');
    this.auth.saveAiSettings(this.selectedProvider, key).subscribe({
      next: () => {
        this.savingKey.set(false);
        this.keySaved.set(true);
        setTimeout(() => this.showSettings.set(false), 1000);
      },
      error: () => {
        this.keyError.set('Failed to save settings.');
        this.savingKey.set(false);
      }
    });
  }

  logout(): void {
    this.auth.logout();
  }
}
