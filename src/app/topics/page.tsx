import TopicList from "@/components/topic-list";
import { AllTopics } from "@/components/content/topics";
import Page from "@/components/page";

export default function TopicsPage() {
  return (
    <Page title="All topics">
      <TopicList topics={AllTopics} />
    </Page>
  );
}
