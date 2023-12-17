import { FC } from "react";
import { Topic } from "@/models/topic";
import TopicSummary from "./topic-summary";

export interface TopicListProps {
  topics: Topic[];
}

const TopicList: FC<TopicListProps> = ({ topics }) => {
  return (
    <article className="grid max-[500px]:grid-cols-1 grid-cols-2 md:grid-cols-3 gap-4">
      {topics.map(t => <TopicSummary key={t.slug} topic={t} />)}
    </article>
  );
};

export default TopicList;
