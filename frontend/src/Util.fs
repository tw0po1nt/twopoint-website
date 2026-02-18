module Util

let inline flip f x y = f y x

let inline konst k = fun _ -> k
