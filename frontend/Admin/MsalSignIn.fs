[<RequireQualifiedAccess>]
module TwoPoint.Admin.MsalSignIn

open Fable.Core

let private activeAccount (app: Msal.PublicClientApplication) =
  match app.getActiveAccount () with
  | null -> app.getAllAccounts () |> Array.tryHead
  | account -> Some account

let create () : SignIn =
  let app =
    Msal.publicClientApplication
      {| auth =
          {| clientId = Config.clientId ()
             authority = Config.authority ()
             redirectUri = $"{Config.origin ()}/redirect/" |}
         cache = {| cacheLocation = "localStorage" |} |}

  let scopes = [| Config.scope () |]

  { new SignIn with
      member _.start() =
        app.initialize ()
        |> Promise.bind app.handleRedirectPromise
        |> Promise.map (fun result ->
          let account =
            match result with
            | null -> activeAccount app
            | result -> Some result.account

          account |> Option.iter app.setActiveAccount
          account.IsSome)

      member _.signIn() = app.loginRedirect {| scopes = scopes |}

      member _.token() =
        match activeAccount app with
        | None -> Promise.lift None
        | Some account ->
          app.acquireTokenSilent {| scopes = scopes; account = account |}
          |> Promise.map (fun result -> Some result.accessToken)
          |> Promise.catchBind (fun error ->
            if Msal.isInteractionRequired error then
              Promise.lift None
            else
              Promise.reject error)

      member _.signInByPopup() =
        app.acquireTokenPopup {| scopes = scopes; account = activeAccount app |> Option.toObj |}
        |> Promise.map (fun result -> app.setActiveAccount result.account) }
