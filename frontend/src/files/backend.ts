/**
 * The two ways a browser reaches the user's files (`58`): the File System Access API, which can
 * hold a handle and overwrite in place, and the fallback of an upload input and a download link,
 * which cannot. Capability detection selects one; nothing here names a browser. Every use case in
 * `fileActions.ts` is written against this interface, so both paths pass the same scenarios.
 */

/** A file the user picked: its name, its text, and the handle to write it back through when there is one. */
export interface OpenedFile {
  readonly name: string;
  readonly text: string;
  readonly handle: FileSystemFileHandle | null;
  /** The file's last-modified time in epoch milliseconds, or null when the backend cannot say. */
  readonly modified: number | null;
}

/** What is on disk under a handle right now, read for the conflict check before an overwrite. */
export interface FileSnapshot {
  readonly text: string;
  readonly modified: number | null;
}

export interface FileBackend {
  /** True where Save can overwrite the file it came from; false where "Save" is really a download. */
  readonly canOverwrite: boolean;
  /** Lets the user pick one or more files; an empty list is a cancel (`FILE006`). */
  open(): Promise<readonly OpenedFile[]>;
  /** Lets the user name a new file and writes `text` to it; null is a cancel. Never touches an old handle. */
  saveAs(suggestedName: string, text: string): Promise<OpenedFile | null>;
  /** Overwrites the file behind `handle` through a temporary writable, closed before it counts. */
  write(handle: FileSystemFileHandle, text: string): Promise<void>;
  /** Reads what is behind `handle`, for the conflict check and for reopening after a reload. */
  read(handle: FileSystemFileHandle): Promise<FileSnapshot>;
  /** Asks the user for write permission on a persisted handle; false when refused (`FILE001`). */
  permit(handle: FileSystemFileHandle): Promise<boolean>;
  /** Offers `text` as a download named `name`; never described as saving (`58` invariant 2). */
  download(name: string, text: string): void;
}

const types = [{ description: 'FluidScript', accept: { 'text/plain': ['.fluid'] } }];

type PickerWindow = Window & {
  showOpenFilePicker?: (options: {
    multiple?: boolean;
    types?: typeof types;
  }) => Promise<FileSystemFileHandle[]>;
  showSaveFilePicker?: (options: {
    suggestedName?: string;
    types?: typeof types;
  }) => Promise<FileSystemFileHandle>;
};

type PermissionHandle = FileSystemFileHandle & {
  queryPermission?: (d: { mode: 'readwrite' }) => Promise<PermissionState>;
  requestPermission?: (d: { mode: 'readwrite' }) => Promise<PermissionState>;
};

function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

/** The File System Access path: handles, in-place writes, and permission that outlives a reload. */
export class NativeBackend implements FileBackend {
  readonly canOverwrite = true;

  private readonly window: PickerWindow;

  constructor(window: PickerWindow) {
    this.window = window;
  }

  async open(): Promise<readonly OpenedFile[]> {
    try {
      const handles = await this.window.showOpenFilePicker!({ multiple: true, types });
      const files: OpenedFile[] = [];
      for (const handle of handles) {
        const file = await handle.getFile();
        files.push({
          name: file.name,
          text: await file.text(),
          handle,
          modified: file.lastModified,
        });
      }
      return files;
    } catch (error) {
      if (isAbort(error)) {
        return [];
      }
      throw error;
    }
  }

  async saveAs(suggestedName: string, text: string): Promise<OpenedFile | null> {
    let handle: FileSystemFileHandle;
    try {
      handle = await this.window.showSaveFilePicker!({ suggestedName, types });
    } catch (error) {
      if (isAbort(error)) {
        return null;
      }
      throw error;
    }
    await this.write(handle, text);
    const file = await handle.getFile();
    return { name: file.name, text, handle, modified: file.lastModified };
  }

  async write(handle: FileSystemFileHandle, text: string): Promise<void> {
    // The writable is a temporary the browser swaps in on close; a failure before that leaves the file as it was.
    const writable = await handle.createWritable();
    try {
      await writable.write(text);
    } catch (error) {
      await writable.abort();
      throw error;
    }
    await writable.close();
  }

  async read(handle: FileSystemFileHandle): Promise<FileSnapshot> {
    const file = await handle.getFile();
    return { text: await file.text(), modified: file.lastModified };
  }

  async permit(handle: FileSystemFileHandle): Promise<boolean> {
    const h = handle as PermissionHandle;
    if (h.queryPermission === undefined || h.requestPermission === undefined) {
      return true;
    }
    if ((await h.queryPermission({ mode: 'readwrite' })) === 'granted') {
      return true;
    }
    return (await h.requestPermission({ mode: 'readwrite' })) === 'granted';
  }

  download(name: string, text: string): void {
    downloadThrough(this.window.document, name, text);
  }
}

/** The upload/download path for browsers without the File System Access API. */
export class FallbackBackend implements FileBackend {
  readonly canOverwrite = false;

  private readonly document: Document;

  constructor(document: Document) {
    this.document = document;
  }

  open(): Promise<readonly OpenedFile[]> {
    return new Promise((resolve) => {
      const input = this.document.createElement('input');
      input.type = 'file';
      input.accept = '.fluid,text/plain';
      input.multiple = true;
      input.style.display = 'none';
      const finish = async (): Promise<void> => {
        const list = input.files === null ? [] : Array.from(input.files);
        const files: OpenedFile[] = [];
        for (const file of list) {
          files.push({
            name: file.name,
            text: await file.text(),
            handle: null,
            modified: file.lastModified,
          });
        }
        input.remove();
        resolve(files);
      };
      input.addEventListener('change', () => void finish());
      input.addEventListener('cancel', () => {
        input.remove();
        resolve([]);
      });
      this.document.body.append(input);
      input.click();
    });
  }

  saveAs(suggestedName: string, text: string): Promise<OpenedFile | null> {
    this.download(suggestedName, text);
    return Promise.resolve(null);
  }

  write(): Promise<void> {
    return Promise.reject(new Error('This browser cannot overwrite a file in place.'));
  }

  read(): Promise<FileSnapshot> {
    return Promise.reject(new Error('This browser cannot read a file back.'));
  }

  permit(): Promise<boolean> {
    return Promise.resolve(false);
  }

  download(name: string, text: string): void {
    downloadThrough(this.document, name, text);
  }
}

function downloadThrough(document: Document, name: string, text: string): void {
  const url = URL.createObjectURL(new Blob([text], { type: 'text/plain' }));
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = name.endsWith('.fluid') ? name : `${name}.fluid`;
  anchor.style.display = 'none';
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** The backend this browser supports, by capability, never by name. */
export function selectBackend(window: Window): FileBackend {
  const picker = window as PickerWindow;
  return typeof picker.showOpenFilePicker === 'function' &&
    typeof picker.showSaveFilePicker === 'function'
    ? new NativeBackend(picker)
    : new FallbackBackend(window.document);
}
