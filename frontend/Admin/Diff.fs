[<RequireQualifiedAccess>]
module TwoPoint.Admin.Diff

type Line =
  | Same of string
  | Added of string
  | Removed of string

type Entry =
  | Shown of Line
  | Skipped of count: int

let private split (text: string) =
  if text = "" then [||] else text.Replace("\r\n", "\n").Split '\n'

let private commonPrefix (before: string[]) (after: string[]) =
  let limit = min before.Length after.Length
  let mutable count = 0

  while count < limit && before[count] = after[count] do
    count <- count + 1

  count

let private commonSuffix (before: string[]) (after: string[]) (prefix: int) =
  let limit = min before.Length after.Length - prefix
  let mutable count = 0

  while count < limit && before[before.Length - 1 - count] = after[after.Length - 1 - count] do
    count <- count + 1

  count

let lines (before: string) (after: string) : Line list =
  let before = split before
  let after = split after
  let prefix = commonPrefix before after
  let suffix = commonSuffix before after prefix
  let middleBefore = Array.sub before prefix (before.Length - prefix - suffix)
  let middleAfter = Array.sub after prefix (after.Length - prefix - suffix)
  let rows = middleBefore.Length
  let columns = middleAfter.Length
  let table = Array.init (rows + 1) (fun _ -> Array.zeroCreate<int> (columns + 1))

  for row in rows - 1 .. -1 .. 0 do
    for column in columns - 1 .. -1 .. 0 do
      table[row][column] <-
        if middleBefore[row] = middleAfter[column] then
          table[row + 1][column + 1] + 1
        else
          max (table[row + 1][column]) (table[row][column + 1])

  let rec walk row column (rest: Line list) =
    if row < rows && column < columns && middleBefore[row] = middleAfter[column] then
      walk (row + 1) (column + 1) (Same middleBefore[row] :: rest)
    elif row < rows && (column = columns || table[row + 1][column] >= table[row][column + 1]) then
      walk (row + 1) column (Removed middleBefore[row] :: rest)
    elif column < columns then
      walk row (column + 1) (Added middleAfter[column] :: rest)
    else
      rest

  [ for line in Array.take prefix before -> Same line ]
  @ List.rev (walk 0 0 [])
  @ [ for line in Array.skip (after.Length - suffix) after -> Same line ]

let hasChanges (lines: Line list) =
  lines
  |> List.exists (function
    | Same _ -> false
    | Added _
    | Removed _ -> true)

let withContext (context: int) (lines: Line list) : Entry list =
  let array = Array.ofList lines

  let changed =
    array
    |> Array.indexed
    |> Array.choose (fun (index, line) ->
      match line with
      | Same _ -> None
      | Added _
      | Removed _ -> Some index)

  let entries = ResizeArray<Entry>()
  let mutable skipped = 0

  for index in 0 .. array.Length - 1 do
    if changed |> Array.exists (fun other -> abs (other - index) <= context) then
      if skipped > 0 then
        entries.Add(Skipped skipped)
        skipped <- 0

      entries.Add(Shown array[index])
    else
      skipped <- skipped + 1

  if skipped > 0 then
    entries.Add(Skipped skipped)

  List.ofSeq entries
