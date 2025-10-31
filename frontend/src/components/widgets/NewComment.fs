module NewComment

open Feliz

open System.Text.RegularExpressions

type NewCommentData =
  { Email : string
    Name : string
    Comment : string }

let private whitespace = [| ' '; '\t'; '\n' |]

[<ReactComponent>]
let NewComment (disabled: bool, initial: NewCommentData option, onComment: NewCommentData * (unit -> unit) -> unit) =

  let emailIsFocused, setEmailIsFocused = React.useState false
  let emailIsDirty, setEmailIsDirty = React.useState false
  let email, setEmail = React.useState (initial |> Option.map _.Email |> Option.defaultValue "")

  let name, setName = React.useState (initial |> Option.map _.Name |> Option.defaultValue "")

  let commentIsFocused, setCommentIsFocused = React.useState false
  let commentIsDirty, setCommentIsDirty = React.useState false
  let comment, setComment = React.useState (initial |> Option.map _.Comment |> Option.defaultValue "")

  let resetForm () =
    setEmail ""
    setEmailIsDirty false
    setName ""
    setComment ""
    setCommentIsDirty false

  let isEmailValid = React.useMemo (
    (fun () ->
      let emailRegex = Regex "^\w+([-+.']\w+)*@(\[*\w+)([-.]\w+)*\.\w+([-.]\w+\])*$"
      emailRegex.IsMatch email),
    [| box email |]
  )

  let isCommentValid = React.useMemo (
    (fun () ->
      let commentIsValid = comment.Trim(whitespace).Length <> 0
      commentIsValid),
    [| box comment |]
  )

  let isFormValid = React.useMemo (
    (fun () ->
      isEmailValid && isCommentValid),
    [| isEmailValid; isCommentValid |]
  )

  Html.div [
    prop.className [
      "flex flex-col text-left backdrop-blur bg-white border dark:bg-zinc-900 rounded-lg border-gray-200 dark:border-gray-700 lg:p-8 max-w-5xl mx-auto p-4 shadow sm:p-6 w-full";
      if disabled then "opacity-50"
    ]
    prop.children [
      Html.form [
        prop.children [
          Html.div [
            prop.className "mb-6"
            prop.children [
              Html.label [
                prop.className "block font-medium text-sm mb-2"
                prop.htmlFor "email"
                prop.text "Email (required)"
              ]

              Html.input [
                prop.className "block bg-white border border-gray-200 dark:bg-zinc-900 dark:border-gray-700 px-4 py-3 rounded-lg text-md w-full mb-2"
                prop.id "email"
                prop.name "Email"
                prop.type' "text"
                prop.autoComplete "on"
                prop.placeholder ""
                prop.value email
                prop.onFocus (ignore >> fun () -> setEmailIsFocused true)
                prop.onBlur (ignore >> fun () -> setEmailIsFocused false)
                prop.onTextChange (fun text ->
                  setEmailIsDirty true
                  setEmail text
                )
                prop.disabled disabled
              ]

              if not isEmailValid && emailIsDirty && not emailIsFocused
              then
                Html.p [
                  prop.className "block font-medium text-sm text-primary"
                  prop.text "Must be provide a valid email address" 
                ]
            ]
          ]

          Html.div [
            prop.className "mb-6"
            prop.children [
              Html.label [
                prop.className "block font-medium text-sm mb-2"
                prop.htmlFor "name"
                prop.text "Name (optional)"
              ]

              Html.input [
                prop.className "block bg-white border border-gray-200 dark:bg-zinc-900 dark:border-gray-700 px-4 py-3 rounded-lg text-md w-full mb-2"
                prop.id "name"
                prop.name "name"
                prop.type' "text"
                prop.autoComplete "on"
                prop.placeholder ""
                prop.value name
                prop.onTextChange setName
                prop.maxLength 256
                prop.disabled disabled
              ]
            ]
          ]

          Html.div [
            prop.className "mb-6"
            prop.children [
              Html.label [
                prop.className "block font-medium text-sm mb-2"
                prop.htmlFor "name"
                prop.text "Comment"
              ]

              Html.textarea [
                prop.className "block bg-white border border-gray-200 dark:bg-zinc-900 dark:border-gray-700 px-4 py-3 rounded-lg text-md w-full mb-2"
                prop.id "textarea"
                prop.name "comment"
                prop.rows 4
                prop.maxLength 1000
                prop.value comment
                prop.onFocus (ignore >> fun () -> setCommentIsFocused true)
                prop.onBlur (ignore >> fun () -> setCommentIsFocused false)
                prop.onTextChange (fun text ->
                  setCommentIsDirty true
                  setComment text
                )
                prop.disabled disabled
              ]

              if not isCommentValid && commentIsDirty && not commentIsFocused
              then
                Html.p [
                  prop.className "block font-medium text-sm text-primary"
                  prop.text "Cat got your tongue?" 
                ]
            ]
          ]

          Html.div [
            prop.className "grid mt-10"
            prop.children [
              Html.button [
                prop.className (if isFormValid then "btn-primary" else "btn-tertiary dark:hover:bg-zinc-800")
                prop.text "Comment"
                prop.type' "button"
                prop.onClick (fun _ ->
                  if not isFormValid 
                  then
                    setEmailIsDirty true
                    setCommentIsDirty true
                  else
                    onComment ({ Email = email; Name = name; Comment = comment.Trim whitespace }, resetForm)
                )
                prop.disabled disabled
              ]
            ]
          ]
        ]
      ]
    ]
  ]