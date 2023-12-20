import { FC } from "react";
import { Roboto } from "next/font/google";
import Image from "next/image";
import { Light as SyntaxHighlighter } from "react-syntax-highlighter";
import swift from "react-syntax-highlighter/dist/esm/languages/hljs/swift";
import PostContentHeading from "@/components/post-content-heading";
import Code from "@/components/code";
import { Post } from "@/models/post";
import { TopicSummary } from "../topic";
import { getAssetsDirectory } from "@/models/constants";

SyntaxHighlighter.registerLanguage("swift", swift);

const robotoBold = Roboto({ subsets: ["latin"], weight: "700" });

export const PostSummary: Post = {
  type: "blog",
  slug: "securely-save-a-swiftui-view-as-a-password-protected-pdf",
  title: "Securely save a SwiftUI view as a password-protected PDF",
  img: {
    type: "icon",
    src: "https://twopointwebsite.blob.core.windows.net/logos/swiftui.png",
    alt: "SwiftUI logo",
  },
  date: new Date('2023-12-20T14:00:00'), // Dec 20, 2023 @ 8am local time
  topics: [TopicSummary],
};

const assetsDir = getAssetsDirectory(PostSummary);

export const PostContent: FC = () => {
  return (
    <article className="flex flex-col min-h-screen w-full">
      <PostContentHeading summary={PostSummary} className="pb-2" />
      <section className="flex flex-col gap-8">
        <p>
          In order to develop the secure PDF seed phrase backup feature for
          Nighthawk Wallet 2.0, I needed to figure out how to render a SwiftUI
          view as a PDF and password protect it with a user-supplied password.
          It seemed like a decently-sized effort, so I decided to break it down
          into the following steps that I would figure out one-by-one:
        </p>

        <ul className="list-decimal pl-8">
          <li>Get password from user input</li>
          <li>Render the view to a PDF file sharable via a share sheet</li>
          <li>Password protect the PDF file</li>
        </ul>

        <section className="flex flex-col gap-4">
          <p className={`${robotoBold.className} text-xl`}>
            Our super secret content
          </p>

          <p>
            Starting with a new empty iOS app project in Xcode, let&apos;s
            create a view with some security-sensitive content:
          </p>

          <Code language="swift">
            {`
  struct SecretsView: View {
      var body: some View {
          VStack(spacing: 16) {
              Text("KEEP SECRET:")
                  .font(.largeTitle)
              Text("My phone number is (555) 555-5555")
              Text("The one password I use for everything is M4tTiZcOoL123")
              Text("I'm actually a really big Taylor Swift fan")
          }
      }
  }
  `}
          </Code>

          <p>And our ContentView:</p>

          <Code language="swift">
            {`
  struct ContentView: View {
      var body: some View {
          VStack {
              SecretsView()
          }
          .padding()
      }
  }
  `}
          </Code>

          <p>Which should look like this:</p>

          <Image
            unoptimized
            src={`${assetsDir}/secrets-view.jpg`}
            alt="SecretsView running in an Xcode Preview"
            width="0"
            height="0"
            sizes="100vw"
            className="w-full h-auto"
          />
        </section>

        <section className="flex flex-col gap-4">
          <p className={`${robotoBold.className} text-xl`}>
            Get password from user input
          </p>

          <p>
            Let&apos;s add some state to our ContentView to hold the user&apos;s
            password, a SecureField to allow the user to enter a password, and
            some extra spacing to give everything some room to breathe:
          </p>

          <Code language="swift">
            {`
  struct ContentView: View {
      @State private var password = ""

      var body: some View {
          VStack(alignment: .center, spacing: 16) {
              SecretsView()
          
              SecureField("Password", text: $password)
                  .multilineTextAlignment(.center)
          }
          .padding()
      }
  }
  `}
          </Code>

          <p>We should now have:</p>

          <Image
            unoptimized
            src={`${assetsDir}/password-state-and-secure-field.jpg`}
            alt="ContentView with state for password and a SecureField running in an Xcode Preview"
            width="0"
            height="0"
            sizes="100vw"
            className="w-full h-auto"
          />
        </section>

        <section className="flex flex-col gap-4">
          <p className={`${robotoBold.className} text-xl`}>
            Render the view to a PDF file sharable via a share sheet
          </p>

          <p>
            For this next step, I started with a web search for &ldquo;Save a
            SwiftUI view to a PDF&rdquo;, which brought me to Paul Hudson&apos;s{" "}
            <a
              href="https://www.hackingwithswift.com/quick-start/swiftui/how-to-render-a-swiftui-view-to-a-pdf"
              target="_blank"
              className="link"
            >
              Hacking with Swift article
            </a>{" "}
            on the subject, which provided my starting point. Let&apos;s add the
            following to ContentView:
          </p>

          <Code language="swift">
            {`
  func render() -> URL {
      let renderer = ImageRenderer(content: SecretsView())
    
      let url = URL.documentsDirectory.appending(path: "output.pdf")
    
      renderer.render { size, context in
          var box = CGRect(x: 0, y: 0, width: size.width, height: size.height)
        
          guard let pdf = CGContext(url as CFURL, mediaBox: &box, nil) else {
              return
          }
        
          pdf.beginPDFPage(nil)
          context(pdf)
          pdf.endPDFPage()
          pdf.closePDF()
      }
    
      return url
  }
  `}
          </Code>

          <p>And then update ContentView&apos;s body like so:</p>

          <Code language="swift">
            {`
  var body: some View {
      VStack(alignment: .center, spacing: 16) {
          SecretsView()
        
          SecureField("Password", text: $password)
              .multilineTextAlignment(.center)
        
          ShareLink(item: render()) {
              Text("Securely save secrets")
          }
      }
      .padding()
  }
  `}
          </Code>

          <p>
            I made a few changes to suit our demo app&apos;s needs, namely, I
            changed the output file name and I used a different version of
            ShareLink that takes content, since the one used in HWS includes a
            share icon, which I didn&apos;t want. I strongly encourage you to
            read the original article, as it steps through what this code is
            doing in far greater detail.
          </p>

          <p>But with that, we should have the following:</p>

          <Image
            unoptimized
            src={`${assetsDir}/share-link.jpg`}
            alt="Screenshot of Xcode with share sheet code wired up running in an Xcode Preview"
            width="0"
            height="0"
            sizes="100vw"
            className="w-full h-auto"
          />

          <p>
            Firing up the simulator or a physical device, you should see the
            following:
          </p>

          <div className="w-full flex flex-row justify-center">
            <Image
              unoptimized
              src={`${assetsDir}/share-link-demo.webp`}
              alt="Screen recording of entering a password (that does nothing yet) and launching a share sheet with the SwiftUI view as a PDF"
              width="0"
              height="0"
              sizes="25vw"
              className="w-3/4 md:w-1/4 h-auto"
            />
          </div>
        </section>

        <section className="flex flex-col gap-4">
          <p className={`${robotoBold.className} text-xl`}>
            Password protect the PDF file
          </p>

          <p>
            The final step is to figure out how to apply the user&apos;s entered
            password to the PDF itself. With the code we have now, we do nothing
            with the password. Let&apos;s change that. In render, after the call
            to closePdf, add the following:
          </p>

          <Code language="swift">
            {`
  // Read as a PDF document and encrypt with the provided password
  guard let pdfDocument = PDFDocument(url: url) else { return }
  pdfDocument.write(
      to: url,
      withOptions: [
          PDFDocumentWriteOption.userPasswordOption : password,
          PDFDocumentWriteOption.ownerPasswordOption : password
      ]
  )
  `}
          </Code>

          <p>We&apos;ll also need to add the following import:</p>

          <Code language="swift">
            {`
  import PDFKit
  `}
          </Code>

          <p>This code leverages PDFKit to do the following:</p>

          <ul className="list-decimal pl-8">
            <li>
              Read the PDF data saved at output.pdf into a PDFDocument object
            </li>
            <li>
              Write the PDF document back to output.pdf, using write options to
              pass the input password
            </li>
          </ul>

          <p>
            And that&apos;s really it. Altogether, our render function should
            now look like this:
          </p>

          <Code language="swift">
            {`
  func render() -> URL {
      let renderer = ImageRenderer(content: SecretsView())
    
      let url = URL.documentsDirectory.appending(path: "output.pdf")
    
      renderer.render { size, context in
          var box = CGRect(x: 0, y: 0, width: size.width, height: size.height)
        
          guard let pdf = CGContext(url as CFURL, mediaBox: &box, nil) else {
              return
          }
        
          pdf.beginPDFPage(nil)
          context(pdf)
          pdf.endPDFPage()
          pdf.closePDF()
        
          // Read as a PDF document and encrypt with the provided password
          guard let pdfDocument = PDFDocument(url: url) else { return }
          pdfDocument.write(
              to: url,
              withOptions: [
                  PDFDocumentWriteOption.userPasswordOption : password,
                  PDFDocumentWriteOption.ownerPasswordOption : password
              ]
          )
      }
    
      return url
  }
  `}
          </Code>

          <p>
            And firing it up in the simulator, if I enter the very secure
            password &ldquo;password123&rdquo; and tap share, I can see that I
            can save the file to disk and view it only by entering the password:
          </p>

          <div className="w-full flex flex-row justify-center">
            <Image
              unoptimized
              src={`${assetsDir}/final-demo.webp`}
              alt="Screen recording of the final demo: entering a password, saving the PDF, and viewing the PDF by entering the password"
              width="0"
              height="0"
              sizes="25vw"
              className="w-3/4 md:w-1/4 h-auto"
            />
          </div>
        </section>

        <section className="flex flex-col gap-4">
          <p className={`${robotoBold.className} text-xl`}>
            A problem lurking in the shadows
          </p>

          <p>
            The eagle-eyed among you may have noticed an issue with the
            &ldquo;final&rdquo; code above. Before continuing, take a moment to
            see if you can figure it out. I&apos;ll wait.
          </p>

          <div className="w-full h-screen" />

          <p>Ok, that&apos;s enough time. Let&apos;s dig into it.</p>
        </section>

        <section className="flex flex-col gap-4">
          <p className={`${robotoBold.className} text-xl`}>
            A subtle security vulnerability
          </p>

          <p>
            The issue lies with our render function. Take another look at it and
            I&apos;ll give you one last chance to spot the bug:
          </p>

          <Code language="swift">
            {`
  func render() -> URL {
      let renderer = ImageRenderer(content: SecretsView())
    
      let url = URL.documentsDirectory.appending(path: "output.pdf")
    
      renderer.render { size, context in
          var box = CGRect(x: 0, y: 0, width: size.width, height: size.height)
        
          guard let pdf = CGContext(url as CFURL, mediaBox: &box, nil) else {
              return
          }
        
          pdf.beginPDFPage(nil)
          context(pdf)
          pdf.endPDFPage()
          pdf.closePDF()
        
          // Read as a PDF document and encrypt with the provided password
          guard let pdfDocument = PDFDocument(url: url) else { return }
          pdfDocument.write(
              to: url,
              withOptions: [
                  PDFDocumentWriteOption.userPasswordOption : password,
                  PDFDocumentWriteOption.ownerPasswordOption : password
              ]
          )
      }
    
      return url
  }
  `}
          </Code>

          <p>
            Spot it? It&apos;s really subtle, so you&apos;d be forgiven if you
            missed it &#40;I did&#41;. The issue lies here:
          </p>

          <Code language="swift">
            {`
  guard let pdf = CGContext(url as CFURL, mediaBox: &box, nil) else {
      return
  }
  `}
          </Code>

          <p>
            Here, we initialize a CGContext to render the PDF into. The problem
            is the initializer. Let&apos;s check out the{" "}
            <a
              href="https://developer.apple.com/documentation/coregraphics/cgcontext/1456290-init"
              target="_blank"
              className="link"
            >
              documentation
            </a>{" "}
            for it:
          </p>

          <Image
            unoptimized
            src={`${assetsDir}/cgcontext-init-docs.jpg`}
            alt="Screenshot of Apple's documentation for a URL-based CGContext"
            width="0"
            height="0"
            sizes="100vw"
            className="w-full h-auto"
          />

          <p>
            The key problem here is the <em>URL parameter</em>. When we render
            to a CGContext in this way, we are actually writing an unprotected
            version of our PDF to disk, after which we use PDFKit to overwrite
            that same file with the password protected version. In theory, an
            attacker could capture that unprotected file at some point between
            when we save it and overwrite it with the final version. Like I
            said, pretty subtle. Fortunately, the fix is pretty straightforward.
          </p>
        </section>

        <section className="flex flex-col gap-4">
          <p className={`${robotoBold.className} text-xl`}>
            Patching the vulnerability
          </p>

          <p>
            If the issue is that we are saving the PDF to a file and then
            overwriting that file, then it would seem that the solution is to
            write the PDF to some in-memory location, and then use PDFKit to
            securely save the password-protected version to disk. Handily,
            CoreGraphics provides an initializer for CGContext that allows us to
            do just that. Looking at the{" "}
            <a
              href="https://developer.apple.com/documentation/coregraphics/cgcontext/1454204-init"
              target="_blank"
              className="link"
            >
              documentation
            </a>{" "}
            , we find:
          </p>

          <Image
            unoptimized
            src={`${assetsDir}/cgcontext-init-data-consumer-docs.jpg`}
            alt="Screenshot of Apple's documentation for a URL-based CGContext"
            width="0"
            height="0"
            sizes="100vw"
            className="w-full h-auto"
          />

          <p>
            So instead of passing in a CFURL, it looks like we need to supply a
            CGDataConsumer. How do we do that? Well, since these are C APIs
            we&apos;re working with, it can be a bit verbose, but it can be broken down as follows:
          </p>

          <ul className="list-decimal pl-8">
            <li>
              Create a CFMutableData object that will be the buffer for the raw PDF bytes.
            </li>
            <li>
              Create a CGDataConsumer, passing in the CFMutableData object
            </li>
            <li>
              Create our new CGContext, passing in the CGDataConsumer object
            </li>
          </ul>
        </section>

        <section className="flex flex-col gap-4">
          <p className={`${robotoBold.className} text-xl`}>
            Applying the fix
          </p>

          <p>
            First, the CFMutableData:
          </p>

          <Code language="swift">
            {`
  guard let pdfData = CFDataCreateMutable(nil, 0) else { return }
  `}
          </Code>

          <p>
            Then, the CGDataConsumer:
          </p>

          <Code language="swift">
            {`
  guard let pdfDataConsumer = CGDataConsumer(data: pdfData),
  `}
          </Code>

          <p>
            Finally, the CGContext:
          </p>

          <Code language="swift">
            {`
  let pdf = CGContext(consumer: pdfDataConsumer, mediaBox: &box, nil)
  `}
          </Code>

          <p>Put together, the final, patched version of the render function:</p>

          <Code language="swift">
            {`
  func render() -> URL {
      let renderer = ImageRenderer(content: SecretsView())
    
      let url = URL.documentsDirectory.appending(path: "output.pdf")
    
      renderer.render { size, context in
          guard let pdfData = CFDataCreateMutable(nil, 0) else { return }
          var box = CGRect(x: 0, y: 0, width: size.width, height: size.height)
        
          guard let pdfDataConsumer = CGDataConsumer(data: pdfData),
              let pdf = CGContext(consumer: pdfDataConsumer, mediaBox: &box, nil) else {
              return
          }
        
          pdf.beginPDFPage(nil)
          context(pdf)
          pdf.endPDFPage()
          pdf.closePDF()
        
          // Read as a PDF document and encrypt with the provided password
          guard let pdfDocument = PDFDocument(url: url) else { return }
          pdfDocument.write(
              to: url,
              withOptions: [
                  PDFDocumentWriteOption.userPasswordOption : password,
                  PDFDocumentWriteOption.ownerPasswordOption : password
              ]
          )
      }
    
      return url
  }
  `}
          </Code>

          <p>And that&apos;s it! We have now securely saved a SwiftUI view to a password-protected PDF file, minimal heartache.</p>
        </section>
      </section>
    </article>
  );
};
