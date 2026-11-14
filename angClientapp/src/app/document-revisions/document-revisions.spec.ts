import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthService } from '../auth.service';
import { DocumentRevisionService, RevisionInfo } from '../document-revision.service';
import { DocumentRevisions } from './document-revisions';

describe('DocumentRevisions workflow', () => {
  const revision = (revisionNumber: number, status: RevisionInfo['status']): RevisionInfo => ({
    revisionNumber, status, baseRevisionNumber: revisionNumber - 1, contentType: 'text/plain',
    sizeBytes: 4, editedBy: 'editor', comment: 'reason', createdAtUtc: '2026-01-01T00:00:00Z'
  });
  let api: {
    list: ReturnType<typeof vi.fn>;
    getContent: ReturnType<typeof vi.fn>;
    activities: ReturnType<typeof vi.fn>;
    comments: ReturnType<typeof vi.fn>;
    save: ReturnType<typeof vi.fn>;
    restore: ReturnType<typeof vi.fn>;
    transition: ReturnType<typeof vi.fn>;
    addComment: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    api = {
      list: vi.fn(() => of([revision(1, 'Approved'), revision(2, 'Draft')])),
      getContent: vi.fn((_id: string, n: number) => of({ text: n === 1 ? 'before\nsame' : 'after\nsame', contentType: 'text/plain' })),
      activities: vi.fn(() => of([])),
      comments: vi.fn(() => of([])),
      save: vi.fn(() => of(revision(3, 'Draft'))),
      restore: vi.fn(() => of(revision(3, 'Draft'))),
      transition: vi.fn(() => of(revision(2, 'InReview'))),
      addComment: vi.fn(() => of({}))
    };
    await TestBed.configureTestingModule({
      imports: [DocumentRevisions],
      providers: [
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'doc-1' } } } },
        { provide: DocumentRevisionService, useValue: api },
        { provide: AuthService, useValue: { canWrite: true, canReview: true } }
      ]
    }).compileComponents();
  });

  it('creates a draft against the approved revision without claiming publication', () => {
    const fixture = TestBed.createComponent(DocumentRevisions);
    fixture.detectChanges();
    fixture.componentInstance.comment = 'reason';
    fixture.componentInstance.save();
    fixture.detectChanges();
    expect(api.save.mock.calls[0][3]).toBe(1);
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('Entwurf 3');
  });

  it('compares proposed text with the approved version and highlights changes', () => {
    const fixture = TestBed.createComponent(DocumentRevisions);
    fixture.detectChanges();
    fixture.componentInstance.view(2);
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.removed').textContent).toBe('before\n');
    expect(fixture.nativeElement.querySelector('.added').textContent).toBe('after\n');
    expect(api.comments).toHaveBeenCalledWith('doc-1', 2);
  });

  it('requires a rejection reason and reports conflicts', () => {
    const fixture = TestBed.createComponent(DocumentRevisions);
    fixture.detectChanges();
    fixture.componentInstance.transition(2, 'reject');
    expect(api.transition).not.toHaveBeenCalled();
    api.transition.mockReturnValue(throwError(() => ({ status: 409 })));
    fixture.componentInstance.decisionReason = 'incorrect';
    fixture.componentInstance.transition(2, 'reject');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('zwischenzeitlich geändert');
  });

  it('submits a comment anchored to selected proposal text', () => {
    const fixture = TestBed.createComponent(DocumentRevisions);
    fixture.detectChanges();
    fixture.componentInstance.view(2);
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();
    const right: HTMLElement = fixture.nativeElement.querySelector('[aria-label="Verglichene Revision"]');
    const node = right.querySelector('.added')!.firstChild!;
    const range = document.createRange();
    range.setStart(node, 0);
    range.setEnd(node, 5);
    const selection = window.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);
    fixture.componentInstance.captureSelection(right);
    fixture.componentInstance.commentText = 'Please check';
    fixture.componentInstance.addComment();
    expect(api.addComment).toHaveBeenCalledWith('doc-1', 2, null, 0, 'after', 'Please check');
    selection.removeAllRanges();
  });

  it('compares large documents when only a small section changed', () => {
    const common = Array.from({ length: 1500 }, (_, i) => `line ${i}\n`).join('');
    api.getContent.mockImplementation((_id: string, n: number) =>
      of({ text: `${common}${n === 1 ? 'before' : 'after'}\n${common}`, contentType: 'text/plain' }));
    const fixture = TestBed.createComponent(DocumentRevisions);
    fixture.detectChanges();
    fixture.componentInstance.view(2);
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.added').textContent).toBe('after\n');
    expect(fixture.nativeElement.querySelector('.removed').textContent).toBe('before\n');
  });
});
