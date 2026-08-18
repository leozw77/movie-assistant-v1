import { render } from "preact";
import { describe, expect, it } from "vitest";

import type { Comment } from "@/modules/subject/domain";
import { useResolvedComments } from "@/modules/subject/runtime/use-resolved-comments";

const makeComment = (overrides?: Partial<Comment>): Comment => ({
  avatar: "https://example.com/raw-avatar.jpg",
  cid: "resolved-comment",
  content: "comment",
  link: "https://www.douban.com/people/no-avatar-lookup/",
  name: "Resolved viewer",
  ratingWord: "recommended",
  stars: 4,
  time: "2026-07-16",
  voted: false,
  votes: 1,
  ...overrides,
});

const ResolvedCommentsProbe = ({ comments }: { comments: Comment[] }) => {
  const resolvedComments = useResolvedComments(comments, document);
  return (
    <output data-avatars={resolvedComments.map(({ avatar }) => avatar).join("|")} />
  );
};

describe("Resolved comments runtime hook", () => {
  it("keeps extracted comment avatars without profile-page requests", () => {
    const comments = [
      makeComment({ cid: "resolved-first" }),
      makeComment({
        avatar: "https://example.com/second-avatar.jpg",
        cid: "resolved-second",
      }),
    ];
    const root = document.createElement("div");
    render(<ResolvedCommentsProbe comments={comments} />, root);

    expect(root.querySelector("output")?.dataset.avatars).toBe(
      "https://example.com/raw-avatar.jpg|https://example.com/second-avatar.jpg"
    );
  });
});
