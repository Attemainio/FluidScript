import { describe, expect, it } from 'vitest';

import { ContractMismatchError, contractMajor, createClient } from './client.ts';

function answering(body: unknown): typeof fetch {
  return () => Promise.resolve(new Response(JSON.stringify(body), { status: 200 }));
}

describe('the contract major check (26, 51 error cases)', () => {
  const signal = new AbortController().signal;

  it('reads a body of its own major, whatever the minor', async () => {
    const client = createClient(answering({ contractVersion: `${contractMajor}.7`, diagnostics: [] }));

    await expect(client.validate('x', signal)).resolves.toMatchObject({
      contractVersion: `${contractMajor}.7`,
    });
  });

  it('refuses a body of another major, and names the version it received', async () => {
    const client = createClient(answering({ contractVersion: '2.3', model: null }));

    const refused = await client.compile({ sessionId: 's', script: 'x' }, signal).catch((e: unknown) => e);

    expect(refused).toBeInstanceOf(ContractMismatchError);
    expect((refused as ContractMismatchError).received).toBe('2.3');
  });

  it('checks the metadata too, which is versioned like a compile', async () => {
    const client = createClient(answering({ contractVersion: `${contractMajor + 1}.0` }));

    await expect(client.metadata(signal)).rejects.toBeInstanceOf(ContractMismatchError);
  });

  it('leaves an unversioned body alone', async () => {
    const client = createClient(answering({ edits: [] }));

    await expect(client.format('x', signal)).resolves.toEqual({ edits: [] });
  });
});
