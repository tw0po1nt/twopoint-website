namespace TwoPoint.Core.Util

open System.Diagnostics.CodeAnalysis
open System.Globalization

[<AutoOpen>]
module Parsing =
  open System.Text.RegularExpressions
  
  let regexParser
    ([<StringSyntax(StringSyntaxAttribute.Regex)>] regex)
    (value: string) =  
    if Regex.IsMatch(value, regex)
    then Some value
    else None
  
  /// <summary>
  /// An active pattern for parsing <see cref="System.Int32"/> values
  /// </summary>
  let (|Int|_|) (str: string) =
    match System.Int32.TryParse str with
    | true, parsed -> Some parsed
    | _ -> None
    
  /// <summary>
  /// An active pattern for parsing <see cref="System.UInt32"/> values
  /// </summary>
  let (|UInt|_|) (str: string) =
    match System.UInt32.TryParse str with
    | true, parsed -> Some parsed
    | _ -> None
    
  /// <summary>
  /// An active pattern for parsing <see cref="System.Uri"/> values
  /// </summary>
  let (|Uri|_|) (str: string) =
    match System.Uri.TryCreate(str, System.UriKind.Absolute) with
    | true, uri -> Some uri
    | _ -> None
    
  /// <summary>
  /// An active pattern for parsing <see cref="System.Guid"/> values
  /// </summary>
  let (|Guid|_|) (str: string) =
    match System.Guid.TryParse str with
    | true, parsed -> Some parsed
    | _ -> None
    
  /// <summary>
  /// An active pattern for parsing UTC timestamps as <see cref="System.DateTime"/> values
  /// </summary>
  let (|DateTime|_|) (str: string) =
    match System.DateTime.TryParseExact(str, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.None) with
    | true, parsed -> Some parsed
    | _ -> None
