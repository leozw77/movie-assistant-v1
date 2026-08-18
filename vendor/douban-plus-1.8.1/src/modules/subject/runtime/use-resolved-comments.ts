import type { Comment } from "@/modules/subject/domain";

const useResolvedComments = (comments: Comment[], _doc: Document): Comment[] => comments;

export { useResolvedComments };
