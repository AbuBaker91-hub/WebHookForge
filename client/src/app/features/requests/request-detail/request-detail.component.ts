import { Component, OnInit, signal, inject } from '@angular/core';
import { ActivatedRoute, RouterLink }         from '@angular/router';
import { DatePipe, NgClass }                  from '@angular/common';
import { RequestService }                     from '../../../core/services/request.service';
import { AuthService }                        from '../../../core/services/auth.service';
import { IncomingRequest }                    from '../../../core/models/request.models';
import { SvgIconDirective }                   from '../../../shared/directives/svg-icon.directive';
import { HttpMethodPipe }                     from '../../../shared/pipes/http-method.pipe';
import { FileSizePipe }                       from '../../../shared/pipes/file-size.pipe';
import { PrettyJsonPipe }                     from '../../../shared/pipes/pretty-json.pipe';
import { ObjectEntriesPipe }                  from '../../../shared/pipes/object-entries.pipe';

@Component({
  selector:    'app-request-detail',
  standalone:  true,
  imports:     [RouterLink, DatePipe, NgClass, SvgIconDirective, HttpMethodPipe, FileSizePipe, PrettyJsonPipe, ObjectEntriesPipe],
  templateUrl: './request-detail.component.html',
  styleUrls:   ['./request-detail.component.scss']
})
export class RequestDetailComponent implements OnInit {

  private route = inject(ActivatedRoute);
  private svc   = inject(RequestService);
  private auth  = inject(AuthService);

  request    = signal<IncomingRequest | null>(null);
  loading    = signal(true);
  copied     = signal(false);
  analyzing  = signal(false);
  analysis   = signal<string | null>(null);
  analyzeErr = signal('');

  readonly currentUser = this.auth.currentUser;

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.svc.getById(id).subscribe({
      next: r  => { this.request.set(r); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  analyze(id: string): void {
    this.analyzing.set(true);
    this.analysis.set(null);
    this.analyzeErr.set('');
    this.svc.analyze(id).subscribe({
      next: res => { this.analysis.set(res.analysis); this.analyzing.set(false); },
      error: err => {
        this.analyzeErr.set(err?.error?.error ?? 'Analysis failed.');
        this.analyzing.set(false);
      }
    });
  }

  copy(text: string): void {
    navigator.clipboard.writeText(text);
    this.copied.set(true);
    setTimeout(() => this.copied.set(false), 1500);
  }
}
