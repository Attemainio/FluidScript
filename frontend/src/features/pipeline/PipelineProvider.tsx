import { useEffect, useMemo } from 'react';

import { createClient } from '../../api/client.ts';
import { useDraftStore } from '../../state/draftStore.ts';
import { CompilePipeline } from './compilePipeline.ts';
import { PipelineContext } from './pipelineContext.ts';

/** Makes one pipeline available to the shell; a test may pass its own. */
export function PipelineProvider({
  pipeline,
  children,
}: {
  readonly pipeline?: CompilePipeline;
  readonly children: React.ReactNode;
}): React.ReactNode {
  const own = useMemo(
    () => pipeline ?? new CompilePipeline(createClient(), useDraftStore.getState()),
    [pipeline],
  );

  useEffect(() => () => own.dispose(), [own]);

  return <PipelineContext value={own}>{children}</PipelineContext>;
}
