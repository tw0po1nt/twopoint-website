module DateFormatter

open System
open Fable.Core
open Fable.Core.JsInterop

[<Import("RelativeTimeFormat", "Intl")>]
type RelativeTimeFormat =
    [<Emit("new Intl.RelativeTimeFormat($0, $1)")>]
    static member Create(locale: string, options: obj) : RelativeTimeFormat = jsNative
    
    [<Emit("$0.format($1, $2)")>]
    member _.format(value: float, unit: string) : string = jsNative

let formatDate (dateString: string) : string =
    match DateTime.TryParse dateString with
    | true, date -> date.ToString "MMM d, yyyy"
    | _ -> ""

let formatRelativeDate (dateString: string) : string =
    match DateTime.TryParse dateString with
    | true, date ->
        let now = DateTime.Now
        let diff = now - date
        
        let rtf = RelativeTimeFormat.Create("en", createObj ["numeric" ==> "auto"])
        
        // Calculate the appropriate unit and value
        if diff.TotalDays < 1.0 then
            if diff.TotalHours < 1.0 then
                if diff.TotalMinutes < 1.0 then
                    "just now"
                else
                    let minutes = -diff.TotalMinutes |> floor
                    rtf.format(minutes, "minute")
            else
                let hours = -diff.TotalHours |> floor
                rtf.format(hours, "hour")
        elif diff.TotalDays < 7.0 then
            let days = -diff.TotalDays |> floor
            rtf.format(days, "day")
        elif diff.TotalDays < 30.0 then
            let weeks = -(diff.TotalDays / 7.0) |> floor
            rtf.format(weeks, "week")
        elif diff.TotalDays < 365.0 then
            let months = -(diff.TotalDays / 30.0) |> floor
            rtf.format(months, "month")
        else
            let years = -(diff.TotalDays / 365.0) |> floor
            rtf.format(years, "year")
    | _ -> ""
