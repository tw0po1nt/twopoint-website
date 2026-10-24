module TwoPoint.Admin.Main

App.start (MsalSignIn.create ()) (BrowserPush.create (Config.vapidPublicKey ()))
