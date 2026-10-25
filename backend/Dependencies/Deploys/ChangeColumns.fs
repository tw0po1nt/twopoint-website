namespace TwoPoint.Dependencies.Deploys

open TwoPoint.Core.Deploys
open TwoPoint.Dependencies.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling

open System
open System.Globalization

module ChangeColumns =

  let dateText (date : DateTime) = date.ToString("o", CultureInfo.InvariantCulture)

  let parseDate (text : string) =
    DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime()

  let write (dateColumn : string) (entity : TableEntity) (date : DateTime) (production : ProductionDispatch) =
    entity[dateColumn] <- dateText date
    entity["Production"] <- ProductionDispatch.name production
    entity["RunNumber"] <- production |> ProductionDispatch.runNumber |> Option.map _.Value |> Option.toNullable

  let read (dateColumn : string) (entity : TableEntity) : (DateTime * ProductionDispatch) option =
    option {
      let! date = entity.TryGetString dateColumn
      let number = entity.GetInt64 "RunNumber" |> Option.ofNullable
      let! production = entity.TryGetString "Production" |> Option.bind (fun name -> ProductionDispatch.tryParse name number)
      return parseDate date, production
    }
