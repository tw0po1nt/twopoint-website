module TwoPoint.Site.Generator.Tests.CliTests

open TwoPoint.Site.Generator

open Expecto

[<Tests>]
let tests =
  testList "Cli" [
    test "build with no stage option fails with the stage names" {
      let error = Expect.wantError (Cli.parse [ "build" ]) "build needs a stage"
      Expect.equal error "build needs a stage. Give --stage local|staging|production." "The error names the option"
    }

    test "build with no stage value fails" {
      Expect.isError (Cli.parse [ "build"; "--stage" ]) "build needs a stage value"
    }

    test "build with an unknown stage fails with the stage names" {
      let error = Expect.wantError (Cli.parse [ "build"; "--stage"; "prod" ]) "prod is not a stage"
      Expect.equal error "Unknown stage 'prod'. Give --stage local|staging|production." "The error names the stages"
    }

    test "build reads each stage" {
      for stage in Stage.all do
        Expect.equal (Cli.parse [ "build"; "--stage"; Stage.name stage ]) (Ok(Cli.Build stage)) "build reads the stage"
    }

    test "an unknown command fails with the usage" {
      Expect.equal (Cli.parse [ "serve" ]) (Error Cli.usage) "An unknown command gives the usage"
    }
  ]
