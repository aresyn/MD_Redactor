export type EditAnnotationKind = 'inline' | 'block';

export type EditAnnotation = {
  id: number;
  fragmentText: string;
  fragmentMarkdown: string;
  comment: string;
  kind: EditAnnotationKind;
  from?: number;
  to?: number;
  rawTaggedMarkdown?: string;
  warning?: string;
};

export type EditDiagnostic = {
  severity: 'error' | 'warning';
  message: string;
  index: number;
  editId?: number;
};

export type ParsedTaggedMarkdown = {
  cleanMarkdown: string;
  annotations: EditAnnotation[];
  diagnostics: EditDiagnostic[];
};

export type LoadedDocument = {
  filePath: string;
  fileName: string;
  markdown: string;
  encodingName: string;
};
