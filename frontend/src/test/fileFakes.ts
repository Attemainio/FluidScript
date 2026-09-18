import type { EditorState } from '@codemirror/state';

import type { ApiClient } from '../api/client.ts';
import type { Diagnostic } from '../api/types.ts';
import type { FileBackend, FileSnapshot, OpenedFile } from '../files/backend.ts';
import type { TextAccess } from '../files/fileActions.ts';
import type { RecoveryEntry, RecoveryStore } from '../files/recovery.ts';

/** A file on the fake disk; the handle a document holds is this object. */
export class FakeHandle {
  readonly kind = 'file' as const;
  name: string;
  text: string;
  modified: number;
  constructor(name: string, text: string, modified: number) {
    this.name = name;
    this.text = text;
    this.modified = modified;
  }
  isSameEntry(other: FileSystemHandle): Promise<boolean> {
    return Promise.resolve(other === (this as unknown as FileSystemHandle));
  }
}

/**
 * A backend over an in-memory disk. A test scripts what the pickers return, breaks a write on
 * request, and reads the downloads back; `canOverwrite` flips it between the native and the
 * fallback path so the same scenarios run against both (`58`'s first criterion).
 */
export class FakeBackend implements FileBackend {
  readonly disk = new Map<string, FakeHandle>();
  /** The names the next Open picks; empty is a cancel. */
  pick: string[] = [];
  /** The name the next Save As takes; null is a cancel. */
  saveAsName: string | null = null;
  permitted = true;
  failWrite: Error | null = null;
  readonly downloads: { name: string; text: string }[] = [];
  now = 1000;
  readonly canOverwrite: boolean;

  constructor(canOverwrite: boolean) {
    this.canOverwrite = canOverwrite;
  }

  put(name: string, text: string): FakeHandle {
    const handle = new FakeHandle(name, text, this.now++);
    this.disk.set(name, handle);
    return handle;
  }

  open(): Promise<readonly OpenedFile[]> {
    const files = this.pick.map((name) => {
      const file = this.disk.get(name)!;
      return {
        name,
        text: file.text,
        handle: this.canOverwrite ? (file as unknown as FileSystemFileHandle) : null,
        modified: file.modified,
      };
    });
    this.pick = [];
    return Promise.resolve(files);
  }

  async saveAs(suggestedName: string, text: string): Promise<OpenedFile | null> {
    void suggestedName;
    if (!this.canOverwrite) {
      this.download(suggestedName, text);
      return null;
    }
    if (this.saveAsName === null) {
      return null;
    }
    const handle = this.put(this.saveAsName, '');
    await this.write(handle as unknown as FileSystemFileHandle, text);
    return {
      name: handle.name,
      text,
      handle: handle as unknown as FileSystemFileHandle,
      modified: handle.modified,
    };
  }

  write(handle: FileSystemFileHandle, text: string): Promise<void> {
    if (this.failWrite !== null) {
      return Promise.reject(this.failWrite);
    }
    const file = handle as unknown as FakeHandle;
    file.text = text;
    file.modified = this.now++;
    return Promise.resolve();
  }

  read(handle: FileSystemFileHandle): Promise<FileSnapshot> {
    const file = handle as unknown as FakeHandle;
    return Promise.resolve({ text: file.text, modified: file.modified });
  }

  permit(): Promise<boolean> {
    return Promise.resolve(this.permitted);
  }

  download(name: string, text: string): void {
    this.downloads.push({ name, text });
  }
}

/** A recovery store in memory, which a test can break to see `FILE004`. */
export class MemoryRecoveryStore implements RecoveryStore {
  readonly entries = new Map<string, RecoveryEntry>();
  readonly handles = new Map<string, FileSystemFileHandle>();
  broken = false;

