import { FC } from "react";
import { Roboto } from "next/font/google";
import { Light as SyntaxHighlighter } from "react-syntax-highlighter";
import swift from "react-syntax-highlighter/dist/esm/languages/hljs/swift";
import atalierCaveDark from "react-syntax-highlighter/dist/esm/styles/hljs/atelier-cave-dark";
import PostContentHeading from "@/components/post-content-heading";
import { Post } from "@/models/post";
import { TopicSummary } from "../topic";

SyntaxHighlighter.registerLanguage("swift", swift);

const robotoBold = Roboto({ subsets: ["latin"], weight: "700" });

export const PostSummary: Post = {
  type: "blog",
  slug: "securely-save-a-swiftui-view-as-a-password-protected-pdf",
  title: "Securely save a SwiftUI view as a password-protected PDF",
  img: {
    type: "icon",
    src: "/img/swiftui.png",
    alt: "SwiftUI logo",
  },
  date: new Date(),
  topics: [TopicSummary],
};

export const PostContent: FC = () => {
  return (
    <article className="flex flex-col min-h-screen w-full">
      <PostContentHeading summary={PostSummary} />
      <section className="flex flex-col gap-4">
        <p>
          In order to develop the secure PDF seed phrase backup feature for
          Nighthawk Wallet 2.0, I needed to figure out how to render a SwiftUI
          view as a PDF and password protect it with a user-supplied password.
          It seemed like a decently-sized effort, so I decided to break it down
          into the following steps that I would figure out one-by-one:
        </p>

        <ul className="list-decimal pl-8">
          <li>Get password from user input</li>
          <li>Render the view to a PDF file</li>
          <li>Password protect the PDF file</li>
          <li>Save the PDF file via a share sheet</li>
        </ul>

        <section className="flex flex-col gap-4">
          <p className={`${robotoBold.className} text-xl`}>
            Get password from user input
          </p>

          <p>
            Starting with a new empty iOS app project in Xcode, let&apos;s start
            by creating a view with some security-sensitive content.
          </p>

          <SyntaxHighlighter language="swift" style={atalierCaveDark}>
            {`
  struct SecretsNoOneShouldKnowView: View {
    var body: some View {
      VStack {
        Text("")
      }
    }
  }
              `}
          </SyntaxHighlighter>
        </section>
      </section>
    </article>
  );
};
