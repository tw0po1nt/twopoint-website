import { Tag } from '@lezer/highlight';

export const directiveTag = Tag.define('directive');

// A leaf directive alone on its line, for example ::youtube{id="qRuKTkDvAk4"}.
const directive = /^::[A-Za-z][\w-]*\{.*\}\s*$/;

export const directiveLines = {
  defineNodes: [{ name: 'DirectiveLine', block: true, style: directiveTag }],
  parseBlock: [
    {
      name: 'DirectiveLine',
      parse(context, line) {
        if (!directive.test(line.text.slice(line.pos))) {
          return false;
        }

        context.addElement(context.elt('DirectiveLine', context.lineStart + line.pos, context.lineStart + line.text.length));
        context.nextLine();
        return true;
      },
    },
  ],
};