  private check(): Promise<void> {
    return this.broken ? Promise.reject(new Error('quota')) : Promise.resolve();
  }
  async put(entry: RecoveryEntry): Promise<void> {
    await this.check();
    this.entries.set(entry.documentId, entry);
  }
  async get(documentId: string): Promise<RecoveryEntry | null> {
    await this.check();
    return this.entries.get(documentId) ?? null;
  }
  async list(): Promise<readonly RecoveryEntry[]> {
    await this.check();
    return [...this.entries.values()];
  }
  async remove(documentId: string): Promise<void> {
    await this.check();
    this.entries.delete(documentId);
  }
  async putHandle(documentId: string, handle: FileSystemFileHandle): Promise<void> {
    await this.check();
    this.handles.set(documentId, handle);
  }
  async getHandle(documentId: string): Promise<FileSystemFileHandle | null> {
    await this.check();
    return this.handles.get(documentId) ?? null;
  }
  async removeHandle(documentId: string): Promise<void> {
    await this.check();
    this.handles.delete(documentId);
  }
}

/** The editor's text as a map, for the actions' tests, which read state and never a view. */
export class FakeText implements TextAccess {
  readonly texts = new Map<string, string>();
  readonly readOnly = new Map<string, boolean>();
  readonly diagnostics = new Map<string, readonly Diagnostic[]>();
  stale = false;
  readonly forgotten: string[] = [];

  textOf(documentId: string): string {
    return this.texts.get(documentId) ?? '';
  }
  loadText(documentId: string, text: string, readOnly: boolean): EditorState {
    this.texts.set(documentId, text);
    this.readOnly.set(documentId, readOnly);
    return null as unknown as EditorState;
  }
  diagnosticsOf(documentId: string): { diagnostics: readonly Diagnostic[]; current: boolean } {
    return { diagnostics: this.diagnostics.get(documentId) ?? [], current: !this.stale };
  }
  applySuggestion(documentId: string, diagnostic: Diagnostic): void {
    const s = diagnostic.suggestion!;
    const text = this.textOf(documentId);
    this.texts.set(
      documentId,
      text.slice(0, s.range.offset) + s.newText + text.slice(s.range.offset + s.range.length),
    );
  }
  forget(documentId: string): void {
    this.forgotten.push(documentId);
    this.texts.delete(documentId);
  }
}

/** A client whose validate answers with the diagnostics a test scripts per text. */
export function validateClient(answers: (script: string) => readonly Diagnostic[]): ApiClient {
  return {
    compile: () => Promise.reject(new Error('not used')),
    solve: () => Promise.reject(new Error('not used')),
    validate: (script: string) =>
      Promise.resolve({
        contractVersion: '1.0',
        languageMajor: 1,
        diagnostics: answers(script),
        timings: { parseMs: 0, bindMs: 0, sizeMs: 0, solveMs: 0, totalMs: 0 },
      }),
    format: () => Promise.reject(new Error('not used')),
    metadata: () => Promise.reject(new Error('not used')),
  } as unknown as ApiClient;
}

/** `FS1701` as the wire carries it, with the suggestion Core attaches. */
export function unversioned(): Diagnostic {
  return {
    code: 'FS1701',
    severity: 'info',
    message:
      "This draft states no language version. Add 'fluidscript 1' as its first line to save it.",
    range: {
      start: { line: 0, character: 0 },
      end: { line: 0, character: 0 },
      offset: 0,
      length: 0,
    },
    component: null,
    suggestion: {
      title: "Add 'fluidscript 1'",
      range: {
        start: { line: 0, character: 0 },
        end: { line: 0, character: 0 },
        offset: 0,
        length: 0,
      },
      newText: 'fluidscript 1\n',
    },
    related: [],
  };
}

export function unsupportedMajor(): Diagnostic {
  return {
    code: 'FS1702',
    severity: 'error',
    message: 'This file is FluidScript 7, which this version cannot read. It understands 1.',
    range: {
      start: { line: 0, character: 0 },
      end: { line: 0, character: 13 },
      offset: 0,
      length: 13,
    },
    component: null,
    suggestion: null,
    related: [],
  };
}
