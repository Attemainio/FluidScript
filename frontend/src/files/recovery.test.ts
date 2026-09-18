import 'fake-indexeddb/auto';

import { IDBFactory } from 'fake-indexeddb';
import { describe, expect, it } from 'vitest';

import { IndexedDbRecoveryStore, type RecoveryEntry } from './recovery.ts';

function entry(documentId: string, text: string): RecoveryEntry {
  return {
    documentId,
    displayName: `${documentId}.fluid`,
    text,
    currentHash: text,
    savedHash: null,
    readOnly: false,
    savedAt: 1,
    fingerprint: null,
  };
}

describe('the IndexedDB recovery store (58)', () => {
  it('keeps one entry per document and a handle beside it, and forgets each on request', async () => {
    const store = new IndexedDbRecoveryStore(new IDBFactory());
    await store.put(entry('a', 'one'));
    await store.put(entry('b', 'two'));
    await store.put(entry('a', 'one, edited'));
    expect((await store.get('a'))?.text).toBe('one, edited');
    expect((await store.list()).map((e) => e.documentId).sort()).toEqual(['a', 'b']);

    const handle = { kind: 'file', name: 'a.fluid' } as unknown as FileSystemFileHandle;
    await store.putHandle('a', handle);
    expect(await store.getHandle('a')).toEqual(handle);
    expect(await store.getHandle('b')).toBeNull();

    await store.remove('a');
    await store.removeHandle('a');
    expect(await store.get('a')).toBeNull();
    expect(await store.getHandle('a')).toBeNull();
    expect((await store.list()).map((e) => e.documentId)).toEqual(['b']);
  });
});
