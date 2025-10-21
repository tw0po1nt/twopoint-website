module DateFormatter

open System

let formatDate (dateString: string) : string =
    match DateTime.TryParse dateString with
    | true, date -> date.ToString "MMM d, yyyy"
    | _ -> ""
