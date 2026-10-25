/// The part of CodeMirror 6 that the post editor uses.
[<RequireQualifiedAccess>]
module TwoPoint.Admin.CodeMirror

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop

open System

type Extension = interface end

type Facet<'T> =
  abstract ``of``: 'T -> Extension

type Text =
  abstract toString: unit -> string

[<Import("EditorState", "@codemirror/state")>]
type EditorState() =
  member _.doc: Text = jsNative
  static member create(config: obj) : EditorState = jsNative

type ViewUpdate =
  abstract docChanged: bool
  abstract state: EditorState

[<Import("EditorView", "@codemirror/view")>]
type EditorView(config: obj) =
  member _.state: EditorState = jsNative
  member _.focus() : unit = jsNative
  member _.destroy() : unit = jsNative
  static member lineWrapping: Extension = jsNative
  static member updateListener: Facet<ViewUpdate -> unit> = jsNative
  static member contentAttributes: Facet<obj> = jsNative

[<Import("HighlightStyle", "@codemirror/language")>]
type HighlightStyle() =
  static member define(specs: obj array) : obj = jsNative

[<ImportMember("@codemirror/view")>]
let placeholder (text: string) : Extension = jsNative

[<ImportMember("@codemirror/view")>]
let keymap: Facet<obj array> = jsNative

[<ImportMember("@codemirror/commands")>]
let history () : Extension = jsNative

[<ImportMember("@codemirror/commands")>]
let defaultKeymap: obj array = jsNative

[<ImportMember("@codemirror/commands")>]
let historyKeymap: obj array = jsNative

[<ImportMember("@codemirror/language")>]
let syntaxHighlighting (style: obj) : Extension = jsNative

[<ImportMember("@codemirror/lang-markdown")>]
let markdown (config: obj) : Extension = jsNative

[<ImportMember("@lezer/highlight")>]
let tags: obj = jsNative

/// A @lezer/markdown extension. It marks each leaf directive line, for example `::pdf{file="…"}`.
[<ImportMember("./directive-lines.js")>]
let directiveLines: obj = jsNative

[<ImportMember("./directive-lines.js")>]
let directiveTag: obj = jsNative

[<ImportMember("./upload-placeholders.js")>]
let placeholders: Extension = jsNative

[<ImportMember("./upload-placeholders.js")>]
let fileHandlers (onFiles: Action<File array, int>) : Extension = jsNative

[<ImportMember("./upload-placeholders.js")>]
let hasPlaceholder (view: EditorView) (id: int) : bool = jsNative

[<ImportMember("./upload-placeholders.js")>]
let placeholderPosition (view: EditorView) (id: int) : int = jsNative

[<ImportMember("./upload-placeholders.js")>]
let addPlaceholder (view: EditorView) (id: int) (pos: int) (label: string) : unit = jsNative

[<ImportMember("./upload-placeholders.js")>]
let removePlaceholder (view: EditorView) (id: int) : unit = jsNative

[<ImportMember("./upload-placeholders.js")>]
let cursor (view: EditorView) : int = jsNative

/// `insertion` gives `{ text, cursor }`, where `cursor` is an offset in `text`.
[<ImportMember("./upload-placeholders.js")>]
let replacePlaceholder (view: EditorView) (id: int) (insertion: Func<string, string, obj>) : unit = jsNative

let private quietStyle () =
  let style (tag: obj) (cls: string) = createObj [ "tag" ==> tag; "class" ==> cls ]

  HighlightStyle.define
    [| style [| tags?heading1; tags?heading2; tags?heading3; tags?heading4; tags?heading5; tags?heading6 |] "cm-heading"
       style tags?emphasis "cm-emphasis"
       style tags?strong "cm-strong"
       style tags?link "cm-link"
       style tags?url "cm-url"
       style tags?monospace "cm-code"
       style tags?quote "cm-quote"
       style tags?processingInstruction "cm-mark"
       style directiveTag "cm-directive" |]

/// `onChange` runs with the whole text after each change. The text has no line numbers and no gutter.
let create
  (parent: HTMLElement)
  (doc: string)
  (label: string)
  (hint: string)
  (onChange: string -> unit)
  (onFiles: Action<File array, int>)
  : EditorView =
  let onUpdate (update: ViewUpdate) =
    if update.docChanged then
      onChange (update.state.doc.toString ())

  let extensions =
    [| history ()
       keymap.``of`` (Array.append defaultKeymap historyKeymap)
       markdown (createObj [ "extensions" ==> directiveLines ])
       syntaxHighlighting (quietStyle ())
       EditorView.lineWrapping
       placeholder hint
       placeholders
       fileHandlers onFiles
       EditorView.contentAttributes.``of`` (createObj [ "aria-label" ==> label ])
       EditorView.updateListener.``of`` onUpdate |]

  EditorView(createObj [ "state" ==> EditorState.create (createObj [ "doc" ==> doc; "extensions" ==> extensions ]); "parent" ==> parent ])
