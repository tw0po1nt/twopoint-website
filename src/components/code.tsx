import { FC, PropsWithChildren } from "react";
import {
  Light as SyntaxHighlighter,
  SyntaxHighlighterProps,
} from "react-syntax-highlighter";
import atomOneDark from "react-syntax-highlighter/dist/esm/styles/hljs/atom-one-dark";
import atomOneLight from "react-syntax-highlighter/dist/esm/styles/hljs/atom-one-light";

export interface CodeProps {
  language: SyntaxHighlighterProps["language"];
  children: string | string[];
}

const Code: FC<PropsWithChildren<CodeProps>> = ({ children, language }) => {
  return (
    <section>
      <div className="block dark:hidden">
        <SyntaxHighlighter
          language={language}
          style={atomOneLight}
          className="rounded-lg shadow-md"
        >
          {children}
        </SyntaxHighlighter>
      </div>
      <div className="hidden dark:block">
        <SyntaxHighlighter
          language={language}
          style={atomOneDark}
          className="rounded-lg shadow-md"
        >
          {children}
        </SyntaxHighlighter>
      </div>
    </section>
  );
};

export default Code;
