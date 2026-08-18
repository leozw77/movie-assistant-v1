import { describe, expect, it } from "vitest";

import { extractSearchResults } from "@/modules/subject/runtime/search-page-mount";

describe("search page result extraction", () => {
  it("accepts relative and protocol-relative Douban subject links", () => {
    document.head.innerHTML =
      '<base href="https://search.douban.com/movie/subject_search?search_text=%E8%9C%98%E8%9B%9B%E4%BE%A0">';
    document.body.innerHTML = `
      <div class="item-root">
        <a href="/subject/1295644/">蜘蛛侠</a>
        <img src="https://img.example.test/spider-man.jpg">
      </div>
      <div class="result">
        <a href="//movie.douban.com/subject/36246195/?from=search">蜘蛛侠：崭新之日</a>
      </div>
      <a href="https://example.test/subject/1/">不应纳入</a>
    `;

    expect(extractSearchResults(document)).toEqual([
      expect.objectContaining({
        subjectId: "1295644",
        subjectUrl: "https://movie.douban.com/subject/1295644/",
        title: "蜘蛛侠",
      }),
      expect.objectContaining({
        subjectId: "36246195",
        subjectUrl: "https://movie.douban.com/subject/36246195/",
        title: "蜘蛛侠：崭新之日",
      }),
    ]);
  });

  it("extracts rating, vote count, year, playback state and facts separately", () => {
    document.head.innerHTML =
      '<base href="https://search.douban.com/movie/subject_search?search_text=%E8%9C%98%E8%9B%9B%E4%BE%A0">';
    document.body.innerHTML = `
      <div class="item-root">
        <div class="pic">
          <a href="/subject/1306612/"><img src="https://img.example.test/spider.jpg"></a>
        </div>
        <div class="detail">
          <div class="title"><a href="/subject/1306612/">蜘蛛侠 Spider-Man (2002)</a></div>
          <div class="rating-info">
            <span class="rating_nums">8.1</span>
            <span class="rating_people">(429308人评价)</span>
          </div>
          <div class="abstract">美国 / 动作 / 科幻 / 冒险 / 121分钟</div>
          <div class="subject-cast">山姆·雷米 / 托比·马奎尔 / 威廉·达福</div>
          <span class="playable">可播放</span>
        </div>
      </div>
    `;

    expect(extractSearchResults(document)).toEqual([
      expect.objectContaining({
        cast: "山姆·雷米 / 托比·马奎尔 / 威廉·达福",
        facts: expect.arrayContaining(["美国", "动作", "科幻", "冒险", "121分钟"]),
        score: 8.1,
        title: "蜘蛛侠 Spider-Man",
        voteCount: 429308,
        year: "2002",
      }),
    ]);
  });

  it("keeps a missing rating as an explicit empty value", () => {
    document.head.innerHTML =
      '<base href="https://search.douban.com/movie/subject_search?search_text=%E6%9C%AA%E7%9F%A5">';
    document.body.innerHTML = `
      <div class="item-root">
        <a class="title" href="/subject/9999999/">未上映作品 (2027)</a>
        <div class="abstract">中国 / 剧情</div>
      </div>
    `;

    expect(extractSearchResults(document)[0]).toEqual(
      expect.objectContaining({
        score: null,
        voteCount: null,
        year: "2027",
      })
    );
  });
});
