import { Component, OnInit, OnDestroy, signal, inject } from '@angular/core';
import { ActivatedRoute, RouterLink }                   from '@angular/router';
import { FormsModule }                                  from '@angular/forms';
import { DatePipe, NgClass }                            from '@angular/common';
import { Subscription, switchMap }                      from 'rxjs';
import { EndpointService }                              from '../../../core/services/endpoint.service';
import { RequestService }                               from '../../../core/services/request.service';
import { SignalrService }                               from '../../../core/services/signalr.service';
import { MockRuleService }                              from '../../../core/services/mock-rule.service';
import { Endpoint }                                     from '../../../core/models/endpoint.models';
import { IncomingRequest, PagedResult }                 from '../../../core/models/request.models';
import { MockRule, SaveMockRuleRequest }                from '../../../core/models/mock-rule.models';
import { SvgIconDirective }                             from '../../../shared/directives/svg-icon.directive';
import { HttpMethodPipe }                               from '../../../shared/pipes/http-method.pipe';
import { FileSizePipe }                                 from '../../../shared/pipes/file-size.pipe';
import { PrettyJsonPipe }                               from '../../../shared/pipes/pretty-json.pipe';
import { ObjectEntriesPipe }                            from '../../../shared/pipes/object-entries.pipe';

type Tab = 'requests' | 'rules' | 'settings';

@Component({
  selector:    'app-endpoint-detail',
  standalone:  true,
  imports:     [RouterLink, FormsModule, DatePipe, NgClass, SvgIconDirective, HttpMethodPipe, FileSizePipe, PrettyJsonPipe, ObjectEntriesPipe],
  templateUrl: './endpoint-detail.component.html',
  styleUrls:   ['./endpoint-detail.component.scss']
})
export class EndpointDetailComponent implements OnInit, OnDestroy {

  private route   = inject(ActivatedRoute);
  private epSvc   = inject(EndpointService);
  private reqSvc  = inject(RequestService);
  private sgrSvc  = inject(SignalrService);
  private ruleSvc = inject(MockRuleService);

  endpoint    = signal<Endpoint | null>(null);
  requests    = signal<IncomingRequest[]>([]);
  paging      = signal<Omit<PagedResult<unknown>, 'items'> | null>(null);
  rules       = signal<MockRule[]>([]);
  loading     = signal(true);
  activeTab   = signal<Tab>('requests');
  selectedReq = signal<IncomingRequest | null>(null);
  liveMode    = signal(true);

  // Rule form
  showRuleForm = signal(false);
  editingRule  = signal<MockRule | null>(null);
  ruleFormData: SaveMockRuleRequest = this.emptyRule();
  savingRule   = signal(false);
  ruleError    = signal('');

  // Settings form
  epName   = '';
  epDesc   = '';
  epActive = true;
  savingEp = signal(false);

  private endpointId = '';
  protected wsId     = '';
  private reqSub?:   Subscription;

  ngOnInit(): void {
    this.route.params.pipe(
      switchMap(p => {
        this.wsId       = p['wsId'];
        this.endpointId = p['id'];
        this.loading.set(true);
        return this.epSvc.getById(this.endpointId);
      })
    ).subscribe({
      next: ep => {
        this.endpoint.set(ep);
        this.epName   = ep.name;
        this.epDesc   = ep.description ?? '';
        this.epActive = ep.isActive;
        this.loadRequests(1);
        this.loadRules();
        if (this.liveMode()) this.sgrSvc.subscribe(this.endpointId);
      },
      error: () => this.loading.set(false)
    });

    this.reqSub = this.sgrSvc.requests$.subscribe(req => {
      if (req.endpointId !== this.endpointId) return;
      this.requests.update(list => [req, ...list]);
      if (this.selectedReq() === null) this.selectedReq.set(req);
    });
  }

  ngOnDestroy(): void {
    this.reqSub?.unsubscribe();
    this.sgrSvc.unsubscribe(this.endpointId);
  }

  loadRequests(page: number): void {
    this.loading.set(true);
    this.reqSvc.getByEndpoint(this.endpointId, page).subscribe({
      next: r => {
        const { items, ...meta } = r;
        this.requests.set(items);
        this.paging.set(meta);
        if (items.length > 0 && !this.selectedReq()) this.selectedReq.set(items[0]);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  loadRules(): void {
    this.ruleSvc.getByEndpoint(this.endpointId).subscribe({
      next: rules => this.rules.set(rules)
    });
  }

  selectRequest(req: IncomingRequest): void { this.selectedReq.set(req); }

  purgeRequests(): void {
    if (!confirm('Delete all requests older than 30 days?')) return;
    this.reqSvc.purge(this.endpointId).subscribe(() => this.loadRequests(1));
  }

  copyUrl(): void {
    const url = this.endpoint()?.webhookUrl;
    if (url) navigator.clipboard.writeText(url);
  }

  regenerateToken(): void {
    if (!confirm('Regenerate the webhook token? The old URL will stop working.')) return;
    this.epSvc.regenerateToken(this.endpointId).subscribe(ep => this.endpoint.set(ep));
  }

  saveSettings(): void {
    this.savingEp.set(true);
    this.epSvc.update(this.endpointId, { name: this.epName, description: this.epDesc || undefined, isActive: this.epActive }).subscribe({
      next: ep => { this.endpoint.set(ep); this.savingEp.set(false); },
      error: ()  => this.savingEp.set(false)
    });
  }

  openCreateRule(): void {
    this.editingRule.set(null);
    this.ruleFormData = this.emptyRule();
    this.ruleError.set('');
    this.showRuleForm.set(true);
  }

  openEditRule(rule: MockRule, ev: MouseEvent): void {
    ev.stopPropagation();
    this.editingRule.set(rule);
    this.ruleFormData = {
      name:                rule.name,
      priority:            rule.priority,
      matchMethod:         rule.matchMethod,
      matchPath:           rule.matchPath,
      matchBodyExpression: rule.matchBodyExpression,
      responseStatus:      rule.responseStatus,
      responseBody:        rule.responseBody,
      responseHeaders:     rule.responseHeaders,
      delayMs:             rule.delayMs,
      isActive:            rule.isActive
    };
    this.ruleError.set('');
    this.showRuleForm.set(true);
  }

  saveRule(): void {
    this.savingRule.set(true);
    const editing = this.editingRule();
    const obs = editing
      ? this.ruleSvc.update(editing.id, this.ruleFormData)
      : this.ruleSvc.create(this.endpointId, this.ruleFormData);

    obs.subscribe({
      next: _ => { this.loadRules(); this.showRuleForm.set(false); this.savingRule.set(false); },
      error: err => { this.ruleError.set(err?.error?.message ?? 'Failed to save rule.'); this.savingRule.set(false); }
    });
  }

  deleteRule(rule: MockRule, ev: MouseEvent): void {
    ev.stopPropagation();
    if (!confirm(`Delete rule "${rule.name}"?`)) return;
    this.ruleSvc.delete(rule.id).subscribe(() =>
      this.rules.update(list => list.filter(r => r.id !== rule.id))
    );
  }

  toggleRule(rule: MockRule, ev: MouseEvent): void {
    ev.stopPropagation();
    this.ruleSvc.toggle(rule.id).subscribe(updated =>
      this.rules.update(list => list.map(r => r.id === updated.id ? updated : r))
    );
  }

  private emptyRule(): SaveMockRuleRequest {
    return { name: '', priority: 1, responseStatus: 200, delayMs: 0 };
  }

  copyText(text: string): void { navigator.clipboard.writeText(text); }

  trackById(index: number, item: { id: string }) { return item.id; }
}
