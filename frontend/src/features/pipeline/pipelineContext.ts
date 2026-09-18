import { createContext, useContext } from 'react';

import type { CompilePipeline } from './compilePipeline.ts';

/** The pipeline the shell shares; `PipelineProvider` fills it. */
export const PipelineContext = createContext<CompilePipeline | null>(null);

/** The pipeline in scope. */
export function usePipeline(): CompilePipeline {
  const pipeline = useContext(PipelineContext);
  if (pipeline === null) {
    throw new Error('usePipeline outside a PipelineProvider');
  }
  return pipeline;
}
