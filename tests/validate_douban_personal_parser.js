const fs = require("fs");
const path = require("path");
const vm = require("vm");

const sourcePath = path.join(__dirname, "..", "WebAssets", "DoubanPlus", "douban-personal-source-bridge.js");
const labelsPath = path.join(__dirname, "..", "WebAssets", "DoubanPlus", "douban-country-labels.js");
const labels = fs.readFileSync(labelsPath, "utf8");
const source = fs.readFileSync(sourcePath, "utf8");
const exposed = source.replace(
  "window.QbDoubanPersonalSourceBridge = Object.freeze({",
  "window.QbDoubanPersonalSourceBridge = Object.freeze({ __personalFields: personalFields, __readItem: readItem,"
);
const window = {};
const context = {
  window,
  location: { hostname: "movie.douban.com", pathname: "/people/1/collect", href: "https://movie.douban.com/people/1/collect" },
  document: { querySelector: () => null, querySelectorAll: () => [], body: { textContent: "" } },
  URL,
  Set,
  String,
  Number,
  Boolean,
  console
};
vm.runInNewContext(`${labels}\n${exposed}`, context, { filename: sourcePath });

const emptyItem = { querySelector: () => ({ textContent: "" }), querySelectorAll: () => [] };
const personalFields = window.QbDoubanPersonalSourceBridge.__personalFields;
const cases = [
  ["2025 / 中国香港 / 中国台湾 / 爱情 / 奇幻 / 许光汉 / 袁澧林", "2025 / 中国香港 / 中国台湾", ["许光汉", "袁澧林"], ""],
  ["2026 / 中国台湾 / 美国 / 剧情 / 科幻 / 惊悚 / 中国大陆 / 瑞恩·高斯林 / 桑德拉·布洛克", "2026 / 中国台湾 / 美国 / 中国大陆", ["瑞恩·高斯林", "桑德拉·布洛克"], ""],
  ["2023-03-19(瑞士影展) / 吴慷仁 / 陈泽耀 / 马来西亚 / 王礼霖 / 115分钟 / 剧情 / 王礼霖 Jin Ong / 马来语", "2023 / 马来西亚", ["吴慷仁", "陈泽耀"], "王礼霖"],
  ["2011-10-02(美国) / 克莱尔·丹尼斯 / 戴米恩·路易斯 / 曼迪·帕廷金 / 美国 / www.sho.com/sho/homeland/home / 迈克尔·科斯塔 / 丹尼尔·艾提奥斯 / 国土安全 / 55分钟 / 国土安全 / 剧情 / 英语", "2011 / 美国", ["克莱尔·丹尼斯", "戴米恩·路易斯"], "迈克尔·科斯塔"],
  ["2014-10-15(日本) / 泽尻英龙华 / 木村佳乃 / 仓科加奈 / 日本 / www.fujitv.co.jp/firstclass2/ / 宫胁亮 / 54分钟 / First Class 2 / 剧情 / 及川博則 / 日语", "2014 / 日本", ["泽尻英龙华", "木村佳乃"], "宫胁亮"]
];
for (const [intro, expectedIdentity, expectedCast, expectedDirector] of cases) {
  const actual = personalFields(emptyItem, { textContent: intro });
  if (actual.identity !== expectedIdentity || JSON.stringify(actual.cast) !== JSON.stringify(expectedCast) || actual.director !== expectedDirector) {
    throw new Error(`personal parse mismatch: ${intro} -> ${JSON.stringify(actual)}`);
  }
}

const titleAnchor = {
  href: "https://movie.douban.com/subject/1234567/",
  title: "好孩子 / A Good Child [可播放]",
  textContent: "",
  getAttribute(name) { return name === "href" ? this.href : name === "title" ? this.title : null; },
  querySelector(selector) { return selector === "em" ? { textContent: "好孩子" } : null; }
};
const titleItem = {
  querySelector(selector) {
    if (selector.includes(".pic a[href*='/subject/']")) return titleAnchor;
    if (selector.includes(".title a[href*='/subject/']")) return null;
    if (selector.includes("a.title[href*='/subject/']")) return titleAnchor;
    if (selector === ".intro") return { textContent: "2025 / 新加坡 / 剧情 / 同性" };
    if (selector === ".title") return { textContent: "" };
    return null;
  },
  querySelectorAll: () => []
};
const parsedTitle = window.QbDoubanPersonalSourceBridge.__readItem(titleItem, "collect", "https://movie.douban.com/people/1/collect").title;
if (parsedTitle !== "好孩子") throw new Error(`title parse mismatch: ${parsedTitle}`);

console.log(`Douban personal parser regression: PASS (${cases.length + 1}/${cases.length + 1})`);
