module Fable.ReactHotToast

open Fable.Core.JsInterop

let private toast : obj = importDefault "react-hot-toast"

let successToast (str : string) : unit = 
  toast?success $ str
