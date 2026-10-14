namespace TwoPoint.Dependencies.Subscriptions

open System.Net

module internal SubscriptionEmails =

  let welcomeSubject = "Welcome to TwoPoint!"

  /// <summary>
  /// The intro that goes above the latest post in a welcome
  /// </summary>
  /// <param name="name">The name of the subscription, if the subscriber gave one</param>
  let welcomeIntro (name : string option) =
    let greeting =
      name
      |> Option.map (fun name -> $"Hey {WebUtility.HtmlEncode name}!")
      |> Option.defaultValue "Hey there!"

    $$"""
<div style="padding: 30px 30px 10px 30px; border-bottom: 1px solid #eeeeee; font-family: Arial, Helvetica, sans-serif;">
  <p style="margin: 0 0 16px 0; font-size: 20px; font-weight: bold; line-height: 1.3; color: #1f2937;">
    {{greeting}}
  </p>
  <p style="margin: 0 0 16px 0; font-size: 16px; line-height: 1.8; color: #333333;">
    Thanks for subscribing to TwoPoint! To get you started, here is my latest post.
  </p>
  <p style="margin: 0 0 16px 0; font-size: 16px; line-height: 1.8; color: #333333;">
    Thanks,
    <br>
    <span style="font-weight: bold;">Matt</span>
  </p>
</div>
"""
