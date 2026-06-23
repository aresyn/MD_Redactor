import type { EditDiagnostic } from './editor/types';

export type UiLanguage = 'ru' | 'en';

type Params = Record<string, string | number | undefined>;

let currentLanguage: UiLanguage = 'ru';

const messages: Record<UiLanguage, Record<string, string>> = {
  ru: {
    'app.rootMissing': 'Корневой элемент редактора не найден.',
    'app.editorAria': 'Markdown-редактор',
    'app.reviewAria': 'Правки',
    'app.fileNotOpened': 'Файл не открыт',
    'app.untitled': 'Без имени',
    'app.encoding': 'Кодировка: {encoding}',
    'app.elementMissing': 'Элемент редактора не найден: {selector}',
    'app.unknownHostMessage': 'Получено неизвестное сообщение приложения.',

    'review.title': 'Правки',
    'review.emptyTitle': 'В этом файле пока нет правок',
    'review.emptyHint': 'Выделите фрагмент текста и нажмите Enter',
    'review.kindInline': 'в строке',
    'review.kindBlock': 'блок',
    'review.commentPlaceholder': 'Что нужно исправить?',
    'review.commentAria': 'Комментарий к правке #{id}',
    'review.emptyComment': 'Комментарий не заполнен',
    'review.goTo': 'Перейти',
    'review.delete': 'Удалить',
    'review.count.one': '{count} правка',
    'review.count.few': '{count} правки',
    'review.count.many': '{count} правок',

    'editor.aria': 'Редактор Markdown',
    'editor.renderError': 'Не удалось отобразить Markdown: {message}',
    'editor.deleteConfirm': 'Удалить правку #{id}? Сам текст фрагмента останется в документе.',
    'editor.deleteDone': 'Правка #{id} удалена. Текст фрагмента остался в документе.',
    'editor.unsafeComment': 'Последовательность -- в комментарии заменена на - -, чтобы сохранить корректный HTML-comment.',
    'editor.fragmentNotFound': 'Фрагмент не найден',
    'editor.annotationBadgeTitle': 'Правка #{id}',
    'editor.annotationUnmapped': 'Не удалось надежно сопоставить правку #{id} с текстом документа. При сохранении исходная разметка правки будет сохранена отдельно.',

    'selection.intersectsExisting': 'Выделение пересекается с уже существующей правкой',
    'selection.emptyText': 'Выделение не содержит текста для правки',
    'selection.partialMultiblock': 'Выделение через несколько абзацев должно охватывать абзацы целиком',

    'edit.unknown_marker': 'Неизвестный служебный маркер правки.',
    'edit.nested': 'Вложенные правки запрещены: найден ed-start внутри незакрытой правки.',
    'edit.duplicate_id': 'Дублирующийся id правки {id} запрещен.',
    'edit.comment_without_start': 'Маркер ed-comm найден без открывающего ed-start.',
    'edit.comment_id_mismatch': 'Id в ed-comm ({commentId}) не совпадает с id ed-start ({startId}).',
    'edit.duplicate_comment': 'Для правки id {id} найден повторный ed-comm.',
    'edit.unsafe_comment': 'Комментарий правки содержит запрещенную для HTML-comment последовательность "--".',
    'edit.end_without_start': 'Маркер ed-end найден без открывающего ed-start.',
    'edit.missing_comment_before_end': 'Правка id {id} закрыта без обязательного ed-comm.',
    'edit.end_id_mismatch': 'Id в ed-end ({endId}) не совпадает с id ed-start ({startId}).',
    'edit.missing_comment': 'Правка id {id} не содержит обязательный ed-comm.',
    'edit.missing_end': 'Правка id {id} не содержит закрывающий ed-end.',
    'edit.status_attribute': 'Формат правок не поддерживает атрибут status.',
    'edit.missing_id': 'Маркер правки должен содержать id в формате id="N".',
    'edit.invalid_id': 'Id правки должен быть положительным целым числом.',
    'edit.idMissing': 'не указан',
  },
  en: {
    'app.rootMissing': 'Editor root element was not found.',
    'app.editorAria': 'Markdown editor',
    'app.reviewAria': 'Edits',
    'app.fileNotOpened': 'No file open',
    'app.untitled': 'Untitled',
    'app.encoding': 'Encoding: {encoding}',
    'app.elementMissing': 'Editor element was not found: {selector}',
    'app.unknownHostMessage': 'Received an unknown host message.',

    'review.title': 'Edits',
    'review.emptyTitle': 'This file has no edits yet',
    'review.emptyHint': 'Select text and press Enter',
    'review.kindInline': 'inline',
    'review.kindBlock': 'block',
    'review.commentPlaceholder': 'What should be fixed?',
    'review.commentAria': 'Comment for edit #{id}',
    'review.emptyComment': 'Comment is empty',
    'review.goTo': 'Go to',
    'review.delete': 'Delete',
    'review.count.one': '{count} edit',
    'review.count.few': '{count} edits',
    'review.count.many': '{count} edits',

    'editor.aria': 'Markdown editor',
    'editor.renderError': 'Could not render Markdown: {message}',
    'editor.deleteConfirm': 'Delete edit #{id}? The selected text will stay in the document.',
    'editor.deleteDone': 'Edit #{id} was deleted. The selected text stayed in the document.',
    'editor.unsafeComment': 'The -- sequence in the comment was replaced with - - to keep the HTML comment valid.',
    'editor.fragmentNotFound': 'Fragment not found',
    'editor.annotationBadgeTitle': 'Edit #{id}',
    'editor.annotationUnmapped': 'Edit #{id} could not be matched reliably to the document text. The original tagged edit will be preserved separately when saving.',

    'selection.intersectsExisting': 'The selection overlaps an existing edit',
    'selection.emptyText': 'The selection does not contain text for an edit',
    'selection.partialMultiblock': 'A selection across several paragraphs must include whole paragraphs',

    'edit.unknown_marker': 'Unknown edit marker.',
    'edit.nested': 'Nested edits are not allowed: ed-start was found inside an open edit.',
    'edit.duplicate_id': 'Duplicate edit id {id} is not allowed.',
    'edit.comment_without_start': 'ed-comm was found without an opening ed-start.',
    'edit.comment_id_mismatch': 'The id in ed-comm ({commentId}) does not match ed-start ({startId}).',
    'edit.duplicate_comment': 'Edit id {id} contains a duplicate ed-comm.',
    'edit.unsafe_comment': 'The edit comment contains the "--" sequence, which is not allowed inside an HTML comment.',
    'edit.end_without_start': 'ed-end was found without an opening ed-start.',
    'edit.missing_comment_before_end': 'Edit id {id} was closed without the required ed-comm.',
    'edit.end_id_mismatch': 'The id in ed-end ({endId}) does not match ed-start ({startId}).',
    'edit.missing_comment': 'Edit id {id} does not contain the required ed-comm.',
    'edit.missing_end': 'Edit id {id} does not contain a closing ed-end.',
    'edit.status_attribute': 'The edit format does not support the status attribute.',
    'edit.missing_id': 'The edit marker must contain id="N".',
    'edit.invalid_id': 'The edit id must be a positive integer.',
    'edit.idMissing': 'not specified',
  },
};

export function setLanguage(language: string | undefined): UiLanguage {
  currentLanguage = language === 'en' ? 'en' : 'ru';
  document.documentElement.lang = currentLanguage;
  return currentLanguage;
}

export function getLanguage(): UiLanguage {
  return currentLanguage;
}

export function t(key: string, params: Params = {}): string {
  const template = messages[currentLanguage][key] ?? messages.ru[key] ?? key;
  return template.replace(/\{(?<name>[a-zA-Z0-9_]+)\}/g, (_, name: string) => String(params[name] ?? ''));
}

export function formatEditCount(count: number): string {
  if (currentLanguage === 'en') {
    return t(count === 1 ? 'review.count.one' : 'review.count.many', { count });
  }

  const mod10 = count % 10;
  const mod100 = count % 100;
  if (mod10 === 1 && mod100 !== 11) {
    return t('review.count.one', { count });
  }

  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
    return t('review.count.few', { count });
  }

  return t('review.count.many', { count });
}

export function localizeDiagnostic(diagnostic: EditDiagnostic): string {
  return diagnostic.code ? t(diagnostic.code, diagnostic.params) : diagnostic.message;
}
