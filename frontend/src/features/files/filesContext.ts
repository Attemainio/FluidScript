import { createContext, useContext } from 'react';

import type { FileActions } from '../../files/fileActions.ts';

/** The file actions the shell shares; `FilesProvider` fills it. */
export const FilesContext = createContext<FileActions | null>(null);

/** The file actions in scope. */
export function useFiles(): FileActions {
  const files = useContext(FilesContext);
  if (files === null) {
    throw new Error('useFiles outside a FilesProvider');
  }
  return files;
}
