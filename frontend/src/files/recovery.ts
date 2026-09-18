/**
 * The recovery store (`58`): IndexedDB, one entry per document id, written while a document is
 * dirty and never into the named file. It holds the text, both hashes, the name, the read-only bit
 * that stands for the language disposition, when it was written, and a fingerprint of the file the
 * document came from, so a later Open can tell "this draft is of that file" from "same name, other
 * file". File handles live in a second store, because a handle survives a reload only there.
 */

export interface RecoveryEntry {
  readonly documentId: string;
  readonly displayName: string;
  readonly text: string;
  readonly currentHash: string;
  readonly savedHash: string | null;
  readonly readOnly: boolean;
  /** Epoch milliseconds. */
  readonly savedAt: number;
  /** The original file's name and modified time, when the document came from one. */
  readonly fingerprint: { readonly name: string; readonly modified: number | null } | null;
}

export interface RecoveryStore {
  put(entry: RecoveryEntry): Promise<void>;
  get(documentId: string): Promise<RecoveryEntry | null>;
  list(): Promise<readonly RecoveryEntry[]>;
  remove(documentId: string): Promise<void>;
  putHandle(documentId: string, handle: FileSystemFileHandle): Promise<void>;
  getHandle(documentId: string): Promise<FileSystemFileHandle | null>;
  removeHandle(documentId: string): Promise<void>;
}

const dbName = 'fluidscript';
const dbVersion = 1;

function request<T>(r: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    r.onsuccess = () => resolve(r.result);
    r.onerror = () => reject(r.error ?? new Error('IndexedDB request failed'));
  });
}

function openDb(factory: IDBFactory): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const r = factory.open(dbName, dbVersion);
    r.onupgradeneeded = () => {
      const db = r.result;
      if (!db.objectStoreNames.contains('recovery')) {
        db.createObjectStore('recovery', { keyPath: 'documentId' });
      }
      if (!db.objectStoreNames.contains('handles')) {
        db.createObjectStore('handles');
      }
    };
    r.onsuccess = () => resolve(r.result);
    r.onerror = () => reject(r.error ?? new Error('IndexedDB open failed'));
    r.onblocked = () => reject(new Error('IndexedDB open blocked'));
  });
}

/** The IndexedDB store. Every failure rejects, and the caller reports `FILE004` once and carries on. */
export class IndexedDbRecoveryStore implements RecoveryStore {
  private db: Promise<IDBDatabase> | null = null;

  private readonly factory: IDBFactory;

  constructor(factory: IDBFactory) {
    this.factory = factory;
  }

  private async store(
    name: 'recovery' | 'handles',
    mode: IDBTransactionMode,
  ): Promise<IDBObjectStore> {
    this.db ??= openDb(this.factory);
    const db = await this.db;
    return db.transaction(name, mode).objectStore(name);
  }

  async put(entry: RecoveryEntry): Promise<void> {
    await request((await this.store('recovery', 'readwrite')).put(entry));
  }

  async get(documentId: string): Promise<RecoveryEntry | null> {
    const entry = (await request((await this.store('recovery', 'readonly')).get(documentId))) as
      RecoveryEntry | undefined;
    return entry ?? null;
  }

  async list(): Promise<readonly RecoveryEntry[]> {
    return (await request((await this.store('recovery', 'readonly')).getAll())) as RecoveryEntry[];
  }

  async remove(documentId: string): Promise<void> {
    await request((await this.store('recovery', 'readwrite')).delete(documentId));
  }

  async putHandle(documentId: string, handle: FileSystemFileHandle): Promise<void> {
    await request((await this.store('handles', 'readwrite')).put(handle, documentId));
  }

  async getHandle(documentId: string): Promise<FileSystemFileHandle | null> {
    const handle = (await request((await this.store('handles', 'readonly')).get(documentId))) as
      FileSystemFileHandle | undefined;
    return handle ?? null;
  }

  async removeHandle(documentId: string): Promise<void> {
    await request((await this.store('handles', 'readwrite')).delete(documentId));
  }
}

/** A store that is not there (`FILE004`): every write fails, every read is empty. */
export class UnavailableRecoveryStore implements RecoveryStore {
  put(): Promise<void> {
    return Promise.reject(new Error('The recovery store is unavailable.'));
  }
  get(): Promise<RecoveryEntry | null> {
    return Promise.resolve(null);
  }
  list(): Promise<readonly RecoveryEntry[]> {
    return Promise.resolve([]);
  }
  remove(): Promise<void> {
    return Promise.resolve();
  }
  putHandle(): Promise<void> {
    return Promise.reject(new Error('The recovery store is unavailable.'));
  }
  getHandle(): Promise<FileSystemFileHandle | null> {
    return Promise.resolve(null);
  }
  removeHandle(): Promise<void> {
    return Promise.resolve();
  }
}

export function selectRecoveryStore(window: Window): RecoveryStore {
  try {
    return typeof window.indexedDB === 'object' && window.indexedDB !== null
      ? new IndexedDbRecoveryStore(window.indexedDB)
      : new UnavailableRecoveryStore();
  } catch {
    // Reading `indexedDB` itself throws in some private modes.
    return new UnavailableRecoveryStore();
  }
}
