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

[<Tests>]
let partitionTests =
  testList "Db.filtersOfKeysInPartitions" [
    test "the replies of top-level comments on two posts have one query for each post, in the partition of the post" {
      let topLevels = [ "post-a", "1"; "post-b", "2"; "post-a", "3" ]

      Expect.equal
        (Db.filtersOfKeysInPartitions (eq "ParentId") topLevels |> List.map FilterConverter.toQuery)
        [ "((ParentId eq '1') or ParentId eq '3') and PartitionKey eq 'post-a'"
          "(ParentId eq '2') and PartitionKey eq 'post-b'" ]
        "Expected the IDs of each post in parentheses, and the PartitionKey of the post"
    }

    test "a filter in one partition has at most 100 keys, and each filter has the PartitionKey" {
      let filters = Db.filtersOfKeysInPartitions (eq "ParentId") (keys 150 |> List.map (fun key -> "post-a", key))

      Expect.equal (filters |> List.length) 2 "Expected 2 filters for 150 keys"
      for filter in filters do
        Expect.stringEnds (FilterConverter.toQuery filter) ") and PartitionKey eq 'post-a'" "Expected the PartitionKey"
    }

    test "no key gives no filter" {
      Expect.isEmpty (Db.filtersOfKeysInPartitions (eq "ParentId") []) "Expected no filter"
    }
  ]
