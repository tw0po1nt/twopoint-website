namespace TwoPoint.Core.Subscriptions

open System
open System.Net

type ConfirmEmailContent =
  { Subject : string
    Html : string
    Text : string }

[<RequireQualifiedAccess>]
module ConfirmEmail =

  let confirmUrl (page : Uri) (subscription : Subscription) = Uri(page, $"?subscriptionId={subscription.Id}")

  let subject = "Confirm your TwoPoint subscription"

  let private encode (text : string) = WebUtility.HtmlEncode text

  /// `sentDate` gives the year of the copyright line.
  let compose (page : Uri) (sentDate : DateTime) (subscription : Subscription) : ConfirmEmailContent =
    let url = (confirmUrl page subscription).AbsoluteUri
    let site = Uri(page, "/").AbsoluteUri
    let copyright = $"© {sentDate.Year} TwoPoint. All rights reserved."
    let intro = "Thanks for subscribing to TwoPoint! Confirm your subscription to get new posts by email."
    let ignore = "If you didn't subscribe, ignore this email. You won't get any posts unless you confirm."

    let html =
      $$"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="x-apple-disable-message-reformatting">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="format-detection" content="telephone=no, date=no, address=no, email=no, url=no">
  <meta name="color-scheme" content="light dark">
  <meta name="supported-color-schemes" content="light dark">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;600&display=swap" rel="stylesheet" media="screen">
  <style>
    .hover-bg-slate-800:hover {
      background-color: #1e293b !important
    }
    @media (max-width: 600px) {
      .sm-p-6 {
        padding: 24px !important
      }
      .sm-px-4 {
        padding-left: 16px !important;
        padding-right: 16px !important
      }
      .sm-px-6 {
        padding-left: 24px !important;
        padding-right: 24px !important
      }
    }
    @media (prefers-color-scheme: dark) {
      .dark-text-red-500 {
        color: #ef4444 !important
      }
      .dark-text-white {
        color: #fffffe !important
      }
    }
  </style>
</head>
<body style="margin: 0; width: 100%; background-color: #f8fafc; padding: 0; -webkit-font-smoothing: antialiased; word-break: break-word">
  <div role="article" aria-roledescription="email" aria-label="{{encode subject}}" lang="en">
    <div class="sm-px-4" style="background-color: #fafafa; font-family: Inter, ui-sans-serif, system-ui, -apple-system, 'Segoe UI', sans-serif">
      <table align="center" style="margin: 0 auto" cellpadding="0" cellspacing="0" role="none">
        <tr>
          <td style="width: 552px; max-width: 100%">
            <div role="separator" style="line-height: 24px">&zwj;</div>
            <table style="width: 100%" cellpadding="0" cellspacing="0" role="none">
              <tr>
                <td class="sm-p-6" style="border-radius: 8px; background-color: #fffffe; padding: 24px 36px; border: 1px solid #e2e8f0">
                  <a href="{{encode site}}" style="color: #000001; text-decoration: none">
                    <span class="dark-text-white" style="display: flex; flex-direction: row; align-items: center; font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 20px; font-weight: 700">
                      Two&nbsp;
                      <svg class="dark-text-red-500" height="1em" width="1em" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg" style="color: #dc2626"><path d="m7 8l-4 4l4 4m10-8l4 4l-4 4M14 4l-4 16" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" stroke-width="2"></path></svg>
                      &nbsp;Point
                    </span>
                  </a>
                  <div role="separator" style="line-height: 24px">&zwj;</div>
                  <h1 style="margin: 0 0 24px; font-size: 24px; line-height: 32px; font-weight: 600; color: #0f172a">
                    Confirm your subscription
                  </h1>
                  <p style="margin: 0 0 24px; font-size: 16px; line-height: 24px; color: #475569">
                    {{encode intro}}
                  </p>
                  <div>
                    <a href="{{encode url}}" style="display: inline-block; text-decoration: none; padding: 16px 24px; font-size: 16px; line-height: 1; border-radius: 4px; color: #fffffe; background-color: #020617" class="hover-bg-slate-800">
                      <!--[if mso]><i style="mso-font-width: 150%; mso-text-raise: 31px" hidden>&emsp;</i><![endif]-->
                      <span style="mso-text-raise: 16px">Confirm subscription</span>
                      <!--[if mso]><i hidden style="mso-font-width: 150%">&emsp;&#8203;</i><![endif]-->
                    </a>
                  </div>
                  <div role="separator" style="line-height: 24px">&zwj;</div>
                  <p style="margin: 0; font-size: 16px; line-height: 24px; color: #475569">
                    {{encode ignore}}
                  </p>
                  <div role="separator" style="height: 1px; line-height: 1px; background-color: #cbd5e1; margin-top: 24px; margin-bottom: 24px">&zwj;</div>
                  <p style="margin: 0; font-size: 12px; line-height: 20px; color: #475569; word-break: break-all">
                    If the button doesn't work, copy this link into your browser:
                    <a href="{{encode url}}" style="color: #1e293b; text-decoration: underline">{{encode url}}</a>
                  </p>
                </td>
              </tr>
            </table>
            <table style="width: 100%" cellpadding="0" cellspacing="0" role="none">
              <tr>
                <td class="sm-px-6" style="padding: 24px 36px">
                  <p style="margin: 0; font-size: 12px; color: #64748b">
                    {{encode copyright}}
                  </p>
                </td>
              </tr>
            </table>
          </td>
        </tr>
      </table>
    </div>
  </div>
</body>
</html>
"""

    let text =
      String.concat
        "\n"
        [ "Confirm your subscription"
          ""
          intro
          ""
          $"Confirm subscription: {url}"
          ""
          ignore
          ""
          copyright
          "" ]

    { Subject = subject
      Html = html
      Text = text }
