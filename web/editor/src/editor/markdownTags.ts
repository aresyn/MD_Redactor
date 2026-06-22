import type { EditAnnotation, EditDiagnostic, ParsedTaggedMarkdown } from './types';

type TagKind = 'start' | 'comm' | 'end' | 'unknown';

type TagToken = {
  kind: TagKind;
  id?: number;
  start: number;
  end: number;
  header: string;
  comment: string;
  commentStart: number;
  commentEnd: number;
};

type ActiveEdit = {
  id: number;
  rawStart: number;
  fragmentRawStart: number;
  fragmentRawEnd: number;
  fragmentCleanStart: number;
  fragmentCleanEnd: number;
  comment: string;
  hasComment: boolean;
  hasError: boolean;
};

const markerPrefix = '<!-- ed-';
const idPattern = /\bid\s*=\s*"(?<id>\d+)"/;
const statusPattern = /\bstatus\s*=/i;

export function parseTaggedMarkdown(markdown: string): ParsedTaggedMarkdown {
  const diagnostics: EditDiagnostic[] = [];
  const annotations: EditAnnotation[] = [];
  const cleanParts: string[] = [];
  const usedIds = new Set<number>();
  let current: ActiveEdit | undefined;
  let position = 0;

  for (const token of readTokens(markdown)) {
    cleanParts.push(markdown.slice(position, token.start));

    if (token.kind === 'unknown') {
      cleanParts.push(markdown.slice(token.start, token.end));
      diagnostics.push(error('Неизвестный служебный маркер правки.', token.start, token.id));
      position = token.end;
      continue;
    }

    collectTokenDiagnostics(token, diagnostics);

    if (token.kind === 'start') {
      if (current) {
        diagnostics.push(error('Вложенные правки запрещены: найден ed-start внутри незакрытой правки.', token.start, token.id));
        current.hasError = true;
      }

      const idError = token.id === undefined || token.id <= 0;
      let duplicateError = false;
      if (token.id !== undefined && token.id > 0 && usedIds.has(token.id)) {
        diagnostics.push(error(`Дублирующийся id правки ${token.id} запрещен.`, token.start, token.id));
        duplicateError = true;
      }

      if (token.id !== undefined && token.id > 0) {
        usedIds.add(token.id);
      }

      current = {
        id: token.id ?? 0,
        rawStart: token.start,
        fragmentRawStart: token.end,
        fragmentRawEnd: token.end,
        fragmentCleanStart: cleanParts.join('').length,
        fragmentCleanEnd: cleanParts.join('').length,
        comment: '',
        hasComment: false,
        hasError: idError || duplicateError || current?.hasError === true,
      };
    }

    if (token.kind === 'comm') {
      if (!current) {
        diagnostics.push(error('Маркер ed-comm найден без открывающего ed-start.', token.start, token.id));
        position = token.end;
        continue;
      }

      if (token.id !== current.id) {
        diagnostics.push(error(`Id в ed-comm (${formatId(token.id)}) не совпадает с id ed-start (${current.id}).`, token.start, token.id));
        current.hasError = true;
      }

      current.fragmentRawEnd = token.start;
      current.fragmentCleanEnd = cleanParts.join('').length;
      current.comment = trimSingleTrailingLineBreak(token.comment);
      current.hasComment = true;

      if (current.comment.includes('--')) {
        diagnostics.push(error('Комментарий правки содержит запрещенную для HTML-comment последовательность "--".', token.commentStart, current.id));
        current.hasError = true;
      }
    }

    if (token.kind === 'end') {
      if (!current) {
        diagnostics.push(error('Маркер ed-end найден без открывающего ed-start.', token.start, token.id));
        position = token.end;
        continue;
      }

      if (!current.hasComment) {
        diagnostics.push(error(`Правка id ${current.id} закрыта без обязательного ed-comm.`, token.start, current.id));
        current.hasError = true;
      }

      if (token.id !== current.id) {
        diagnostics.push(error(`Id в ed-end (${formatId(token.id)}) не совпадает с id ed-start (${current.id}).`, token.start, token.id));
        current.hasError = true;
      }

      if (current.hasComment) {
        const fragmentMarkdown = markdown.slice(current.fragmentRawStart, current.fragmentRawEnd);
        const annotation: EditAnnotation = {
          id: current.id,
          fragmentMarkdown,
          fragmentText: plainTextFromMarkdown(fragmentMarkdown),
          comment: current.comment,
          kind: isInlineFragment(fragmentMarkdown) ? 'inline' : 'block',
          rawTaggedMarkdown: markdown.slice(current.rawStart, token.end),
        };

        annotations.push(annotation);
      }

      current = undefined;
    }

    position = token.end;
  }

  cleanParts.push(markdown.slice(position));

  if (current) {
    if (!current.hasComment) {
      diagnostics.push(error(`Правка id ${current.id} не содержит обязательный ed-comm.`, current.rawStart, current.id));
    }

    diagnostics.push(error(`Правка id ${current.id} не содержит закрывающий ed-end.`, current.rawStart, current.id));
  }

  return {
    cleanMarkdown: cleanParts.join(''),
    annotations,
    diagnostics,
  };
}

