namespace TwoPoint.Http

open TwoPoint.Core.Deploys
open TwoPoint.Core.Shared

open System

type AzureConfig =
  { CommsResourceEndpoint : Uri
    EmailSender : EmailAddress
    EntraTenantId : Guid
    ManagedIdentityClientId : Guid
    TableStorageUri : Uri
    PostStoreUri : Uri
    AssetsUri : Uri
    ServiceBusUri : Uri
    KeyVaultUri : Uri option }

type ValidRedirectUri =
  { Uri : Uri }

type WelcomeConfig =
  { Enabled : bool
    NewsletterContainerUri : Uri
    UnsubscribeUri : Uri }

type GitHubAppConfig =
  { ClientId : string
    SigningKeyUri : Uri }

/// The local backend needs no GitHub App, because it sends no dispatch.
type DeployConfig =
  { ProductionRepo : Repo
    StagingRepo : Repo
    GitHubApp : GitHubAppConfig option }

type PrivateKeySource =
  | Inline of privateKey : string
  | KeyVault of secretName : string * vaultUri : Uri

type WebPushConfig =
  { Subject : string
    PublicKey : string
    PrivateKey : PrivateKeySource }

type Config =
  { Azure : AzureConfig
    WebPush : WebPushConfig option
    ValidRedirectUris : ValidRedirectUri list
    Welcome : WelcomeConfig option
    Deploy : DeployConfig option
    Stage : TwoPoint.Site.Render.Stage }

module Config =

  open TwoPoint.Http.Extensions

  open Symbolica.Extensions.Configuration.FSharp

  let bind config =
    bind {
      let! azure = Bind.section "Azure" <| bind {
        let! commsResourceEndpoint = Bind.valueAt "CommsResourceEndpoint" (Bind.uri UriKind.Absolute)
        let! emailSender = Bind.valueAt "EmailSender" Bind.emailAddress
        let! entraTenantId = Bind.valueAt "EntraTenantId" Bind.guid
        let! managedIdentityClientId = Bind.valueAt "ManagedIdentityClientId" Bind.guid
        let! tableStorageUri = Bind.valueAt "TableStorageUri" (Bind.uri UriKind.Absolute)
        let! postStoreUri = Bind.valueAt "PostStoreUri" (Bind.uri UriKind.Absolute)
        let! assetsUri = Bind.valueAt "AssetsUri" (Bind.uri UriKind.Absolute)
        let! serviceBusUri = Bind.valueAt "ServiceBusUri" (Bind.uri UriKind.Absolute)
        let! keyVaultUri = Bind.optValueAt "KeyVaultUri" (Bind.uri UriKind.Absolute)
        return
          { CommsResourceEndpoint = commsResourceEndpoint
            EmailSender = emailSender
            EntraTenantId = entraTenantId
            ManagedIdentityClientId = managedIdentityClientId
            TableStorageUri = tableStorageUri
            PostStoreUri = postStoreUri
            AssetsUri = assetsUri
            ServiceBusUri = serviceBusUri
            KeyVaultUri = keyVaultUri }
      }

      let! webPush = Bind.optSection "WebPush" <| bind {
        let! subject = Bind.valueAt "Subject" Bind.string
        let! publicKey = Bind.valueAt "PublicKey" Bind.string
        let! inlineKey = Bind.optValueAt "PrivateKey" Bind.string
        let! secretName = Bind.optValueAt "PrivateKeyFromKeyVault" Bind.string
        let! privateKey =
          match inlineKey, secretName, azure.KeyVaultUri with
          | Some key, _, _ -> Binder.result (Inline key)
          | None, Some name, Some vaultUri -> Binder.result (KeyVault(name, vaultUri))
          | None, _, _ -> Binder.fail (Error.keyNotFound "PrivateKey")
        return
          { Subject = subject
            PublicKey = publicKey
            PrivateKey = privateKey }
      }

      let bindValidRedirectUri =  bind {
        let! uri = Bind.valueAt "Uri" (Bind.uri UriKind.Absolute)
        return { Uri = uri }
      }

      let! validRedirectUris = Bind.section "ValidRedirectUris" (Bind.list bindValidRedirectUri)

      let! welcome = Bind.optSection "Welcome" <| bind {
        let! enabled = Bind.optValueAt "Enabled" Bind.bool
        let! newsletterContainerUri = Bind.valueAt "NewsletterContainerUri" (Bind.uri UriKind.Absolute)
        let! unsubscribeUri = Bind.valueAt "UnsubscribeUri" (Bind.uri UriKind.Absolute)
        return
          { Enabled = enabled |> Option.defaultValue false
            NewsletterContainerUri = newsletterContainerUri
            UnsubscribeUri = unsubscribeUri }
      }

      let! deploy = Bind.optSection "Deploy" <| bind {
        let! productionRepo = Bind.valueAt "ProductionRepo" Bind.repo
        let! stagingRepo = Bind.valueAt "StagingRepo" Bind.repo
        let! gitHubApp = Bind.optSection "GitHubApp" <| bind {
          let! clientId = Bind.valueAt "ClientId" Bind.string
          let! signingKeyUri = Bind.valueAt "SigningKeyUri" (Bind.uri UriKind.Absolute)
          return { ClientId = clientId; SigningKeyUri = signingKeyUri }
        }
        return
          { ProductionRepo = productionRepo
            StagingRepo = stagingRepo
            GitHubApp = gitHubApp }
      }

      return
        { Azure = azure
          WebPush = webPush
          ValidRedirectUris = validRedirectUris
          Welcome = welcome
          Deploy = deploy
          Stage =
            if TwoPoint.Site.Render.Azurite.isUrl azure.PostStoreUri
            then TwoPoint.Site.Render.Stage.Local
            else TwoPoint.Site.Render.Stage.Staging }
    }
    |> Binder.eval config
