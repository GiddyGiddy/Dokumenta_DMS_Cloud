import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { FileUploadService, UploadReceipt, UploadedFileInfo } from '../file-upload.service';

import { FileUpload } from './file-upload';

describe('FileUpload', () => {
  let component: FileUpload;
  let fixture: ComponentFixture<FileUpload>;
  let fileUploadService: {
    postFile: ReturnType<typeof vi.fn>;
    archiveAsPdfA: ReturnType<typeof vi.fn>;
    convertDocument: ReturnType<typeof vi.fn>;
    getUploadedFiles: ReturnType<typeof vi.fn>;
    getFileUrl: ReturnType<typeof vi.fn>;
  };

  const uploadReceipt: UploadReceipt = {
    id: 'document-123',
    originalFileName: 'report.docx',
    storedFileName: 'document-123',
    size: 12,
  };

  beforeEach(async () => {
    fileUploadService = {
      postFile: vi.fn(() => of(uploadReceipt)),
      archiveAsPdfA: vi.fn(() => of(true)),
      convertDocument: vi.fn(() => of(true)),
      getUploadedFiles: vi.fn(() => of([] as UploadedFileInfo[])),
      getFileUrl: vi.fn((storedFileName: string) => `/files/${storedFileName}`),
    };

    await TestBed.configureTestingModule({
      imports: [FileUpload],
      providers: [{ provide: FileUploadService, useValue: fileUploadService }],
    }).compileComponents();

    fixture = TestBed.createComponent(FileUpload);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('queues multiple files, rejecting oversized and empty files independently', () => {
    const valid = new File(['a'], 'report.docx');
    const atLimit = new File([new Uint8Array(component.maxFileSizeBytes)], 'limit.pdf');
    const oversized = new File([new Uint8Array(component.maxFileSizeBytes + 1)], 'large.pdf');
    const empty = new File([], 'empty.txt');
    component.addFiles([valid, atLimit, oversized, empty]);

    expect(component.queue.map(item => item.status)).toEqual(['queued', 'queued', 'invalid', 'invalid']);
    expect(component.queue[2].message).toContain('5 MB');
    expect(component.queue[3].message).toContain('empty');
    component.uploadFileToActivity();
    expect(fileUploadService.postFile).toHaveBeenCalledTimes(2);
    expect(fileUploadService.postFile).toHaveBeenCalledWith(valid, false);
  });

  it('accepts multiple files through the file input', () => {
    fixture.detectChanges();
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input[type="file"]');
    const files = [new File(['a'], 'one.pdf'), new File(['b'], 'two.pdf')];
    Object.defineProperty(input, 'files', { configurable: true, value: files });
    expect(input.multiple).toBe(true);
    input.dispatchEvent(new Event('change'));
    expect(component.queue.map(item => item.file)).toEqual(files);
  });

  it('uploads at most two files concurrently and applies PDF/A per file', () => {
    const files = ['one.docx', 'two.png', 'three.pdf'].map(name => new File(['x'], name));
    const requests = files.map(() => new Subject<UploadReceipt>());
    files.forEach((file, index) => {
      fileUploadService.postFile.mockImplementationOnce(() => requests[index]);
    });
    component.addFiles(files);
    component.queue[0].archiveAsPdfA = true;
    expect(component.canArchiveAsPdfA(files[0])).toBe(true);
    expect(component.canArchiveAsPdfA(files[1])).toBe(false);

    component.uploadFileToActivity();
    expect(fileUploadService.postFile).toHaveBeenCalledTimes(2);
    expect(fileUploadService.postFile).toHaveBeenNthCalledWith(1, files[0], true);
    expect(fileUploadService.postFile).toHaveBeenNthCalledWith(2, files[1], false);
    expect(component.queue.map(item => item.status)).toEqual(['uploading', 'uploading', 'queued']);

    requests[0].next(uploadReceipt);
    requests[0].complete();
    expect(fileUploadService.postFile).toHaveBeenCalledTimes(3);
    requests[1].next(uploadReceipt);
    requests[1].complete();
    requests[2].next(uploadReceipt);
    requests[2].complete();
    expect(component.queue.map(item => item.status)).toEqual(['uploaded', 'uploaded', 'uploaded']);
    expect(component.successMessage).toContain('3 uploaded');
    expect(fileUploadService.getUploadedFiles).toHaveBeenCalledTimes(2);
  });

  it('continues after a failure and retries only the failed file', () => {
    const files = [new File(['a'], 'one.docx'), new File(['b'], 'two.pdf')];
    fileUploadService.postFile
      .mockReturnValueOnce(throwError(() => new Error('network error')))
      .mockReturnValueOnce(of(uploadReceipt))
      .mockReturnValueOnce(of(uploadReceipt));
    const log = vi.spyOn(console, 'error').mockImplementation(() => {});
    component.addFiles(files);
    component.queue[0].archiveAsPdfA = true;
    component.uploadFileToActivity();

    expect(component.queue.map(item => item.status)).toEqual(['failed', 'uploaded']);
    expect(component.queue[0].message).toContain('avoid duplicates');
    expect(component.successMessage).toContain('1 uploaded, 1 failed');
    component.retryFile(component.queue[0]);
    expect(fileUploadService.postFile).toHaveBeenCalledTimes(3);
    expect(fileUploadService.postFile).toHaveBeenLastCalledWith(files[0], true);
    expect(component.queue.map(item => item.status)).toEqual(['uploaded', 'uploaded']);
    log.mockRestore();
  });

  it('adds dropped files to the queue and ignores drops during an upload', () => {
    const preventDefault = vi.fn();
    const file = new File(['x'], 'drop.pdf');
    const event = { preventDefault, dataTransfer: { files: [file] } } as unknown as DragEvent;
    component.onFilesDropped(event);
    component.isUploading = true;
    component.onFilesDropped(event);

    expect(preventDefault).toHaveBeenCalledTimes(2);
    expect(component.queue).toHaveLength(1);
    expect(component.queue[0].file).toBe(file);
  });

  it('reports list refresh failures rather than showing an empty list', () => {
    const log = vi.spyOn(console, 'error').mockImplementation(() => {});
    fileUploadService.getUploadedFiles.mockReturnValueOnce(throwError(() => new Error('offline')));
    component.loadUploadedFiles();
    fixture.detectChanges();
    expect(component.filesErrorMessage).toContain('Could not refresh');
    expect(fixture.nativeElement.textContent).not.toContain('No files uploaded yet.');
    log.mockRestore();
  });

  it('converts an existing uploaded document and refreshes the list', () => {
    const uploadedDocument: UploadedFileInfo = {
      storedFileName: uploadReceipt.id,
      correlationId: 'workflow-123',
      originalFileName: 'report.docx',
      size: 12,
      uploadedAt: '2026-10-01T12:00:00Z',
      processingStatus: 'Completed',
      requestedOutputFormat: 'Original',
    };

    component.convertUploadedDocument(uploadedDocument, false);

    expect(fileUploadService.convertDocument).toHaveBeenCalledWith(uploadReceipt.id, false);
    expect(fileUploadService.getUploadedFiles).toHaveBeenCalledTimes(2);
    expect(component.conversionMessage).toContain('converted to PDF');
  });

  it('labels a stored PDF/A rendition in the uploaded list', () => {
    const archivedDocument: UploadedFileInfo = {
      storedFileName: 'pdfa-123',
      correlationId: 'workflow-456',
      originalFileName: 'report.pdfa.pdf',
      size: 42,
      uploadedAt: '2026-10-01T12:00:00Z',
      processingStatus: 'Completed',
      requestedOutputFormat: 'PdfA',
    };

    expect(component.getDocumentFormat(archivedDocument)).toBe('PDF/A');
  });

  it('offers PDF/A conversion but not redundant PDF conversion for an uploaded PDF', () => {
    const uploadedPdf: UploadedFileInfo = {
      storedFileName: 'pdf-123',
      correlationId: 'workflow-789',
      originalFileName: 'report.pdf',
      size: 42,
      uploadedAt: '2026-10-01T12:00:00Z',
      processingStatus: 'Completed',
      requestedOutputFormat: 'Original',
    };

    expect(component.canConvertToPdf(uploadedPdf)).toBe(false);
    expect(component.canConvertToPdfA(uploadedPdf)).toBe(true);
  });
});
