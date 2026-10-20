module TwoPoint.Site.Generator.Tests.CliTests

open TwoPoint.Site.Generator

open Expecto
open System

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

    test "upload with no container option fails with a clear error" {
      let error = Expect.wantError (Cli.parse [ "upload" ]) "upload needs a container"
      Expect.equal error "upload needs a container. Give --container <uri>." "The error names the option"
    }

    test "upload with no container value fails" {
      Expect.isError (Cli.parse [ "upload"; "--container" ]) "upload needs a container value"
    }

    test "upload with a container that is not an https URI fails" {
      for value in [ "newsletter"; "http://account.blob.core.windows.net/newsletter" ] do
        let error = Expect.wantError (Cli.parse [ "upload"; "--container"; value ]) $"'{value}' is not an https URI"

        Expect.equal
          error
          $"The container '{value}' is not an https URI. Give --container <uri>."
          "The error names the value"
    }

    test "upload reads the container URI" {
      let uri = "https://account.blob.core.windows.net/newsletter"
      Expect.equal (Cli.parse [ "upload"; "--container"; uri ]) (Ok(Cli.Upload(Uri uri))) "upload reads the URI"
    }

    test "upload has no stage option" {
      Expect.equal
        (Cli.parse [ "upload"; "--container"; "https://account.blob.core.windows.net/newsletter"; "--stage"; "production" ])
        (Error Cli.usage)
        "A stage option gives the usage"
    }

    test "an unknown command fails with the usage" {
      Expect.equal (Cli.parse [ "serve" ]) (Error Cli.usage) "An unknown command gives the usage"
    }
  ]
