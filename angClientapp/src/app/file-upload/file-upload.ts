import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { catchError, from, mergeMap, of, tap } from 'rxjs';
import { FileUploadService, UploadedFileInfo } from '../file-upload.service';

interface FileUploadError {
  // Define the error structure
  [key: string]: unknown;
}

interface QueuedFile {
  file: File;
  archiveAsPdfA: boolean;
  status: 'queued' | 'uploading' | 'uploaded' | 'failed' | 'invalid';
  message: string;
}

@Component({
  selector: 'app-file-upload',
  imports: [CommonModule, RouterLink],
  templateUrl: './file-upload.html',
  styleUrl: './file-upload.css',
})
export class FileUpload implements OnInit {
  @ViewChild('fileInputRef') fileInputRef?: ElementRef<HTMLInputElement>;

  queue: QueuedFile[] = [];
  uploadedFiles: UploadedFileInfo[] = [];
  readonly maxFileSizeBytes = 5 * 1024 * 1024;
  private readonly officeConversionExtensions = ['doc', 'docx', 'rtf', 'odt', 'xls', 'xlsx', 'ods'];
  private readonly pdfaSupportedExtensions = [...this.officeConversionExtensions, 'pdf'];
  isUploading = false;
  isLoadingFiles = false;
  filesErrorMessage = '';
  errorMessage = '';
  successMessage = '';
  conversionInProgressId: string | null = null;
  conversionMessage = '';
  conversionErrorMessage = '';

  constructor(
    private fileUploadService: FileUploadService,
    private cdr: ChangeDetectorRef
  ) { }

  ngOnInit(): void {
    this.loadUploadedFiles();
  }

  get queuedCount(): number {
    return this.queue.filter(item => item.status === 'queued').length;
  }

  get uploadedCount(): number {
    return this.queue.filter(item => item.status === 'uploaded').length;
  }

  get failedCount(): number {
    return this.queue.filter(item => item.status === 'failed' || item.status === 'invalid').length;
  }

  canArchiveAsPdfA(file: File): boolean {
    const extension = file.name.toLowerCase().split('.').pop() ?? '';
    return this.pdfaSupportedExtensions.includes(extension);
  }

  canConvertToPdf(file: UploadedFileInfo): boolean {
    const extension = file.originalFileName.toLowerCase().split('.').pop() ?? '';
    return this.officeConversionExtensions.includes(extension);
  }

  canConvertToPdfA(file: UploadedFileInfo): boolean {
    const extension = file.originalFileName.toLowerCase().split('.').pop() ?? '';
    return this.pdfaSupportedExtensions.includes(extension);
  }

  getDocumentFormat(file: UploadedFileInfo): string {
    const fileName = file.originalFileName.toLowerCase();
    if (fileName.endsWith('.pdfa.pdf')) {
      return 'PDF/A';
    }
    if (fileName.endsWith('.pdf')) {
      return 'PDF';
    }
    return fileName.split('.').pop()?.toUpperCase() ?? 'FILE';
  }

