import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { AuthService } from '../auth.service';
import { DocumentRevisionService, RevisionActivity, RevisionComment, RevisionInfo } from '../document-revision.service';

interface DiffPart { text: string; changed: boolean }

@Component({
  selector: 'app-document-revisions',
  imports: [FormsModule, DatePipe],
  templateUrl: './document-revisions.html',
  styleUrl: './document-revisions.css'
})
export class DocumentRevisions implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(DocumentRevisionService);
  protected readonly auth = inject(AuthService);

  readonly documentId = this.route.snapshot.paramMap.get('id') ?? '';
  readonly revisions = signal<RevisionInfo[]>([]);
  readonly activities = signal<RevisionActivity[]>([]);
  readonly activityFilter = signal('all');
  readonly visibleActivities = computed(() => this.activities().filter(a => {
    const filter = this.activityFilter();
    return filter === 'all' || (filter === 'comments' ? a.action.startsWith('comment_') :
      filter === 'review' ? ['submit', 'approve', 'reject'].includes(a.action) : a.action === 'draft_created');
  }));
  readonly comments = signal<RevisionComment[]>([]);
  readonly currentRevision = computed(() => this.revisions().filter(r => r.status === 'Approved').reduce((max, r) => Math.max(max, r.revisionNumber), 0));
  readonly currentInfo = computed(() => this.revisions().find(r => r.revisionNumber === this.currentRevision()));
  readonly editable = computed(() => this.isText(this.currentInfo()?.contentType));
  readonly roots = computed(() => this.comments().filter(c => c.parentId === null));
  readonly message = signal('');
  readonly error = signal('');
  readonly busy = signal(false);
  readonly editorReady = signal(false);
  readonly leftDiff = signal<DiffPart[]>([]);
  readonly rightDiff = signal<DiffPart[]>([]);
  readonly compareMessage = signal('');
  readonly compareReady = signal(false);
  text = '';
  comment = '';
  decisionReason = '';
  restoreReason = '';
  commentText = '';
  replyText: Record<string, string> = {};
  viewing: number | null = null;
  selection: { start: number; text: string } | null = null;
  private loadedRevision = 0;
  private compareRequest = 0;

  ngOnInit(): void { this.reload(); }

  reload(): void {
    this.error.set('');
    this.api.list(this.documentId).subscribe({
      next: list => {
        this.revisions.set(list);
        this.loadCurrent();
        if (this.viewing !== null && list.some(r => r.revisionNumber === this.viewing)) this.view(this.viewing);
      },
      error: e => this.error.set(e.status === 404 ? 'Dokument nicht gefunden.' : 'Verlauf konnte nicht geladen werden.')
    });
    this.loadActivities();
  }

  view(revision: number): void {
    const info = this.revisions().find(r => r.revisionNumber === revision);
    this.viewing = revision;
    this.selection = null;
    this.comments.set([]);
    this.leftDiff.set([]);
    this.rightDiff.set([]);
    this.compareReady.set(false);
    const request = ++this.compareRequest;
    if (!this.isText(info?.contentType) || !this.editable()) {
      this.compareMessage.set('Vergleich und Kommentare sind nur für Textdokumente verfügbar. Bitte laden Sie die Datei herunter.');
      return;
    }
    this.compareMessage.set('Vergleich wird geladen …');
    forkJoin({
      current: this.api.getContent(this.documentId, this.currentRevision()),
      proposed: this.api.getContent(this.documentId, revision)
    }).subscribe({
      next: ({ current, proposed }) => {
        if (request !== this.compareRequest) return;
        const diff = this.diffLines(current.text, proposed.text);
        this.leftDiff.set(diff?.left ?? [{ text: current.text, changed: false }]);
        this.rightDiff.set(diff?.right ?? [{ text: proposed.text, changed: false }]);
        this.compareMessage.set(diff ? '' : 'Der Änderungsbereich ist für die Hervorhebung zu groß. Die Texte werden ohne Hervorhebung angezeigt.');
        this.compareReady.set(true);
        this.loadComments(revision);
      },
      error: () => { if (request === this.compareRequest) this.compareMessage.set('Vergleich konnte nicht geladen werden.'); }
    });
  }

  save(): void {
    if (this.busy() || !this.editable() || this.loadedRevision !== this.currentRevision() || !this.comment.trim()) return;
    this.busy.set(true);
    this.error.set('');
    this.api.save(this.documentId, this.text, this.currentInfo()?.contentType ?? 'text/plain', this.loadedRevision, this.comment).subscribe({
      next: r => this.done(`Entwurf ${r.revisionNumber} gespeichert. Zur Prüfung einreichen, um die Freigabe anzufordern.`),
      error: e => this.fail(e)
    });
  }

  restore(revision: number): void {
    if (this.busy() || !this.restoreReason.trim()) return;
    this.busy.set(true);
    this.api.restore(this.documentId, revision, this.currentRevision(), this.restoreReason).subscribe({
      next: r => { this.restoreReason = ''; this.done(`Revision ${revision} wurde als Entwurf ${r.revisionNumber} vorbereitet.`); },
      error: e => this.fail(e)
    });
  }

  transition(revision: number, action: 'submit' | 'approve' | 'reject'): void {
    if (this.busy() || (action === 'reject' && !this.decisionReason.trim())) return;
    this.busy.set(true);
    this.api.transition(this.documentId, revision, action, this.decisionReason).subscribe({
      next: () => {
        this.decisionReason = '';
        this.done(action === 'approve' ? `Revision ${revision} freigegeben. Die Indizierung wurde angestoßen.` :
          action === 'reject' ? `Revision ${revision} abgelehnt.` : `Revision ${revision} zur Prüfung eingereicht.`);
      },
      error: e => this.fail(e)
    });
  }

  captureSelection(container: HTMLElement): void {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return;
    const range = selection.getRangeAt(0);
    if (!container.contains(range.startContainer) || !container.contains(range.endContainer)) return;
    const prefix = document.createRange();
    prefix.selectNodeContents(container);
    prefix.setEnd(range.startContainer, range.startOffset);
    const selected = range.toString();
    if (selected.length > 4000) {
      this.error.set('Die Auswahl darf höchstens 4000 Zeichen enthalten.');
      return;
    }
    this.selection = { start: prefix.toString().length, text: selected };
  }

  addComment(parentId: string | null = null): void {
    if (this.viewing === null || this.busy()) return;
    const text = parentId ? this.replyText[parentId] : this.commentText;
    if (!text?.trim() || (!parentId && !this.selection)) return;
    this.busy.set(true);
    this.api.addComment(this.documentId, this.viewing, parentId, this.selection?.start ?? 0, this.selection?.text ?? '', text).subscribe({
      next: () => {
        this.busy.set(false);
        if (parentId) this.replyText[parentId] = ''; else { this.commentText = ''; this.selection = null; }
        this.loadComments(this.viewing!);
        this.loadActivities();
      },
      error: e => this.fail(e)
    });
  }

  resolve(comment: RevisionComment): void {
    if (this.viewing === null || this.busy()) return;
    this.busy.set(true);
    this.api.resolveComment(this.documentId, this.viewing, comment.id, !comment.resolved).subscribe({
      next: () => { this.busy.set(false); this.loadComments(this.viewing!); this.loadActivities(); },
      error: e => this.fail(e)
    });
  }

  replies(id: string): RevisionComment[] { return this.comments().filter(c => c.parentId === id); }

  activityLabel(action: string): string {
    const labels: Record<string, string> = {
      draft_created: 'Entwurf erstellt', submit: 'Zur Prüfung eingereicht', approve: 'Freigegeben', reject: 'Abgelehnt',
      comment_added: 'Kommentar hinzugefügt', comment_replied: 'Kommentar beantwortet',
      comment_resolved: 'Kommentar erledigt', comment_reopened: 'Kommentar wieder geöffnet'
    };
    return labels[action] ?? action;
  }

  statusLabel(status: RevisionInfo['status']): string {
    return { Draft: 'Entwurf', InReview: 'In Prüfung', Approved: 'Freigegeben', Rejected: 'Abgelehnt' }[status];
  }

  activityChanges(activity: RevisionActivity): string {
    if (!activity.changes) return '';
    try {
      const value: unknown = JSON.parse(activity.changes);
      if (value && typeof value === 'object') {
        const fields = value as Record<string, unknown>;
        if (typeof fields['contentChanged'] === 'boolean') {
          const type = fields['contentType'] as { before?: string; after?: string } | undefined;
          const size = fields['sizeBytes'] as { before?: number; after?: number } | undefined;
          return `Inhalt: ${fields['contentChanged'] ? 'geändert' : 'unverändert'}; Dateityp: ${type?.before} → ${type?.after}; Größe: ${size?.before} → ${size?.after} Bytes`;
        }
        if (fields['status']) {
          const status = fields['status'] as { before?: string; after?: string };
          return `Status: ${status.before} → ${status.after}`;
        }
        if (typeof fields['commentId'] === 'string') return `Kommentar: ${fields['commentId']}`;
      }
    } catch {
      return `Änderungsdaten: ${activity.changes}`;
    }
    return `Änderungsdaten: ${activity.changes}`;
  }

  focusComment(container: HTMLElement, comment: RevisionComment): void {
    const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
    let node = walker.nextNode();
    let offset = 0;
    let startNode: Node | null = null;
    let startOffset = 0;
    const end = comment.startOffset + comment.selectedText.length;
    while (node) {
      const length = node.textContent?.length ?? 0;
      if (!startNode && comment.startOffset >= offset && comment.startOffset < offset + length) {
        startNode = node;
        startOffset = comment.startOffset - offset;
      }
      if (startNode && end <= offset + length) {
        const range = document.createRange();
        range.setStart(startNode, startOffset);
        range.setEnd(node, end - offset);
        if (range.toString() !== comment.selectedText) break;
        const selection = window.getSelection();
        selection?.removeAllRanges();
        selection?.addRange(range);
        startNode.parentElement?.scrollIntoView({ block: 'center' });
        return;
      }
      offset += length;
      node = walker.nextNode();
    }
    this.error.set('Die kommentierte Textstelle konnte in dieser Ansicht nicht gefunden werden.');
  }

  syncScroll(source: HTMLElement, target: HTMLElement): void {
    const maxSource = source.scrollHeight - source.clientHeight;
    const maxTarget = target.scrollHeight - target.clientHeight;
    if (maxSource > 0) target.scrollTop = source.scrollTop / maxSource * maxTarget;
  }

  download(revision: number): void {
    this.api.download(this.documentId, revision).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `${this.documentId}-rev${revision}${this.extensionFor(blob.type)}`;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: e => this.fail(e)
    });
  }

  private loadCurrent(): void {
    const current = this.currentRevision();
    this.loadedRevision = 0;
    this.editorReady.set(false);
    this.text = '';
    if (current && this.editable()) {
      this.api.getContent(this.documentId, current).subscribe({
        next: c => { if (this.currentRevision() === current) { this.loadedRevision = current; this.text = c.text; this.editorReady.set(true); } },
        error: () => this.error.set('Aktuelle Revision konnte nicht als UTF-8-Text geladen werden.')
      });
    }
  }

  private loadActivities(): void {
    this.api.activities(this.documentId).subscribe({
      next: activities => this.activities.set(activities),
      error: () => this.error.set('Aktivitäten konnten nicht geladen werden.')
    });
  }

  private loadComments(revision: number): void {
    this.api.comments(this.documentId, revision).subscribe({
      next: comments => { if (this.viewing === revision) this.comments.set(comments); },
      error: () => this.error.set('Kommentare konnten nicht geladen werden.')
    });
  }

  private done(message: string): void {
    this.busy.set(false);
    this.comment = '';
    this.message.set(message);
    this.reload();
  }

  private fail(e: { status?: number; error?: { message?: string } }): void {
    this.busy.set(false);
    this.message.set('');
    this.error.set(e.status === 409 ? 'Die Revision oder ihre Basis wurde zwischenzeitlich geändert. Bitte neu laden.' :
      e.status === 400 ? (e.error?.message ?? 'Ungültige Eingabe.') :
      e.status === 404 ? 'Dokument oder Revision nicht gefunden.' : 'Aktion fehlgeschlagen.');
  }

  private isText(type?: string): boolean {
    const value = (type ?? '').toLowerCase();
    return value.startsWith('text/') || value.includes('json') || value.includes('xml');
  }

  private extensionFor(contentType: string): string {
    const extensions: Record<string, string> = {
      'text/plain': '.txt', 'text/markdown': '.md', 'text/html': '.html', 'application/json': '.json',
      'application/xml': '.xml', 'text/xml': '.xml', 'text/csv': '.csv', 'application/pdf': '.pdf'
    };
    return extensions[contentType.split(';')[0].trim().toLowerCase()] ?? '';
  }

  private diffLines(before: string, after: string): { left: DiffPart[]; right: DiffPart[] } | null {
    const beforeLines = before.match(/[^\n]*\n|[^\n]+$/g) ?? [];
    const afterLines = after.match(/[^\n]*\n|[^\n]+$/g) ?? [];
    let prefix = 0;
    while (prefix < beforeLines.length && prefix < afterLines.length && beforeLines[prefix] === afterLines[prefix]) prefix++;
    let suffix = 0;
    while (suffix < beforeLines.length - prefix && suffix < afterLines.length - prefix &&
      beforeLines[beforeLines.length - 1 - suffix] === afterLines[afterLines.length - 1 - suffix]) suffix++;
    const a = beforeLines.slice(prefix, beforeLines.length - suffix);
    const b = afterLines.slice(prefix, afterLines.length - suffix);
    if ((a.length + 1) * (b.length + 1) > 1_000_000) return null;
    const width = b.length + 1;
    const lcs = new Uint32Array((a.length + 1) * width);
    for (let i = a.length - 1; i >= 0; i--)
      for (let j = b.length - 1; j >= 0; j--)
        lcs[i * width + j] = a[i] === b[j] ? 1 + lcs[(i + 1) * width + j + 1] :
          Math.max(lcs[(i + 1) * width + j], lcs[i * width + j + 1]);
    const left: DiffPart[] = beforeLines.slice(0, prefix).map(text => ({ text, changed: false }));
    const right: DiffPart[] = afterLines.slice(0, prefix).map(text => ({ text, changed: false }));
    let i = 0, j = 0;
    while (i < a.length || j < b.length) {
      if (i < a.length && j < b.length && a[i] === b[j]) {
        left.push({ text: a[i], changed: false }); right.push({ text: b[j], changed: false }); i++; j++;
      } else if (j < b.length && (i === a.length || lcs[i * width + j + 1] >= lcs[(i + 1) * width + j])) {
        right.push({ text: b[j++], changed: true });
      } else left.push({ text: a[i++], changed: true });
    }
    for (const text of beforeLines.slice(beforeLines.length - suffix)) {
      left.push({ text, changed: false });
      right.push({ text, changed: false });
    }
    return { left, right };
  }
}
