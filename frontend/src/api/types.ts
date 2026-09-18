/**
 * The wire types (`42`), generated from the Api's committed JSON schemas (`D-46`). This module is
 * the import site; the generated file is not referenced directly, so a regeneration that renames
 * something breaks here and nowhere else.
 */
export type {
  Binding,
  Circuit,
  CompileResponse,
  Component,
  ComponentState,
  Connection,
  Diagnostic,
  DiagnosticCode,
  Dimension,
  Kind,
  Layout,
  IndexedParameter,
  Metadata,
  ModelContract,
  ParameterMeta,
  PortFamily,
  Primitive,
  ResolvedStyle,
  PropertyMeta,
  Placement,
  Position,
  Quantity,
  Range,
  Related,
  Route,
  Solve,
  Suggestion,
  Symbol as SymbolDefinition,
  Timings,
} from './types.generated.ts';

/** The body of `POST /api/v1/validate`, diagnostics only (`42`); not part of the committed schemas yet. */
export interface ValidateResponse {
  readonly contractVersion: string;
  readonly languageMajor: number | null;
  readonly diagnostics: readonly import('./types.generated.ts').Diagnostic[];
  readonly timings: import('./types.generated.ts').Timings;
}

/** One text replacement (`17`): the span in UTF-16 code units of the script sent, and its replacement. */
export interface TextEdit {
  readonly span: { readonly start: number; readonly length: number };
  readonly newText: string;
}

/** The body of `POST /api/v1/format` (`42`): the edits that bring the script to the canonical layout. */
export interface FormatResponse {
  readonly edits: readonly TextEdit[];
}

/** RFC 9457 problem details, the body of every non-200 (`42`). */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly field?: string;
  readonly code?: string;
  readonly correlationId?: string;
  readonly bytes?: number;
  readonly limit?: number;
}
