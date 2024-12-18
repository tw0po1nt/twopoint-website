module Test

open Feliz

type Components =
  [<ReactComponent(exportDefault=true)>]
  static member Test(config: string, ?key: string) =
    Html.section [
      prop.className "relative not-prose scroll-mt-[72px]"
      prop.children [
        Html.div [
          prop.className "intersect-once motion-safe:md:intersect:animate-fade motion-safe:md:opacity-0 intersect-quarter mx-auto intercept-no-queue px-4 relative lg:py-20 md:px-6 md:py-16 py-12 text-default max-w-7xl"
          prop.children [
            Html.div [
              prop.className "mb-8 md:mb-12 md:mx-auto text-center max-w-3xl"
              prop.children [
                Html.h2 [
                  prop.className "font-bold font-heading leading-tighter tracking-tighter text-heading text-3xl md:text-4xl"
                  prop.text "[Title]"
                ]

                // Html.p [
                //   prop.className "text-muted text-xl mt-4"
                //   prop.text "Lorem ipsum odor amet, consectetuer adipiscing elit. Mus tellus proin gravida litora consectetur iaculis conubia scelerisque. Hendrerit aenean massa tincidunt torquent tellus laoreet fusce habitasse."
                // ]              
              ]
            ]
          ]
        ]
      ]
    ]