  convertUploadedDocument(file: UploadedFileInfo, toPdfA: boolean): void {
    if (this.conversionInProgressId || !(toPdfA ? this.canConvertToPdfA(file) : this.canConvertToPdf(file))) {
      return;
    }

    this.conversionInProgressId = file.storedFileName;
    this.conversionMessage = '';
    this.conversionErrorMessage = '';
    this.fileUploadService.convertDocument(file.storedFileName, toPdfA).subscribe({
      next: (converted) => {
        this.conversionInProgressId = null;
        if (converted) {
          this.conversionMessage = toPdfA
            ? `${file.originalFileName} archived as PDF/A.`
            : `${file.originalFileName} converted to PDF.`;
          this.loadUploadedFiles();
        } else {
          this.conversionErrorMessage = 'Conversion failed. The original document remains available.';
        }
        this.cdr.detectChanges();
      },
      error: (error: FileUploadError) => {
        this.conversionInProgressId = null;
        this.conversionErrorMessage = 'Conversion failed. The original document remains available.';
        console.error('Document conversion error:', error);
        this.cdr.detectChanges();
      },
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement | null;
    if (input?.files) {
      this.addFiles(Array.from(input.files));
      input.value = '';
    }
  }

  onFilesDropped(event: DragEvent): void {
    event.preventDefault();
    if (!this.isUploading && event.dataTransfer?.files) {
      this.addFiles(Array.from(event.dataTransfer.files));
    }
  }

  addFiles(files: File[]): void {
    if (this.isUploading) {
      return;
    }
    this.successMessage = '';
    this.errorMessage = '';
    this.queue.push(...files.map(file => {
      const message = file.size > this.maxFileSizeBytes
        ? 'File is too large. Maximum allowed size is 5 MB.'
        : file.size === 0 ? 'File is empty.' : '';
      return { file, archiveAsPdfA: false, status: message ? 'invalid' as const : 'queued' as const, message };
    }));
  }

  setArchiveAsPdfA(item: QueuedFile, event: Event): void {
    item.archiveAsPdfA = (event.target as HTMLInputElement).checked;
  }

  clearSelection(): void {
    this.queue = [];
    this.errorMessage = '';
    this.successMessage = '';
    if (this.fileInputRef?.nativeElement) {
      this.fileInputRef.nativeElement.value = '';
    }
  }

  uploadFileToActivity(): void {
    this.uploadFiles(this.queue.filter(item => item.status === 'queued'));
  }

  retryFile(item: QueuedFile): void {
    if (item.status === 'failed') {
      this.uploadFiles([item]);
    }
  }

  private uploadFiles(items: QueuedFile[]): void {
    if (items.length === 0 || this.isUploading) {
      return;
    }

    this.isUploading = true;
    this.successMessage = '';
    this.errorMessage = '';

    from(items).pipe(
      mergeMap(item => {
        item.status = 'uploading';
        item.message = '';
        return this.fileUploadService.postFile(item.file, item.archiveAsPdfA).pipe(
          tap(() => {
            item.status = 'uploaded';
            item.message = 'Uploaded; processing continues in the uploaded files list.';
          }),
          catchError((error: unknown) => {
            item.status = 'failed';
            item.message = 'Upload failed or its result is unknown. Check uploaded files before retrying to avoid duplicates.';
            console.error('File upload error:', error);
            return of(null);
          })
        );
      }, 2)
    ).subscribe({
      next: () => this.cdr.detectChanges(),
      complete: () => {
        this.isUploading = false;
        this.successMessage = `Queue: ${this.uploadedCount} uploaded, ${this.failedCount} failed or invalid, ${this.queuedCount} waiting.`;
        this.loadUploadedFiles();
        this.cdr.detectChanges();
      },
    });
  }

  loadUploadedFiles(): void {
    this.isLoadingFiles = true;
    this.filesErrorMessage = '';
    this.fileUploadService.getUploadedFiles().subscribe({
      next: (files) => {
        this.uploadedFiles = files;
        this.isLoadingFiles = false;
        this.cdr.detectChanges();
      },
      error: (error: unknown) => {
        this.isLoadingFiles = false;
        this.filesErrorMessage = 'Could not refresh uploaded files. Try again before retrying an uncertain upload.';
        console.error('Failed to load uploaded files:', error);
        this.cdr.detectChanges();
      },
    });
  }

  formatSize(sizeInBytes: number): string {
    if (sizeInBytes < 1024) {
      return `${sizeInBytes} B`;
    }

    if (sizeInBytes < 1024 * 1024) {
      return `${(sizeInBytes / 1024).toFixed(1)} KB`;
    }

    return `${(sizeInBytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  // Drives which inline preview markup the template renders for a given file.
  getFileKind(file: UploadedFileInfo): 'pdf' | 'image' | 'video' | 'audio' | 'other' {
    const extension = file.originalFileName.toLowerCase().split('.').pop() ?? '';

    if (extension === 'pdf') {
      return 'pdf';
    }
    if (['png', 'jpg', 'jpeg', 'gif', 'bmp'].includes(extension)) {
      return 'image';
    }
    if (['mp4', 'mkv'].includes(extension)) {
      return 'video';
    }
    if (extension === 'mp3') {
      return 'audio';
    }
    return 'other';
  }

  openFile(file: UploadedFileInfo, event?: Event): void {
    event?.preventDefault();
    const preview = this.getFileKind(file) !== 'other';
    const tab = preview ? window.open('', '_blank') : null;
    this.fileUploadService.fetchFile(file.storedFileName).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        if (preview && tab) {
          tab.location.href = url;
        } else {
          const a = document.createElement('a');
          a.href = url;
          a.download = file.originalFileName;
          a.click();
        }
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      },
      error: (error) => {
        tab?.close();
        console.error('Failed to fetch file:', error);
      }
    });
  }

  getFileUrl(file: UploadedFileInfo): string {
    return this.fileUploadService.getFileUrl(file.storedFileName);
  }
}
