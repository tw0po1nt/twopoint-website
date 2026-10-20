# Grammar sources

The highlighter embeds the JSON files in this folder. Do not edit a file by hand. To update a
grammar file, copy the new version from its source. To update `css-variables.json`, apply the
filter in the next section to the new version of its source. Then change the row of the file in
this table.

| File | Source | Version | License |
| --- | --- | --- | --- |
| `fsharp.json` | `tm-grammars` npm package, from microsoft/vscode `extensions/fsharp/syntaxes/fsharp.tmLanguage.json` | 1.32.22, commit `e6cffb2` | MIT |
| `csharp.json` | `tm-grammars` npm package, from microsoft/vscode `extensions/csharp/syntaxes/csharp.tmLanguage.json` | 1.32.22, commit `af60048` | MIT |
| `swift.json` | `tm-grammars` npm package, from jtbandes/swift-tmlanguage `Swift.tmLanguage.yaml` | 1.32.22, commit `2be007b` | MIT |
| `shellscript.json` | `tm-grammars` npm package, from microsoft/vscode `extensions/shellscript/syntaxes/shell-unix-bash.tmLanguage.json` | 1.32.22, commit `9473445` | MIT |
| `diff.json` | `tm-grammars` npm package, from microsoft/vscode `extensions/diff/syntaxes/diff.tmLanguage.json` | 1.32.22, commit `4549bd2` | MIT |
| `json.json` | `tm-grammars` npm package, from microsoft/vscode `extensions/json/syntaxes/JSON.tmLanguage.json` | 1.32.22, commit `d6af489` | MIT |
| `xml.json` | `TextMateSharp.Grammars` NuGet package, resource `Grammars/xml/syntaxes/xml.tmLanguage.json`, from atom/language-xml | 2.0.4, commit `7bc75df` | MIT |
| `css.json` | `TextMateSharp.Grammars` NuGet package, resource `Grammars/css/syntaxes/css.tmLanguage.json`, from atom/language-css | 2.0.4, commit `4a6dc90` | MIT |
| `css-variables.json` | `shiki` npm package, the `tokenColors` of `createCssVariablesTheme()` | 3.22.0 | MIT |

The commit is the commit of the grammar in its source repository.

`css-variables.json` is the scope map. It is a filtered copy of its source: it contains each rule
of the theme that sets a foreground color, and it omits each `fontStyle`. Each color is a CSS variable, and the end of the
variable name is the role of the token. For example, `var(--shiki-token-keyword)` gives the role
`keyword`. `var(--shiki-foreground)` gives no role.

The F# grammar includes `text.html.markdown`, and the XML grammar includes `source.java`. This
folder has no grammar for these scopes, so the highlighter ignores the two includes.

`LICENSES.txt` contains the text of each license.
