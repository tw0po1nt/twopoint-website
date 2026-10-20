module TwoPoint.Site.Generator.Tests.SitePathTests

open TwoPoint.Site.Generator

open Expecto

[<Tests>]
let tests =
  testList "SitePath" [
    test "the root path gives the root index page" {
      Expect.equal (SitePath.ofUrlPath "/") "index.html" "The root path gives index.html"
    }

    test "a folder path gives its index page" {
      Expect.equal (SitePath.ofUrlPath "/posts/") "posts/index.html" "A folder path gives its index.html"
      Expect.equal (SitePath.ofUrlPath "/posts/2/") "posts/2/index.html" "A nested folder path gives its index.html"
    }

    test "a file path gives the same file" {
      Expect.equal (SitePath.ofUrlPath "/robots.txt") "robots.txt" "A file path gives the file"
      Expect.equal (SitePath.ofUrlPath "/posts") "posts" "A path with no trailing slash is a file path"
    }
  ]
