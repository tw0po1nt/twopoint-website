module Pdf

open Feliz
open Feliz.UseMediaQuery
open Feliz.ReactPdf


[<ReactComponent(exportDefault=true)>]
let Pdf (file : string) =
  let pageNumber, setPageNumber = React.useState 1
  let numPages, setNumPages = React.useState<int option> None

  let screenSize = React.useResponsive()

  Html.div [
    prop.children [
      ReactPdf.document [
        prop.className "flex flex-row justify-center mb-4"
        ReactPdf.File file
        ReactPdf.OnLoadSuccess (fun num -> setNumPages (Some num))
        prop.children [
          let width =
            match screenSize with
            | ScreenSize.Mobile -> 350
            | ScreenSize.MobileLandscape -> 600
            | ScreenSize.Tablet -> 725
            | ScreenSize.Desktop -> 600
            | ScreenSize.WideScreen -> 725

          ReactPdf.page [
            ReactPdf.Width width
            ReactPdf.PageNumber pageNumber
            prop.className "border border-black"
          ]
        ]
      ]

      Html.div [
        prop.className "flex flex-row justify-center items-center gap-4"
        prop.children [
          Html.button [
            prop.className ["btn"; if pageNumber > 1 then "btn-primary"; ]
            prop.disabled (pageNumber <= 1)
            prop.onClick (fun _ -> setPageNumber (pageNumber - 1))
            prop.text "Previous"
          ]
          Html.span [
            prop.text (sprintf "%d of %s" pageNumber (numPages |> Option.map string |> Option.defaultValue "unknown"))
          ]
          Html.button [
            prop.className ["btn"; if numPages |> Option.forall ((<>) pageNumber) then "btn-primary"; ]
            prop.disabled (numPages |> Option.forall ((=) pageNumber))
            prop.onClick (fun _ -> setPageNumber (pageNumber + 1))
            prop.text "Next"
          ]
        ]
      ]
    ]
  ]
  