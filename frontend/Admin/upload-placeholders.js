import { StateEffect, StateField } from '@codemirror/state';
import { Decoration, EditorView, WidgetType } from '@codemirror/view';

class Placeholder extends WidgetType {
  constructor(id, label) {
    super();
    this.id = id;
    this.label = label;
  }

  eq(other) {
    return other.id === this.id && other.label === this.label;
  }

  toDOM() {
    const element = document.createElement('span');
    element.className = 'cm-upload';
    element.dataset.upload = String(this.id);
    element.textContent = `Uploading ${this.label}…`;
    return element;
  }

  ignoreEvent() {
    return true;
  }
}

const added = StateEffect.define();
const removed = StateEffect.define();

export const placeholders = StateField.define({
  create: () => Decoration.none,
  update(set, transaction) {
    set = set.map(transaction.changes);

    for (const effect of transaction.effects) {
      if (effect.is(added)) {
        const { id, label, pos } = effect.value;
        set = set.update({ add: [Decoration.widget({ widget: new Placeholder(id, label), side: 1 }).range(pos)] });
      } else if (effect.is(removed)) {
        set = set.update({ filter: (from, to, value) => value.spec.widget.id !== effect.value });
      }
    }

    return set;
  },
  provide: (field) => EditorView.decorations.from(field),
});

const find = (view, id) => {
  let found = null;

  view.state.field(placeholders).between(0, view.state.doc.length, (from, to, value) => {
    if (value.spec.widget.id === id) {
      found = from;
    }
  });

  return found;
};

export const hasPlaceholder = (view, id) => find(view, id) !== null;

// The offset of the placeholder now, or -1 when the view has no placeholder with this id.
export const placeholderPosition = (view, id) => find(view, id) ?? -1;

export const addPlaceholder = (view, id, pos, label) => {
  view.dispatch({ effects: added.of({ id, label, pos: Math.min(pos, view.state.doc.length) }) });
};

export const removePlaceholder = (view, id) => {
  view.dispatch({ effects: removed.of(id) });
};

export const cursor = (view) => view.state.selection.main.head;

export const replacePlaceholder = (view, id, insertion) => {
  const pos = find(view, id) ?? cursor(view);
  const doc = view.state.doc.toString();
  const { text, cursor: offset } = insertion(doc.slice(0, pos), doc.slice(pos));

  view.focus();

  view.dispatch({
    changes: { from: pos, insert: text },
    selection: { anchor: pos + offset },
    effects: removed.of(id),
    scrollIntoView: true,
  });
};

const place = (event, view) => {
  try {
    return view.posAtCoords({ x: event.clientX, y: event.clientY }) ?? cursor(view);
  } catch {
    return cursor(view);
  }
};

// A paste with text is a paste of text, for example a copy from a spreadsheet that also carries an image.
export const fileHandlers = (onFiles) =>
  EditorView.domEventHandlers({
    paste(event, view) {
      const data = event.clipboardData;
      const files = [...(data?.files ?? [])];

      if (files.length === 0 || data.getData('text/plain') !== '') {
        return false;
      }

      event.preventDefault();
      onFiles(files, cursor(view));
      return true;
    },
    drop(event, view) {
      const files = [...(event.dataTransfer?.files ?? [])];

      if (files.length === 0) {
        return false;
      }

      event.preventDefault();
      onFiles(files, place(event, view));
      return true;
    },
  });
