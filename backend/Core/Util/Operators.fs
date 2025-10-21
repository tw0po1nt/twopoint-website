namespace TwoPoint.Core.Util

[<AutoOpen>]
module Operators =
  
  /// <summary> Creates a constant function.</summary>
  /// <param name="k">The constant value.</param>
  /// <returns>The constant value function.</returns>
  let inline konst k = fun _ -> k
  
  // Curry: ('a * 'b -> 'c) -> 'a -> 'b -> 'c
  let curry f a b = f (a, b)

  // Uncurry: ('a -> 'b -> 'c) -> ('a * 'b) -> 'c
  let uncurry f (a, b) = f a b
  
  // Uncurry3: ('a -> 'b -> 'c -> 'd) -> ('a * 'b * 'c) -> 'd
  let uncurry3 f (a, b, c) = f a b c
  
 
