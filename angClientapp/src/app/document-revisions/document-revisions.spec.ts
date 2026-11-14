import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthService } from '../auth.service';
import { DocumentRevisionService, RevisionInfo } from '../document-revision.service';
import { DocumentRevisions } from './document-revisions';

describe('DocumentRevisions notifications', () => {
  const revision = (revisionNumber: number): RevisionInfo => ({
    revisionNumber,
    baseRevisionNumber: revisionNumber - 1,
    contentType: 'text/plain',
    sizeBytes: 4,
    editedBy: 'editor',
    comment: null,
    createdAtUtc: '2026-01-01T00:00:00Z'
  });
  let api: {
    list: ReturnType<typeof vi.fn>;
    getContent: ReturnType<typeof vi.fn>;
    save: ReturnType<typeof vi.fn>;
    restore: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    api = {
      list: vi.fn(() => of([revision(1), revision(2)])),
      getContent: vi.fn(() => of({ text: 'test', contentType: 'text/plain' })),
      save: vi.fn(() => of(revision(3))),
      restore: vi.fn(() => of(revision(3)))
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

  it('notifies the editor that saved revisions are queued', () => {
    const fixture = TestBed.createComponent(DocumentRevisions);
    fixture.detectChanges();
    fixture.componentInstance.save();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('Revision 3 gespeichert');
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('Indizierung wurde angestoßen');
  });

  it('notifies the editor that restored revisions are queued', () => {
    const fixture = TestBed.createComponent(DocumentRevisions);
    fixture.detectChanges();
    fixture.componentInstance.restore(1);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('als Revision 3 wiederhergestellt');
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('Indizierung wurde angestoßen');
  });

  it('does not show a success notification when saving fails', () => {
    api.save.mockReturnValue(throwError(() => ({ status: 409 })));
    const fixture = TestBed.createComponent(DocumentRevisions);
    fixture.detectChanges();
    fixture.componentInstance.save();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('zwischenzeitlich geändert');
  });
});
