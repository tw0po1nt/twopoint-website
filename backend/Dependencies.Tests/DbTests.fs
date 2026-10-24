module TwoPoint.Dependencies.Tests.DbTests

open TwoPoint.Dependencies.Util

open Azure.Data.Tables.FSharp
open Expecto

let private keysIn (filter : Filter) =
  let rec leaves filter =
    match filter with
    | Column (_, Eq key) -> [ string key ]
    | Binary (left, _, right) -> leaves left @ leaves right
    | _ -> []
  leaves filter

let private keys count = List.init count (fun i -> $"key-{i}")

[<Tests>]
let tests =
  testList "Db.filtersOfKeys" [
    test "a filter has at most 100 keys" {
      let filters = Db.filtersOfKeys (eq "PartitionKey") (keys 250)

      Expect.equal (filters |> List.map (keysIn >> List.length)) [ 100; 100; 50 ] "Expected 3 filters for 250 keys"
      Expect.equal (filters |> List.collect keysIn) (keys 250) "Expected each key in a filter"
    }

    test "a repeated key is in one filter once" {
      let filters = Db.filtersOfKeys (eq "PartitionKey") [ "a"; "b"; "a" ]

      Expect.equal (filters |> List.collect keysIn) [ "a"; "b" ] "Expected each key once"
    }

    test "no key gives no filter, so no query reads the whole table" {
      Expect.isEmpty (Db.filtersOfKeys (eq "PartitionKey") []) "Expected no filter"
    }
  ]
