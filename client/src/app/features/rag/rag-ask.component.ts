import { Component, Input, signal, inject } from '@angular/core';
import { FormsModule }                       from '@angular/forms';
import { DatePipe, DecimalPipe }             from '@angular/common';
import { RagService }                        from '../../core/services/rag.service';
import { RagAnswer }                         from '../../core/models/rag.models';

/**
 * "Ask your webhooks" panel — retrieval-augmented Q&A over an endpoint's captured history.
 * Drop it into an endpoint view: <app-rag-ask [endpointId]="endpoint.id" />
 *
 * Flow: Re-index (chunk + embed into pgvector) → type a question → grounded answer + citations.
 */
@Component({
  selector:    'app-rag-ask',
  standalone:  true,
  imports:     [FormsModule, DatePipe, DecimalPipe],
  templateUrl: './rag-ask.component.html',
  styleUrls:   ['./rag-ask.component.scss']
})
export class RagAskComponent {
  /** The endpoint whose captured webhooks we ask over. */
  @Input({ required: true }) endpointId!: string;

  private svc = inject(RagService);

  question = '';
  asking    = signal(false);
  ingesting = signal(false);
  ingestMsg = signal('');
  error     = signal('');
  answer    = signal<RagAnswer | null>(null);

  ingest(): void {
    this.ingesting.set(true);
    this.ingestMsg.set('');
    this.error.set('');
    this.svc.ingest(this.endpointId).subscribe({
      next: r => {
        this.ingestMsg.set(`Indexed ${r.chunksIndexed} chunks from ${r.requestsProcessed} requests.`);
        this.ingesting.set(false);
      },
      error: err => {
        this.error.set(err?.error?.error ?? 'Indexing failed.');
        this.ingesting.set(false);
      }
    });
  }

  ask(): void {
    const q = this.question.trim();
    if (!q) return;
    this.asking.set(true);
    this.answer.set(null);
    this.error.set('');
    this.svc.ask(this.endpointId, q).subscribe({
      next: a => { this.answer.set(a); this.asking.set(false); },
      error: err => {
        this.error.set(err?.error?.error ?? 'Question failed.');
        this.asking.set(false);
      }
    });
  }
}