export function plainTextFromMarkdown(markdown: string): string {
  return markdown
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/!\[[^\]]*]\([^)]*\)/g, '')
    .replace(/\[([^\]]+)]\([^)]*\)/g, '$1')
    .replace(/[`*_>#()[\]-]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

function* readTokens(markdown: string): Generator<TagToken> {
  let searchIndex = 0;

  while (searchIndex < markdown.length) {
    const start = markdown.indexOf(markerPrefix, searchIndex);
    if (start < 0) {
      return;
    }

    const close = markdown.indexOf('-->', start + markerPrefix.length);
    if (close < 0) {
      yield {
        kind: 'unknown',
        start,
        end: markdown.length,
        header: markdown.slice(start),
        comment: '',
        commentStart: markdown.length,
        commentEnd: markdown.length,
      };
      return;
    }

    const contentStart = start + '<!--'.length;
    const { headerEnd, payloadStart } = findHeaderEnd(markdown, contentStart, close);
    const header = markdown.slice(contentStart, headerEnd).trim();
    const kind = readKind(header);
    const id = readId(header);
    const comment = kind === 'comm' ? markdown.slice(payloadStart, close) : '';

    yield {
      kind,
      id,
      start,
      end: close + '-->'.length,
      header,
      comment,
      commentStart: payloadStart,
      commentEnd: close,
    };

    searchIndex = close + '-->'.length;
  }
}

function findHeaderEnd(markdown: string, contentStart: number, close: number): { headerEnd: number; payloadStart: number } {
  for (let index = contentStart; index < close; index += 1) {
    if (markdown[index] === '\r') {
      const payloadStart = markdown[index + 1] === '\n' ? index + 2 : index + 1;
      return { headerEnd: index, payloadStart };
    }

    if (markdown[index] === '\n') {
      return { headerEnd: index, payloadStart: index + 1 };
    }
  }

  return { headerEnd: close, payloadStart: close };
}

function readKind(header: string): TagKind {
  if (header.startsWith('ed-start')) {
    return 'start';
  }

  if (header.startsWith('ed-comm')) {
    return 'comm';
  }

  if (header.startsWith('ed-end')) {
    return 'end';
  }

  return 'unknown';
}

function readId(header: string): number | undefined {
  const match = idPattern.exec(header);
  if (!match?.groups?.id) {
    return undefined;
  }

  const id = Number.parseInt(match.groups.id, 10);
  return Number.isFinite(id) ? id : undefined;
}

function collectTokenDiagnostics(token: TagToken, diagnostics: EditDiagnostic[]): void {
  if (statusPattern.test(token.header)) {
    diagnostics.push(error('Формат правок не поддерживает атрибут status.', token.start, token.id));
  }

  if (token.id === undefined) {
    diagnostics.push(error('Маркер правки должен содержать id в формате id="N".', token.start));
  } else if (token.id <= 0) {
    diagnostics.push(error('Id правки должен быть положительным целым числом.', token.start, token.id));
  }
}

function error(message: string, index: number, editId?: number): EditDiagnostic {
  return { severity: 'error', message, index, editId };
}

function trimSingleTrailingLineBreak(value: string): string {
  if (value.endsWith('\r\n')) {
    return value.slice(0, -2);
  }

  if (value.endsWith('\n') || value.endsWith('\r')) {
    return value.slice(0, -1);
  }

  return value;
}

function isInlineFragment(fragmentMarkdown: string): boolean {
  return !fragmentMarkdown.includes('\n') && !fragmentMarkdown.includes('\r');
}

function formatId(id: number | undefined): string {
  return id === undefined ? 'не указан' : String(id);
}
