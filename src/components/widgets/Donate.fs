module Donate

open Browser
open Fable.ReactHotToast
open Feliz
open Feliz.QRCodeSVG


let [<Literal>] private sample = "./donation-config.json" 
type private DonationConfig = Fable.JsonProvider.Generator<sample>

[<ReactComponent(exportDefault=true)>]
let Donate (config : string) =
  let config = DonationConfig(config)

  let firstCurrency = (config.currencies |> Array.head)
  let (selectedCurrency, setSelectedCurrency) = React.useState(firstCurrency)

  Html.section [
    prop.className "relative not-prose scroll-mt-[72px]"
    prop.children [
      Html.div [
        prop.className "intersect-once motion-safe:md:intersect:animate-fade motion-safe:md:opacity-0 intersect-quarter mx-auto intercept-no-queue px-4 relative lg:pb-20 md:px-6 md:pb-16 pb-12 text-default max-w-7xl"
        prop.children [
          Html.div [
            prop.className "mb-8 md:mb-12 md:mx-auto text-center max-w-3xl"
            prop.children [
              Html.h2 [
                prop.className "font-bold font-heading leading-tighter tracking-tighter text-heading text-3xl md:text-4xl"
                prop.text "Buy me a beer? 🍺"
              ]

              Html.p [
                prop.className "text-muted text-xl mt-4"
                prop.text "I write content for this blog in my free time. Ads are gross. If you like this post, consider supporting me via one of your favorite cryptocurrencies!"
              ]

              Html.div [
                prop.className "mt-4"
                prop.children [
                  Html.div [
                    prop.className "sm:hidden"
                    prop.children [
                      Html.label [
                        prop.htmlFor "Tab"
                        prop.className "sr-only"
                        prop.text "Tab"
                      ]

                      Html.select [
                        prop.id "Tab"
                        prop.className "w-full rounded-md border-gray-200"
                        prop.defaultValue (config.currencies |> Array.head).symbol
                        prop.onChange (fun (value: string) -> 
                          config.currencies
                          |> Array.tryFind (fun ccy -> ccy.symbol = value)
                          |> Option.iter setSelectedCurrency
                        )
                        prop.children (
                          config.currencies
                          |> Array.map (fun ccy -> 
                            Html.option [
                              prop.text ccy.symbol
                              prop.value ccy.symbol
                            ]
                          )
                          |> Array.toList
                        )
                      ]
                    ]
                  ]

                  Html.div [
                    prop.className "hidden sm:block"
                    prop.children [
                      Html.nav [
                        prop.className "flex justify-evenly"
                        prop.ariaLabel "Tabs"
                        prop.children (
                          config.currencies
                          |> Array.map (fun ccy ->
                            Html.p [
                              prop.className [
                                "shrink-0"; "rounded-lg"; "p-2"; "text-sm"; "font-medium"; "cursor-pointer";
                                if ccy.symbol = selectedCurrency.symbol
                                then "bg-red-100 text-red-600 dark:bg-red-700 dark:text-default"
                                else "text-gray-500 hover:bg-gray-50 hover:text-gray-700 dark:text-gray-400 dark:hover:bg-gray-900 dark:hover:text-gray-200"
                              ]
                              prop.text ccy.symbol
                              prop.onClick (fun _ -> setSelectedCurrency ccy)
                            ]
                          )
                          |> Array.toList
                        )
                      ]
                    ]
                  ]
                ]
              ]

              Html.div [
                prop.className "flex flex-col mt-4 items-center"
                prop.children [
                  Html.div [
                    prop.className "flex justify-center"
                    prop.children [
                      QRCodeSVG.create [
                        QRCodeSVG.Value selectedCurrency.address
                      ]
                    ]
                  ]

                  Html.p [
                    prop.className "mt-4 max-w-96 truncate"
                    prop.text selectedCurrency.address
                  ]

                  Html.div [
                    prop.className "w-1/4 mt-4 rounded-lg p-2 text-sm font-medium cursor-pointer text-gray-500 hover:bg-gray-50 hover:text-gray-700 dark:text-gray-400 dark:hover:bg-gray-900 dark:hover:text-gray-200"
                    prop.onClick (fun _ ->
                      navigator.clipboard
                      |> Option.iter (fun cb ->
                        cb.writeText selectedCurrency.address 
                        |> ignore
                        successToast "Copied!"
                      )
                    )
                    prop.children [
                      Html.p "Copy"
                    ]
                  ]
                ]
              ]
            ]
          ]
        ]
      ]
    ]
  ]